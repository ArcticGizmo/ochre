using System.Text;

namespace Ochre.Modules.Sprig;

/// <summary>The end of a log another process is writing. Shares everything, so the writer is never blocked.</summary>
internal static class LogTail
{
    /// <summary>The last <paramref name="maxLines"/> whole lines within the last <paramref name="maxBytes"/>; a
    /// partial first line is dropped. Null when the file can't be read.</summary>
    public static (string Text, bool Truncated)? Read(string path, int maxLines = 300, int maxBytes = 256 * 1024)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - maxBytes);
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[stream.Length - start];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
            var truncated = start > 0;
            // Starting mid-file cuts the first line short.
            if (truncated && lines.Count > 0) lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > maxLines)
            {
                lines = lines[^maxLines..];
                truncated = true;
            }
            return (string.Join('\n', lines), truncated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
