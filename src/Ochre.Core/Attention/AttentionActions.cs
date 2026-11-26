using Ochre.Core.Claude.Control;
using Ochre.Core.Sessions;

namespace Ochre.Core.Attention;

/// <summary>
/// Acting on an attention item from outside a window (a desktop notification's buttons). Whoever clicked, and
/// whatever the OS handed back, is untrusted: an action names only an item and what to do, and is carried out only
/// if that item is still listed and the action fits its kind, so a stale or forged click does nothing.
/// </summary>
internal sealed class AttentionActions(AttentionBoard board, SessionManager sessions)
{
    public const string Allow = "allow";
    public const string Deny = "deny";
    public const string Answer = "answer";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Send = "send";
    public const string Dismiss = "dismiss";

    /// <summary>The input a question's choice arrives in, as the option's index.</summary>
    public const string ChoiceInput = "choice";

    /// <summary>The input a plan's feedback or a reply arrives in.</summary>
    public const string TextInput = "text";

    /// <summary>A question this short is answered from a notification; a longer one is answered in the thread.</summary>
    public const int MaxChoices = 5;

    private const int MaxText = 4_000;

    /// <summary>Does it. Returns null when done, else why not.</summary>
    public string? Act(string itemId, string action, IReadOnlyDictionary<string, string> inputs)
    {
        if (board.Get(itemId) is not { } item) return "that is no longer waiting";
        if (sessions.Get(item.SessionId) is not { } session) return "that agent has gone";

        switch (item.Kind, action)
        {
            case (AttentionKind.Permission, Allow):
                return Respond(session, item, true, null, null);
            case (AttentionKind.Permission, Deny):
                return Respond(session, item, false, null, null);
            case (AttentionKind.Plan, Approve):
                return Respond(session, item, true, null, null);
            case (AttentionKind.Plan, Reject):
                var feedback = Text(inputs);
                if (feedback is { Length: > MaxText }) return "that message is too long";
                return Respond(session, item, false, null, feedback);
            case (AttentionKind.Question, Answer):
                if (ChoiceOf(session, item, inputs) is not { } answer) return "choose one of the options";
                return Respond(session, item, true, new Dictionary<string, IReadOnlyList<string>> { [answer.Question] = [answer.Label] }, null);
            case (AttentionKind.Finished, Send):
                var text = Text(inputs);
                if (text is null) return "the reply is empty";
                if (text.Length > MaxText) return "that reply is too long";
                try
                {
                    sessions.Send(session, text);
                    return null;
                }
                catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
                {
                    return ex.Message;
                }
            case (_, Dismiss):
                return board.Dismiss(itemId) ? null : "that can't be dismissed; answer it instead";
            default:
                return $"'{action}' doesn't apply to this";
        }
    }

    /// <summary>The one question a notification can answer: a single, single-choice question with few options.</summary>
    public static UserQuestion? Answerable(string inputJson) =>
        AskUserQuestionInput.Parse(inputJson) is [{ MultiSelect: false } q] && q.Options.Count is > 0 and <= MaxChoices ? q : null;

    private string? Respond(LiveSession session, AttentionItem item, bool allow,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? answers, string? denyMessage) =>
        item.RequestId is { } requestId && sessions.Respond(session, requestId, allow, null, answers, denyMessage)
            ? null
            : "that prompt is no longer waiting for an answer";

    private static (string Question, string Label)? ChoiceOf(LiveSession session, AttentionItem item, IReadOnlyDictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue(ChoiceInput, out var raw) || !int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)) return null;
        string? inputJson;
        lock (session.Gate)
            inputJson = session.Conversation.PendingPermission is { } p && p.Request.RequestId == item.RequestId ? p.Request.InputJson : null;
        if (inputJson is null || Answerable(inputJson) is not { } q || index >= q.Options.Count) return null;
        return (q.Question, q.Options[index].Label);
    }

    private static string? Text(IReadOnlyDictionary<string, string> inputs) =>
        inputs.TryGetValue(TextInput, out var t) && !string.IsNullOrWhiteSpace(t) ? t.Trim() : null;
}
