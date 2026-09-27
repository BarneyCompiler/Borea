using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Borea.Core.Secrets;
using Borea.Storage.Files;

namespace Borea.Storage.Secrets;

/// <summary>
/// ISecretStore through DPAPI for the current user. Each secret is a file in the secrets folder that only this Windows account can decrypt.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DpapiSecretStore : ISecretStore
{
    private const int UiForbidden = 0x1;

    // Another program that decrypts the files of this user with DPAPI also has to know these bytes.
    private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("Borea secret store v1");

    private readonly string _folder;

    public DpapiSecretStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = folder;
    }

    public string? Read(string name)
    {
        byte[] protectedBytes;
        try
        {
            protectedBytes = File.ReadAllBytes(PathOf(name));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException(SecretStoreProblem.Refused, "Borea cannot read its file in the secrets folder.", exception);
        }

        // A file that this account cannot decrypt, such as one copied from another computer, holds nothing Borea can use.
        if (Transform(protectedBytes, protect: false) is not { } plain)
            return null;

        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    public void Write(string name, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var path = PathOf(name);
        var plain = Encoding.UTF8.GetBytes(secret);
        byte[] protectedBytes;
        try
        {
            protectedBytes = Transform(plain, protect: true)
                ?? throw new SecretStoreException(SecretStoreProblem.Refused, "DPAPI refused to protect the secret.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally
        {
            Array.Clear(plain);
        }

        try
        {
            AtomicFile.WriteAllBytes(path, protectedBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException(SecretStoreProblem.Refused, "Borea cannot write its file in the secrets folder.", exception);
        }
    }

    public void Delete(string name)
    {
        try
        {
            File.Delete(PathOf(name));
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException(SecretStoreProblem.Refused, "Borea cannot delete its file in the secrets folder.", exception);
        }
    }

    private string PathOf(string name) => Path.Combine(_folder, SecretNames.Validate(name) + ".dpapi");

    /// <summary>The protected or the plain bytes, or null when DPAPI refused.</summary>
    private static byte[]? Transform(byte[] input, bool protect)
    {
        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var inputBlob = new DataBlob(input.Length, inputHandle.AddrOfPinnedObject());
            var entropyBlob = new DataBlob(Entropy.Length, entropyHandle.AddrOfPinnedObject());
            var done = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!done)
                return null;

            try
            {
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                LocalFree(output.Data);
            }
        }
        finally
        {
            inputHandle.Free();
            entropyHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob(int length, IntPtr data)
    {
        public int Length = length;
        public IntPtr Data = data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
