using Ochre.Core;
using Xunit;

namespace Ochre.Tests;

/// <summary>The settings file never loses what it doesn't understand (data-loss boundary).</summary>
public sealed class OchreSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ochre-settings-").FullName;
    private string Path_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Auto_park_is_off_until_turned_on_and_the_choice_persists()
    {
        Assert.False(new OchreSettings(Path_).AutoParkOnShutdown);
        new OchreSettings(Path_).AutoParkOnShutdown = true;
        Assert.True(new OchreSettings(Path_).AutoParkOnShutdown);
    }

    [Fact]
    public void Keys_it_doesnt_know_survive_a_save()
    {
        File.WriteAllText(Path_, """{"fromANewerVersion":{"x":1}}""");
        new OchreSettings(Path_).AutoParkOnShutdown = true;
        var text = File.ReadAllText(Path_);
        Assert.Contains("fromANewerVersion", text, StringComparison.Ordinal);
        Assert.Contains("autoParkOnShutdown", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_it_cant_parse_is_set_aside_rather_than_overwritten()
    {
        File.WriteAllText(Path_, "{ not json");
        var settings = new OchreSettings(Path_);
        Assert.False(settings.AutoParkOnShutdown);
        Assert.Equal("{ not json", File.ReadAllText(Path_ + ".bad"));
    }
}
