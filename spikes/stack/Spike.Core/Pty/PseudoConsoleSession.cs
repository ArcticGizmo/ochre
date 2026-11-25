using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Spike.Core.Pty;

/// <summary>A child process attached to a ConPTY. Output arrives on a dedicated thread.</summary>
public sealed class PseudoConsoleSession : IDisposable
{
    private readonly nint _pseudoConsole;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly Process? _process;
    private readonly Lock _inputGate = new();
    private bool _disposed;

    public int ProcessId { get; }
    public event Action<string>? Output;
    public event Action<int>? Exited;

    private PseudoConsoleSession(nint pseudoConsole, FileStream input, FileStream output, int processId)
    {
        _pseudoConsole = pseudoConsole;
        _input = input;
        _output = output;
        ProcessId = processId;
        try
        {
            _process = Process.GetProcessById(processId);
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => Exited?.Invoke(SafeExitCode(_process));
        }
        catch (ArgumentException)
        {
            // The child already exited before we could attach; the reader loop still drains its output.
        }
    }

    public static PseudoConsoleSession Start(string commandLine, string? workingDirectory, short cols, short rows)
    {
        if (!Native.CreatePipe(out var ptyInputRead, out var inputWrite, 0, 0) ||
            !Native.CreatePipe(out var outputRead, out var ptyOutputWrite, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed");
        }

        var hr = Native.CreatePseudoConsole(new Native.Coord { X = cols, Y = rows }, ptyInputRead, ptyOutputWrite, 0, out var hpc);
        Marshal.ThrowExceptionForHR(hr);

        var attrSize = (nint)0;
        Native.InitializeProcThreadAttributeList(0, 1, 0, ref attrSize);
        var attrList = Marshal.AllocHGlobal(attrSize);
        var cmd = Marshal.StringToHGlobalUni(commandLine);
        try
        {
            if (!Native.InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed");
            if (!Native.UpdateProcThreadAttribute(attrList, 0, Native.ProcThreadAttributePseudoConsole, hpc, IntPtr.Size, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed");

            var si = new Native.StartupInfoEx { AttributeList = attrList };
            si.StartupInfo.Cb = Marshal.SizeOf<Native.StartupInfoEx>();

            if (!Native.CreateProcess(null, cmd, 0, 0, false, Native.ExtendedStartupInfoPresent, 0, workingDirectory, ref si, out var pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed");

            Native.CloseHandle(pi.Thread);
            Native.CloseHandle(pi.Process);

            // ConPTY holds its own duplicates; keeping ours open would stop the output pipe from ever closing.
            ptyInputRead.Dispose();
            ptyOutputWrite.Dispose();

            var session = new PseudoConsoleSession(
                hpc,
                new FileStream(inputWrite, FileAccess.Write, 0, false),
                new FileStream(outputRead, FileAccess.Read, 0, false),
                pi.ProcessId);
            session.StartReader();
            return session;
        }
        finally
        {
            Native.DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
            Marshal.FreeHGlobal(cmd);
        }
    }

    public void Write(string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        lock (_inputGate)
        {
            if (_disposed) return;
            _input.Write(bytes);
            _input.Flush();
        }
    }

    public void Resize(short cols, short rows) =>
        Native.ResizePseudoConsole(_pseudoConsole, new Native.Coord { X = cols, Y = rows });

    private void StartReader()
    {
        var thread = new Thread(ReadLoop) { IsBackground = true, Name = "pty-reader" };
        thread.Start();
    }

    private void ReadLoop()
    {
        // A stateful decoder, because a UTF-8 sequence can straddle two reads.
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[64 * 1024];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        try
        {
            int read;
            while ((read = _output.Read(bytes, 0, bytes.Length)) > 0)
            {
                var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                if (count > 0) Output?.Invoke(new string(chars, 0, count));
            }
        }
        catch (IOException)
        {
            // Pipe broken on close.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    public void Dispose()
    {
        lock (_inputGate)
        {
            if (_disposed) return;
            _disposed = true;
            _input.Dispose();
        }
        // Closing the pseudoconsole terminates the attached client and lets the reader drain to EOF.
        Native.ClosePseudoConsole(_pseudoConsole);
        _output.Dispose();
        _process?.Dispose();
    }
}
