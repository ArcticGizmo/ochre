using System.IO.Pipes;
using System.Text;

namespace Ochre.Core.Hosting;

public enum InstanceCommand
{
    OpenWindow,
    CloseWindow,
}

/// <summary>
/// Keeps Ochre single-instance per user and profile. A second launch hands its command to the running instance
/// over a per-user pipe and exits.
/// </summary>
public sealed class InstanceChannel : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private InstanceChannel(string pipeName, Mutex mutex)
    {
        _pipeName = pipeName;
        _mutex = mutex;
    }

    /// <summary>Returns the channel if this process is the first instance; otherwise sends the command and returns null.</summary>
    public static InstanceChannel? ClaimOrForward(OchreProfile profile, InstanceCommand command)
    {
        var pipeName = profile.MachineName("instance");
        var mutex = new Mutex(true, $@"Local\{pipeName}", out var first);
        if (first) return new InstanceChannel(pipeName, mutex);

        mutex.Dispose();
        Forward(pipeName, command);
        return null;
    }

    private static void Forward(string pipeName, InstanceCommand command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            client.Write(Encoding.ASCII.GetBytes(command == InstanceCommand.CloseWindow ? "close\n" : "open\n"));
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Runs <paramref name="onCommand"/> on a pool thread for each command another launch forwards.</summary>
    public void Listen(Action<InstanceCommand> onCommand)
    {
        var ct = _stop.Token;
        _ = Task.Run(async () =>
        {
            var buffer = new byte[16];
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    var read = await server.ReadAsync(buffer, ct).ConfigureAwait(false);
                    var text = buffer.AsSpan(0, read);
                    if (text.StartsWith("open"u8)) onCommand(InstanceCommand.OpenWindow);
                    else if (text.StartsWith("close"u8)) onCommand(InstanceCommand.CloseWindow);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                }
            }
        }, ct);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
