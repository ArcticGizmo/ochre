using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Spike.Core;

/// <summary>A second launch asks the running instance to open a window, then exits.</summary>
public static class SingleInstance
{
    // Pipe names are machine-global, so a per-user suffix keeps two signed-in users from colliding.
    public static string PipeName(string app)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..12].ToLowerInvariant();
        return $"{app}-{hash}";
    }

    /// <summary>"open" by default; "--close" on the command line closes (or hides) the window, for timing reopen.</summary>
    public static void Signal(string pipeName, string[] args)
    {
        var command = args.Contains("--close") ? "close\n"u8 : "open\n"u8;
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            client.Write(command);
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
    }

    public static void Listen(string pipeName, Action onOpen, Action onClose, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            var buffer = new byte[16];
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    var read = await server.ReadAsync(buffer, ct).ConfigureAwait(false);
                    var text = buffer.AsSpan(0, read);
                    if (text.StartsWith("open"u8)) onOpen();
                    else if (text.StartsWith("close"u8)) onClose();
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
}
