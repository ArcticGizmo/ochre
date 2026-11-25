using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Ochre.Core.Hosting;

/// <summary>
/// Keeps Ochre single-instance per user and profile. A later launch sends its command (open the window, close it,
/// or a dev command) to the running instance over a per-user pipe and prints the reply. One JSON line each way.
/// </summary>
public sealed class InstanceChannel : IDisposable
{
    private const int MaxLine = 1024 * 1024;

    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private InstanceChannel(string pipeName, Mutex mutex)
    {
        _pipeName = pipeName;
        _mutex = mutex;
    }

    /// <summary>Returns the channel if this process is the first instance, else null.</summary>
    public static InstanceChannel? Claim(OchreProfile profile)
    {
        var pipeName = profile.MachineName("instance");
        var mutex = new Mutex(true, $@"Local\{pipeName}", out var first);
        if (first) return new InstanceChannel(pipeName, mutex);
        mutex.Dispose();
        return null;
    }

    /// <summary>Sends a command to the running instance. Returns its reply, or null if none answered.</summary>
    public static string? Send(OchreProfile profile, string command, string? argument, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", profile.MachineName("instance"), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect((int)TimeSpan.FromSeconds(3).TotalMilliseconds);
            WriteLine(client, w =>
            {
                w.WriteString("command", command);
                if (argument is null) w.WriteNull("argument");
                else w.WriteString("argument", argument);
            });
            var reply = ReadLineAsync(client, CancellationToken.None).WaitAsync(timeout).GetAwaiter().GetResult();
            if (reply is null) return null;
            using var doc = JsonDocument.Parse(reply);
            return doc.RootElement.GetProperty("reply").GetString();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or JsonException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Handles each command another launch sends, on a pool thread. The handler's string is the reply.</summary>
    public void Listen(Func<string, string?, Task<string>> handler)
    {
        var ct = _stop.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    var line = await ReadLineAsync(server, ct).ConfigureAwait(false);
                    if (line is null) continue;

                    string command;
                    string? argument;
                    using (var doc = JsonDocument.Parse(line))
                    {
                        command = doc.RootElement.GetProperty("command").GetString() ?? "";
                        argument = doc.RootElement.TryGetProperty("argument", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                    }

                    string reply;
                    try
                    {
                        reply = await handler(command, argument).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                    {
                        reply = $"error: {ex.Message}";
                    }
                    WriteLine(server, w => w.WriteString("reply", reply));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    // A client that hung up or sent garbage; wait for the next one.
                }
            }
        }, ct);
    }

    private static void WriteLine(Stream stream, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        stream.Write(buffer.WrittenSpan);
        stream.WriteByte((byte)'\n');
        stream.Flush();
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var bytes = new MemoryStream();
        var one = new byte[1];
        while (bytes.Length < MaxLine)
        {
            var read = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (read == 0) return bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
            bytes.WriteByte(one[0]);
        }
        return null;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
