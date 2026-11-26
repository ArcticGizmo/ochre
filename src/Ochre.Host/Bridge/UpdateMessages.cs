using System.Text.Json.Serialization;

namespace Ochre.Host.Bridge;

[JsonConverter(typeof(JsonStringEnumConverter<UpdateStateDto>))]
public enum UpdateStateDto
{
    /// <summary>Not an installed copy (a dev build, or the portable zip), so it can't update itself.</summary>
    [JsonStringEnumMemberName("unsupported")]
    Unsupported,
    [JsonStringEnumMemberName("idle")]
    Idle,
    [JsonStringEnumMemberName("checking")]
    Checking,
    [JsonStringEnumMemberName("upToDate")]
    UpToDate,
    [JsonStringEnumMemberName("available")]
    Available,
    [JsonStringEnumMemberName("downloading")]
    Downloading,
    /// <summary>Downloaded; Ochre is quitting so the installer can swap it and start the new version.</summary>
    [JsonStringEnumMemberName("restarting")]
    Restarting,
    [JsonStringEnumMemberName("error")]
    Error,
}

/// <param name="Current">The running version.</param>
/// <param name="Available">The newer version found, while there is one.</param>
/// <param name="Percent">Download progress while downloading.</param>
/// <param name="Error">Why the last check or update failed.</param>
/// <param name="CheckedAt">When the feed was last asked (ISO 8601), or null if it hasn't been this run.</param>
public sealed record UpdateDto(UpdateStateDto State, string Current, string? Available, int? Percent, string? Error, string? CheckedAt);
