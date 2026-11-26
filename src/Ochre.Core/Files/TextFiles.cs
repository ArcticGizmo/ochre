using System.Security.Cryptography;
using System.Text;

namespace Ochre.Core.Files;

internal enum TextFileProblem
{
    None,
    Missing,
    TooLarge,
    Binary,
    NotUtf8,
}

/// <param name="Text">Null when it can't be shown (missing, too large, binary).</param>
/// <param name="Version">SHA-256 of the bytes read, in hex; empty when there was no file. A save names it.</param>
/// <param name="ReadOnly">Shown but not saveable: not UTF-8 (shown with replacement characters), or too big to send back.</param>
internal sealed record TextFileRead(string? Text, string Version, long Size, bool Bom, TextFileProblem Problem, bool ReadOnly);

/// <param name="Saved">False when the file changed since <c>expectedVersion</c> was read; nothing was written.</param>
/// <param name="Version">The version on disk now: the new one after a save, the other writer's after a conflict, empty if gone.</param>
internal sealed record TextFileWrite(bool Saved, string Version);

/// <summary>
/// Reading and saving the editor's files (ADR 0015). A save never overwrites a change it didn't see: it names the
/// version it edited and is refused if the file moved on, so an agent's edit and the user's never silently replace
/// each other. Reads share the file with whoever else has it open, as Claude's own files are read.
/// </summary>
internal static class TextFiles
{
    /// <summary>Largest file shown.</summary>
    public const long MaxReadBytes = 5 * 1024 * 1024;

    /// <summary>Largest file that can be saved: the bridge takes at most a million characters a message, and JSON escaping grows text.</summary>
    public const long MaxEditBytes = 512 * 1024;

    private const int BinarySniffBytes = 8000;
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Lenient = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    // One save at a time across the app, so two windows saving one file can't both pass the version check.
    private static readonly Lock WriteGate = new();

    public static TextFileRead Read(string path)
    {
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxReadBytes) return new TextFileRead(null, "", stream.Length, false, TextFileProblem.TooLarge, true);
            bytes = ReadAll(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new TextFileRead(null, "", 0, false, TextFileProblem.Missing, true);
        }
        return Decode(bytes);
    }

    /// <summary>Decodes bytes as the editor shows them; also used for text read from git.</summary>
    public static TextFileRead Decode(byte[] bytes)
    {
        var version = Version(bytes);
        if (bytes.Length > MaxReadBytes) return new TextFileRead(null, version, bytes.Length, false, TextFileProblem.TooLarge, true);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, BinarySniffBytes)).Contains((byte)0))
            return new TextFileRead(null, version, bytes.Length, false, TextFileProblem.Binary, true);

        var bom = bytes.AsSpan().StartsWith(Utf8Bom);
        var body = bom ? bytes.AsSpan(Utf8Bom.Length) : bytes.AsSpan();
        try
        {
            var text = Strict.GetString(body);
            return new TextFileRead(text, version, bytes.Length, bom, TextFileProblem.None, bytes.Length > MaxEditBytes);
        }
        catch (DecoderFallbackException)
        {
            return new TextFileRead(Lenient.GetString(body), version, bytes.Length, bom, TextFileProblem.NotUtf8, true);
        }
    }

    /// <summary>
    /// Saves <paramref name="text"/> over the file if it still has <paramref name="expectedVersion"/>, keeping its BOM.
    /// The text is written as sent, line endings included. Written to a temp file beside it, then moved over it, so a
    /// crash leaves the old file or the new one, never half of each. Never creates a file.
    /// </summary>
    public static TextFileWrite Write(string path, string text, string expectedVersion)
    {
        lock (WriteGate)
        {
            byte[] current;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > MaxEditBytes) throw new InvalidOperationException("this file is too large to save from the editor");
                current = ReadAll(stream);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return new TextFileWrite(false, "");
            }

            var onDisk = Version(current);
            if (!string.Equals(onDisk, expectedVersion, StringComparison.Ordinal)) return new TextFileWrite(false, onDisk);
            if (Decode(current) is not { Problem: TextFileProblem.None, ReadOnly: false }) throw new InvalidOperationException("this file can't be saved from the editor");

            var bom = current.AsSpan().StartsWith(Utf8Bom);
            byte[] body;
            try
            {
                body = Strict.GetBytes(text);
            }
            catch (EncoderFallbackException)
            {
                throw new InvalidOperationException("the text can't be saved as UTF-8");
            }
            var bytes = bom ? [.. Utf8Bom, .. body] : body;

            var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.ochre-save");
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            return new TextFileWrite(true, Version(bytes));
        }
    }

    public static string Version(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] ReadAll(FileStream stream)
    {
        // Length can change under a writer; read to the end rather than trusting it.
        using var buffer = new MemoryStream((int)Math.Min(stream.Length, MaxReadBytes));
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
