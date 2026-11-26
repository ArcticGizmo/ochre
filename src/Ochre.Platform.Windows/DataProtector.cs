using System.ComponentModel;
using System.Runtime.InteropServices;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows;

/// <summary>DPAPI for the current user, called directly rather than through a package.</summary>
public sealed partial class DataProtector : IDataProtector
{
    // Fail instead of showing a prompt; the host may be running with no window.
    private const int UiForbidden = 0x1;

    // Ties the data to Ochre's purpose, so another app's DPAPI blobs and ours can't be swapped for each other.
    private static readonly byte[] Entropy = "Ochre terminal scrollback v1"u8.ToArray();

    public byte[] Protect(byte[] data)
    {
        if (!Crypt(data, protect: true, out var result)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CryptProtectData failed");
        return result;
    }

    public byte[]? Unprotect(byte[] data) => Crypt(data, protect: false, out var result) ? result : null;

    private static unsafe bool Crypt(byte[] data, bool protect, out byte[] result)
    {
        result = [];
        fixed (byte* input = data)
        fixed (byte* entropy = Entropy)
        {
            var inBlob = new Blob { Size = data.Length, Data = (nint)input };
            var entropyBlob = new Blob { Size = Entropy.Length, Data = (nint)entropy };
            var outBlob = default(Blob);
            var ok = protect
                ? CryptProtectData(ref inBlob, null, ref entropyBlob, 0, 0, UiForbidden, ref outBlob)
                : CryptUnprotectData(ref inBlob, 0, ref entropyBlob, 0, 0, UiForbidden, ref outBlob);
            if (!ok) return false;
            try
            {
                result = new byte[outBlob.Size];
                Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
                return true;
            }
            finally
            {
                LocalFree(outBlob.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public nint Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref Blob dataIn, string? description, ref Blob entropy, nint reserved, nint prompt, int flags, ref Blob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref Blob dataIn, nint description, ref Blob entropy, nint reserved, nint prompt, int flags, ref Blob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
