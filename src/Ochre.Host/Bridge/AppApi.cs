using System.Reflection;
using System.Text.Json.Serialization;
using Ochre.Core.Bridge;

namespace Ochre.Host.Bridge;

public sealed record Empty;
public sealed record Ack(bool Ok = true);
public sealed record AppInfo(string Version, string Profile);

/// <param name="AutoParkOnShutdown">Park tasks with running agents, with an automatic note, when Windows shuts down or signs out.</param>
public sealed record SettingsDto(bool AutoParkOnShutdown);

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
[JsonSerializable(typeof(SessionUpdateEvent))]
internal sealed partial class AppJson : JsonSerializerContext;

/// <summary>App-level bridge messages: identity, window lifecycle and settings.</summary>
internal static class AppApi
{
    public static void Register(BridgeDispatcher bridge, OchreHost host)
    {
        var j = AppJson.Default;
        bridge.Handle("app.info", j.Empty, j.AppInfo, _ => new AppInfo(AppInfoProvider.Version, host.Profile.IsDefault ? "" : host.Profile.Name));
        bridge.Handle("settings.get", j.Empty, j.SettingsDto, _ => new SettingsDto(host.Settings.AutoParkOnShutdown));
        bridge.Handle("settings.update", j.SettingsDto, j.SettingsDto, r =>
        {
            host.Settings.AutoParkOnShutdown = r.AutoParkOnShutdown;
            return new SettingsDto(host.Settings.AutoParkOnShutdown);
        });
        bridge.Handle("app.ready", j.Empty, j.Ack, _ =>
        {
            host.Windows.PageReady();
            return new Ack();
        });
    }
}

internal static class AppInfoProvider
{
    public static string Version { get; } =
        typeof(AppInfoProvider).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
