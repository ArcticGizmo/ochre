namespace Ochre.Core.Platform;

/// <summary>Encrypts data at rest for the current OS user (DPAPI on Windows, Keychain later on macOS).</summary>
public interface IDataProtector
{
    byte[] Protect(byte[] data);

    /// <summary>The original bytes, or null when they can't be decrypted (another user or machine, or damaged).</summary>
    byte[]? Unprotect(byte[] data);
}
