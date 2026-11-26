using System.Reflection;
using System.Text.Json.Serialization;
using Ochre.Core.Bridge;
using Ochre.Core.Hosting;
using Ochre.Core.Platform;

namespace Ochre.Host.Bridge;

/// <param name="Modules">The ids of the modules running this session (ADR 0012); the UI shows only their contributions.</param>
public sealed record AppInfo(string Version, string Profile, IReadOnlyList<string> Modules);

/// <param name="Enabled">The user's choice, or the module's default; applies at the next start.</param>
/// <param name="Running">Running this session.</param>
/// <param name="Error">Why it failed to start this session, if it did.</param>
public sealed record ModuleDto(string Id, string Name, string Description, bool Enabled, bool Running, string? Error);

/// <param name="JumpHotkey">The "jump to task" shortcut, e.g. <c>Ctrl+Alt+Space</c>; "" when off.</param>
/// <param name="JumpHotkeyError">Why it isn't working (another app has it), or null.</param>
public sealed record SettingsDto(IReadOnlyList<ModuleDto> Modules, string JumpHotkey, string? JumpHotkeyError);

public sealed record ModuleChoiceDto(string Id, bool Enabled);

/// <param name="JumpHotkey">A new shortcut, "" to turn it off, or null to leave it; applies at once.</param>
public sealed record SettingsUpdateRequest(IReadOnlyList<ModuleChoiceDto> Modules, string? JumpHotkey);

/// <param name="NewWindow">Open a new browser window (middle click) rather than reuse the last one.</param>
public sealed record OpenUrlRequest(string Url, bool NewWindow);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Empty))]
[JsonSerializable(typeof(Ack))]
[JsonSerializable(typeof(AppInfo))]
[JsonSerializable(typeof(IReadOnlyList<TaskListItem>))]
[JsonSerializable(typeof(TaskDetail))]
[JsonSerializable(typeof(TaskIdRequest))]
[JsonSerializable(typeof(CreateTaskRequest))]
[JsonSerializable(typeof(UpdateTaskRequest))]
[JsonSerializable(typeof(SetStateRequest))]
[JsonSerializable(typeof(CheckItemRequest))]
[JsonSerializable(typeof(AddItemRequest))]
[JsonSerializable(typeof(TaskChangedEvent))]
[JsonSerializable(typeof(ParkTaskRequest))]
[JsonSerializable(typeof(IReadOnlyList<SessionSummary>))]
[JsonSerializable(typeof(SessionSnapshot))]
[JsonSerializable(typeof(SessionIdRequest))]
[JsonSerializable(typeof(StartSessionRequest))]
[JsonSerializable(typeof(StartSessionResult))]
[JsonSerializable(typeof(SendPromptRequest))]
[JsonSerializable(typeof(RespondRequest))]
[JsonSerializable(typeof(SetModelRequest))]
[JsonSerializable(typeof(SetEffortRequest))]
[JsonSerializable(typeof(BindSessionRequest))]
[JsonSerializable(typeof(SettingsDto))]
[JsonSerializable(typeof(SettingsUpdateRequest))]
[JsonSerializable(typeof(OpenUrlRequest))]
[JsonSerializable(typeof(SessionUpdateEvent))]
[JsonSerializable(typeof(IReadOnlyList<WorktreeProviderDto>))]
[JsonSerializable(typeof(WorktreeRequest))]
[JsonSerializable(typeof(WorktreePlanDto))]
[JsonSerializable(typeof(WorktreeDto))]
[JsonSerializable(typeof(WorktreePathRequest))]
[JsonSerializable(typeof(WorktreeCheckDto))]
[JsonSerializable(typeof(RemoveWorktreeRequest))]
[JsonSerializable(typeof(WindowInfo))]
[JsonSerializable(typeof(WindowTaskRequest))]
[JsonSerializable(typeof(OpenTaskRequest))]
[JsonSerializable(typeof(OpenTaskResult))]
[JsonSerializable(typeof(AttentionSnapshot))]
[JsonSerializable(typeof(AttentionSeenRequest))]
[JsonSerializable(typeof(AttentionItemRequest))]
[JsonSerializable(typeof(AttentionViewingRequest))]
internal sealed partial class AppJson : JsonSerializerContext;

/// <summary>App-level bridge messages: identity, window lifecycle and settings.</summary>
internal static class AppApi
{
    public static void Register(BridgeDispatcher bridge, OchreHost host)
    {
        var j = AppJson.Default;
        bridge.Handle("app.info", j.Empty, j.AppInfo, _ =>
            new AppInfo(AppInfoProvider.Version, host.Profile.IsDefault ? "" : host.Profile.Name, host.Modules.RunningIds));
        bridge.Handle("settings.get", j.Empty, j.SettingsDto, _ => Settings(host));
        bridge.Handle("settings.update", j.SettingsUpdateRequest, j.SettingsDto, async r =>
        {
            var known = host.Modules.States.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
            if (r.Modules.Any(m => !known.Contains(m.Id))) throw new BridgeRejectException("unknown module");
            string? hotkey = null;
            if (r.JumpHotkey is { } text)
            {
                text = text.Trim();
                if (text.Length > 64) throw new BridgeRejectException("that shortcut is too long");
                hotkey = text.Length == 0 ? "" : Hotkey.Parse(text, out var why)?.ToString() ?? throw new BridgeRejectException(why!);
            }
            host.Settings.SetModules(r.Modules.ToDictionary(m => m.Id, m => m.Enabled, StringComparer.Ordinal));
            if (hotkey is not null && hotkey != host.Settings.JumpHotkey)
            {
                host.Settings.SetJumpHotkey(hotkey);
                await host.ApplyJumpHotkeyAsync().ConfigureAwait(false);
            }
            return Settings(host);
        });
        bridge.Handle("app.restart", j.Empty, j.Ack, _ =>
        {
            host.RequestRestart();
            return new Ack();
        });
        // Links from untrusted text (terminal output) open here; OpenTargets keeps it to web and mail links.
        bridge.Handle("app.openUrl", j.OpenUrlRequest, j.Ack, r =>
        {
            if (r.Url.Length > 4096 || OpenTargets.WebUrl(r.Url) is not { } url) throw new BridgeRejectException("only web links can be opened");
            if (r.NewWindow) host.Urls.OpenInNewWindow(url);
            else host.Urls.Open(url);
            return new Ack();
        });
        bridge.Handle("app.ready", j.Empty, j.Ack, _ =>
        {
            host.Windows.PageReady();
            return new Ack();
        });
    }

    private static SettingsDto Settings(OchreHost host) =>
        new(host.Modules.States.Select(m => new ModuleDto(m.Id, m.Name, m.Description, m.Enabled, m.Running, m.Error)).ToList(),
            host.Settings.JumpHotkey,
            host.JumpHotkeyError);
}

internal static class AppInfoProvider
{
    public static string Version { get; } =
        typeof(AppInfoProvider).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
