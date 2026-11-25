namespace Ochre.Core;

/// <summary>Where Ochre keeps its data. Roaming data (tasks, settings) is separate from local caches and logs.</summary>
public sealed class OchrePaths
{
    public OchrePaths(OchreProfile profile)
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), profile.AppFolder),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), profile.AppFolder))
    {
    }

    public OchrePaths(string roamingRoot, string localRoot)
    {
        RoamingRoot = roamingRoot;
        LocalRoot = localRoot;
    }

    public string RoamingRoot { get; }
    public string LocalRoot { get; }

    public string Tasks => Path.Combine(RoamingRoot, "tasks");
    public string Settings => Path.Combine(RoamingRoot, "settings.json");
    public string Logs => Path.Combine(LocalRoot, "logs");
    public string WebView => Path.Combine(LocalRoot, "webview");
    public string Terminals => Path.Combine(LocalRoot, "terminals");
    public string SessionLedger => Path.Combine(LocalRoot, "sessions.json");
}
