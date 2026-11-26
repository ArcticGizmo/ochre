using System.Globalization;
using System.Text.Json.Serialization;
using Ochre.Core.Attention;
using Ochre.Core.Bridge;
using Ochre.Core.Sessions;

namespace Ochre.Host.Bridge;

[JsonConverter(typeof(JsonStringEnumConverter<AttentionKindDto>))]
public enum AttentionKindDto
{
    [JsonStringEnumMemberName("permission")]
    Permission,

    [JsonStringEnumMemberName("question")]
    Question,

    [JsonStringEnumMemberName("plan")]
    Plan,

    /// <summary>Waiting on a prompt only another client (Remote Control) shows; open the thread.</summary>
    [JsonStringEnumMemberName("elsewhere")]
    Elsewhere,

    /// <summary>A session outside Ochre is waiting; read-only here.</summary>
    [JsonStringEnumMemberName("waiting")]
    Waiting,

    [JsonStringEnumMemberName("finished")]
    Finished,

    [JsonStringEnumMemberName("failed")]
    Failed,
}

/// <param name="Id">Valid only while the item is listed; dismiss and later actions name it.</param>
/// <param name="TaskId">Empty for a session outside Ochre nobody has linked to a task.</param>
/// <param name="NeedsYou">Blocked on the user (counted on the badge), as opposed to finished or failed.</param>
/// <param name="Title">What it wants, after the agent's name: "wants to use Bash", "finished".</param>
/// <param name="Detail">One short line: the command, the question, the reply's last line.</param>
/// <param name="RequestId">For a permission, question or plan: the prompt to answer with <c>sessions.respond</c>.</param>
public sealed record AttentionItemDto(
    string Id,
    string SessionId,
    string TaskId,
    AttentionKindDto Kind,
    bool NeedsYou,
    string Title,
    string Detail,
    string Since,
    string? RequestId,
    string? Role,
    string Cwd);

/// <param name="Items">Grouped by task, in the order the inbox shows them.</param>
/// <param name="NeedsYou">How many items are blocked on the user, for the badge.</param>
public sealed record AttentionSnapshot(IReadOnlyList<AttentionItemDto> Items, int NeedsYou);

public sealed record AttentionSeenRequest(string SessionId);

public sealed record AttentionItemRequest(string Id);

/// <summary>The attention inbox's messages (ADR 0013). The page says what the user has in front of them; the board
/// decides what that clears.</summary>
internal static class AttentionApi
{
    public static void Register(BridgeDispatcher bridge, AttentionBoard board, SessionManager sessions)
    {
        var j = AppJson.Default;

        bridge.Handle("attention.list", j.Empty, j.AttentionSnapshot, _ => Snapshot(board));

        bridge.Handle("attention.seen", j.AttentionSeenRequest, j.Ack, r =>
        {
            if (sessions.Get(r.SessionId) is null) throw new BridgeRejectException("no such session");
            board.Seen(r.SessionId);
            return new Ack();
        });

        bridge.Handle("attention.dismiss", j.AttentionItemRequest, j.Ack, r =>
        {
            if (!board.Dismiss(r.Id)) throw new BridgeRejectException("that item can't be dismissed, or is gone");
            return new Ack();
        });
    }

    public static AttentionSnapshot Snapshot(AttentionBoard board)
    {
        var items = board.Items.Select(Dto).ToList();
        return new AttentionSnapshot(items, items.Count(i => i.NeedsYou));
    }

    private static AttentionItemDto Dto(AttentionItem i) => new(
        i.Id,
        i.SessionId,
        i.TaskId,
        i.Kind switch
        {
            AttentionKind.Permission => AttentionKindDto.Permission,
            AttentionKind.Question => AttentionKindDto.Question,
            AttentionKind.Plan => AttentionKindDto.Plan,
            AttentionKind.Elsewhere => AttentionKindDto.Elsewhere,
            AttentionKind.Waiting => AttentionKindDto.Waiting,
            AttentionKind.Finished => AttentionKindDto.Finished,
            _ => AttentionKindDto.Failed,
        },
        i.NeedsYou,
        i.Title,
        i.Detail,
        i.Since.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        i.RequestId,
        i.Role,
        i.Cwd);
}
