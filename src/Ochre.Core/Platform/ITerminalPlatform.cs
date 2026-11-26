namespace Ochre.Core.Platform;

/// <summary>A shell the user can open a terminal with. The renderer picks one by <see cref="Id"/>; it never sends a command line.</summary>
/// <param name="Executable">An absolute path, so starting it never searches the current directory.</param>
public sealed record ShellProfile(string Id, string Name, string Executable, string Arguments);

/// <summary>A child process attached to a pseudoconsole.</summary>
public interface IPseudoConsole : IDisposable
{
    /// <summary>Queues input for the child. Never blocks, so the UI thread can call it.</summary>
    void Write(string data);

    void Resize(int cols, int rows);
}

/// <summary>The OS side of terminals: which shells exist, and starting one in a pseudoconsole.</summary>
public interface ITerminalPlatform
{
    IReadOnlyList<ShellProfile> DetectShells();

    /// <summary>
    /// Starts <paramref name="shell"/> in <paramref name="folder"/>. <paramref name="onOutput"/> is called on a reader
    /// thread with decoded text, in order; <paramref name="onExited"/> is called once, after the last output, with
    /// the exit code (or -1 when it can't be read), whether the shell ended by itself or the console was disposed.
    /// </summary>
    IPseudoConsole Start(ShellProfile shell, string folder, int cols, int rows, Action<string> onOutput, Action<int> onExited);
}
