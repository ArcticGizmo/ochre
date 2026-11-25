namespace Ochre.Host;

/// <summary>Finds the console CLI agents use to reach their task.</summary>
internal static class AgentCli
{
    /// <summary><c>OCHRE_CLI_EXE</c>, else <c>ochre.com</c> or <c>ochre-cli.exe</c> beside the host; in a dev build,
    /// also this checkout's CLI build. Null when none exists.</summary>
    public static string? Locate()
    {
        if (Environment.GetEnvironmentVariable("OCHRE_CLI_EXE") is { Length: > 0 } fromEnv && Path.IsPathFullyQualified(fromEnv) && File.Exists(fromEnv))
            return fromEnv;
        foreach (var name in new[] { "ochre.com", "ochre-cli.exe" })
        {
            var beside = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(beside)) return beside;
        }
#if DEBUG
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Ochre.slnx"))) continue;
            var dev = Path.Combine(dir.FullName, "src", "Ochre.Cli", "bin", "Debug", "net10.0", "ochre-cli.exe");
            return File.Exists(dev) ? dev : null;
        }
#endif
        return null;
    }
}
