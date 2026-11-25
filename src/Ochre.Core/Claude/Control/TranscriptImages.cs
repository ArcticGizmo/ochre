using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ochre.Core.Claude.Control;

/// <summary>
/// Recovers the images from a resumed user message so the thread can show them instead of the bare
/// <c>[Image #N]</c> placeholder the CLI leaves in the text. Claude Code records a pasted image as a
/// <c>[Image #N]</c> token in a text block <em>and</em> an <c>image</c> content block, and caches the file at
/// <c>~/.claude/image-cache/{sessionId}/{N}.{ext}</c>: the k-th image block pairs with the k-th token, and the
/// chip points at that cached file. An image whose cache file is gone is simply not shown (Perch decodes the
/// block's base64 to a temp file instead; that isn't carried over).
/// </summary>
internal static partial class TranscriptImages
{
    private static readonly string[] CacheExtensions = ["png", "jpeg", "jpg", "gif", "webp"];

    [GeneratedRegex(@"\[Image #(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ImageTokenRegex();

    /// <summary>Removes the <c>[Image #N]</c> placeholder tokens (and any trailing space) from a display
    /// string once the images are shown as chips instead.</summary>
    public static string StripTokens(string text) =>
        ImageTokenRegex().Replace(text, "").Replace("  ", " ").Trim();

    /// <summary>The image attachments for a user message's <c>content</c> array. <paramref name="text"/> is the
    /// message's joined text (for the <c>[Image #N]</c> numbers), <paramref name="sessionId"/> the id whose
    /// image-cache directory to probe. Empty when there are no image blocks. Never throws.</summary>
    public static IReadOnlyList<MessageAttachment> Extract(JsonArray? content, string? text, string? sessionId)
    {
        if (content is null || string.IsNullOrEmpty(sessionId)) return [];

        var numbers = new List<int>();
        if (!string.IsNullOrEmpty(text))
            foreach (Match m in ImageTokenRegex().Matches(text))
                if (int.TryParse(m.Groups[1].Value, out var n)) numbers.Add(n);

        var result = new List<MessageAttachment>();
        int k = 0;
        foreach (var block in content)
        {
            if (TranscriptJson.BlockType(block) != "image") continue;
            int number = k < numbers.Count ? numbers[k] : k + 1;   // token order matches block order
            k++;
            if (Resolve(block, number, sessionId) is { } att) result.Add(att);
        }
        return result;
    }

    private static MessageAttachment? Resolve(JsonNode? block, int number, string sessionId)
    {
        try
        {
            var mediaType = TranscriptJson.AsString(block?["source"]?["media_type"]);
            var dir = Path.Combine(ClaudePaths.ImageCacheDir, sessionId);
            foreach (var ext in ExtensionOrder(mediaType))
            {
                var candidate = Path.Combine(dir, $"{number}.{ext}");
                if (File.Exists(candidate))
                    return new MessageAttachment { Kind = AttachmentKind.Image, Path = candidate, MediaType = mediaType ?? MediaOf(ext) };
            }
        }
        catch { /* best-effort — a chip that can't resolve just doesn't appear */ }
        return null;
    }

    // The extension(s) to try for a media type, most-likely first. When unknown, try every cache extension.
    private static IEnumerable<string> ExtensionOrder(string? mediaType) => mediaType switch
    {
        "image/png" => ["png"],
        "image/jpeg" => ["jpeg", "jpg"],
        "image/gif" => ["gif"],
        "image/webp" => ["webp"],
        _ => CacheExtensions,
    };

    private static string MediaOf(string ext) => ext switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        _ => "image/png",
    };
}
