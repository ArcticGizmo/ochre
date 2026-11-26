using System.Text.Json.Serialization;
using Ochre.Core.Bridge;

namespace Ochre.Modules.Roost.Bridge;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Ack))]
[JsonSerializable(typeof(RoostGetRequest))]
[JsonSerializable(typeof(RoostGridDto))]
[JsonSerializable(typeof(RoostPresetRequest))]
[JsonSerializable(typeof(RoostAssignRequest))]
[JsonSerializable(typeof(RoostSessionRequest))]
[JsonSerializable(typeof(RoostZoomRequest))]
[JsonSerializable(typeof(RoostChangedEvent))]
internal sealed partial class RoostJson : JsonSerializerContext;

// Bridge shapes for a task's grid (M5 slice 5, ADR 0013). The page never sends regions: it names a preset, a region
// the host listed, and sessions of the task, and gets the whole grid back.

/// <param name="Aspect">The stage's width / height, used only to shape a grid the task doesn't have yet.</param>
public sealed record RoostGetRequest(string TaskId, double Aspect);

/// <param name="Number">Its number in reading order (Alt+N).</param>
/// <param name="SessionId">The session in it, or null when it's empty.</param>
public sealed record RoostRegionDto(int Id, int Number, int Row, int Column, int RowSpan, int ColumnSpan, string? SessionId);

/// <param name="Key">The name <c>roost.applyPreset</c> takes.</param>
public sealed record RoostPresetDto(string Key, string Name, IReadOnlyList<RoostRegionDto> Regions);

/// <param name="Units">The grid's size each way; regions are placed in these units.</param>
/// <param name="Zoomed">The region filling the grid for now, or null.</param>
/// <param name="Unplaced">The task's sessions not in the grid, in the order they started.</param>
/// <param name="Preset">The preset the layout matches, or null.</param>
public sealed record RoostGridDto(
    string TaskId,
    int Units,
    IReadOnlyList<RoostRegionDto> Regions,
    int? Zoomed,
    IReadOnlyList<string> Unplaced,
    string? Preset,
    IReadOnlyList<RoostPresetDto> Presets);

public sealed record RoostPresetRequest(string TaskId, string Preset);

public sealed record RoostAssignRequest(string TaskId, int RegionId, string SessionId);

public sealed record RoostSessionRequest(string TaskId, string SessionId);

/// <param name="RegionId">The region to zoom, or null to restore.</param>
public sealed record RoostZoomRequest(string TaskId, int? RegionId);

public sealed record RoostChangedEvent(string TaskId);
