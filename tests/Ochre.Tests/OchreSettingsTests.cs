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
    public void A_module_choice_is_unset_until_made_and_then_persists()
    {
        Assert.Null(new OchreSettings(Path_).ModuleEnabled("terminals"));
        new OchreSettings(Path_).SetModules(new Dictionary<string, bool> { ["terminals"] = false, ["shutdown-park"] = true });
        var reread = new OchreSettings(Path_);
        Assert.False(reread.ModuleEnabled("terminals"));
        Assert.True(reread.ModuleEnabled("shutdown-park"));
    }

    [Fact]
    public void The_old_auto_park_setting_turns_its_module_on_until_the_user_chooses()
    {
        File.WriteAllText(Path_, """{"autoParkOnShutdown":true}""");
        Assert.True(new OchreSettings(Path_).ModuleEnabled("shutdown-park"));
        new OchreSettings(Path_).SetModules(new Dictionary<string, bool> { ["shutdown-park"] = false });
        Assert.False(new OchreSettings(Path_).ModuleEnabled("shutdown-park"));
        Assert.DoesNotContain("autoParkOnShutdown", File.ReadAllText(Path_), StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_it_doesnt_know_survive_a_save()
    {
        File.WriteAllText(Path_, """{"fromANewerVersion":{"x":1},"modules":{"fromANewerModule":true}}""");
        new OchreSettings(Path_).SetModules(new Dictionary<string, bool> { ["terminals"] = true });
        var text = File.ReadAllText(Path_);
        Assert.Contains("fromANewerVersion", text, StringComparison.Ordinal);
        Assert.Contains("fromANewerModule", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_it_cant_parse_is_set_aside_rather_than_overwritten()
    {
        File.WriteAllText(Path_, "{ not json");
        var settings = new OchreSettings(Path_);
        Assert.Null(settings.ModuleEnabled("terminals"));
        Assert.Equal("{ not json", File.ReadAllText(Path_ + ".bad"));
    }
}
