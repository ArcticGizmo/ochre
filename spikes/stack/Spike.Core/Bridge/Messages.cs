using System.Text.Json.Serialization;

namespace Spike.Core.Bridge;

public sealed record Empty;
public sealed record Ack(bool Ok = true);

public sealed record EchoMessage(int N, string S);

public sealed record TaskSummary(string Id, string Title, string State, string Body);

public sealed record ReadFileRequest(string Path);
public sealed record ReadFileResult(string Path, string Content, string Language);

public sealed record PtyStartRequest(int Cols, int Rows);
public sealed record PtyStartResult(bool Attached, int Pid);
public sealed record PtyInput(string Data);
public sealed record PtyResize(int Cols, int Rows);
public sealed record PtyMode(int CoalesceMs);
public sealed record PtyOutput(string Data);
public sealed record PtyExit(int Code);

public sealed record BigFileResult(string Path, long Bytes);
public sealed record BenchReport(string Line);

public sealed record Metrics(string Shell, double ColdStartMs, double? LastWindowOpenMs, double HostWorkingSetMb);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Empty))]
[JsonSerializable(typeof(Ack))]
[JsonSerializable(typeof(EchoMessage))]
[JsonSerializable(typeof(TaskSummary[]))]
[JsonSerializable(typeof(ReadFileRequest))]
[JsonSerializable(typeof(ReadFileResult))]
[JsonSerializable(typeof(PtyStartRequest))]
[JsonSerializable(typeof(PtyStartResult))]
[JsonSerializable(typeof(PtyInput))]
[JsonSerializable(typeof(PtyResize))]
[JsonSerializable(typeof(PtyMode))]
[JsonSerializable(typeof(PtyOutput))]
[JsonSerializable(typeof(PtyExit))]
[JsonSerializable(typeof(BigFileResult))]
[JsonSerializable(typeof(BenchReport))]
[JsonSerializable(typeof(Metrics))]
public sealed partial class BridgeJson : JsonSerializerContext;
