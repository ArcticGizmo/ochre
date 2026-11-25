using System.Reflection;
using System.Text.Json.Serialization;
using Ochre.Core.Bridge;

namespace Ochre.Host.Bridge;

public sealed record Empty;
public sealed record Ack(bool Ok = true);
public sealed record AppInfo(string Version, string Profile);

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
[JsonSerializable(typeof(IReadOnlyList<SessionSummary>))]
[JsonSerializable(typeof(SessionSnapshot))]
[JsonSerializable(typeof(SessionIdRequest))]
[JsonSerializable(typeof(StartSessionRequest))]
[JsonSerializable(typeof(StartSessionResult))]
[JsonSerializable(typeof(SendPromptRequest))]
[JsonSerializable(typeof(RespondRequest))]
[JsonSerializable(typeof(SessionUpdateEvent))]
internal sealed partial class AppJson : JsonSerializerContext;

/// <summary>App-level bridge messages: identity and window lifecycle.</summary>
internal static class AppApi
{
    public static void Register(BridgeDispatcher bridge, OchreHost host)
    {
        var j = AppJson.Default;
        bridge.Handle("app.info", j.Empty, j.AppInfo, _ => new AppInfo(AppInfoProvider.Version, host.Profile.IsDefault ? "" : host.Profile.Name));
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
