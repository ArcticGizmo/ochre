using System.Security.Cryptography;
using System.Text;

namespace Ochre.Core;

/// <summary>
/// Which installation this process belongs to. A dev build runs as the "dev" profile so it never shares tasks,
/// pipes or windows with the copy being used day to day.
/// </summary>
public sealed class OchreProfile
{
    private OchreProfile(string name) => Name = name;

    /// <summary>Empty for the normal profile.</summary>
    public string Name { get; }

    public bool IsDefault => Name.Length == 0;

    /// <summary>"Ochre" or "Ochre-dev": the folder name under the app data roots.</summary>
    public string AppFolder => IsDefault ? "Ochre" : $"Ochre-{Name}";

    public static OchreProfile Current { get; } = Resolve();

    private static OchreProfile Resolve()
    {
        var fromEnv = Environment.GetEnvironmentVariable("OCHRE_PROFILE");
        if (fromEnv is not null) return new OchreProfile(Sanitise(fromEnv));
#if DEBUG
        return new OchreProfile("dev");
#else
        return new OchreProfile("");
#endif
    }

    private static string Sanitise(string name) =>
        new(name.Trim().ToLowerInvariant().Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-').Take(32).ToArray());

    /// <summary>
    /// A machine-wide name (pipe, mutex) unique to this user and profile. Pipe names are global, so the user
    /// part keeps two signed-in users apart without putting their name in it.
    /// </summary>
    public string MachineName(string purpose)
    {
        var user = Environment.UserDomainName + "\\" + Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..12].ToLowerInvariant();
        return IsDefault ? $"ochre-{purpose}-{hash}" : $"ochre-{Name}-{purpose}-{hash}";
    }
}
