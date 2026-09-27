using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Borea.Core.Secrets;

namespace Borea.Storage.Secrets;

/// <summary>
/// ISecretStore through the Secret Service of the desktop, such as GNOME Keyring or KWallet, in its default collection.
/// It calls libsecret directly, so the secret never becomes a process argument.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LibSecretStore : ISecretStore
{
    private const string SecretLibrary = "libsecret-1.so.0";
    private const string GLibLibrary = "libglib-2.0.so.0";
    private const string SchemaName = "io.github.KSAModding.Borea";
    private const string NameAttribute = "name";

    // SecretSchema is a name, flags, 32 attributes of a name and a type, and reserved fields, 592 bytes on a 64-bit system.
    private const int SchemaSize = 592;
    private const int FirstAttributeOffset = 16;
    private const int CancelledCode = 19;

    private static readonly Lazy<Library?> Loaded = new(Library.Load);

    public string? Read(string name)
    {
        var library = Require();
        return Call(library, name, (attributes, error) =>
        {
            var found = secret_password_lookupv_sync(library.Schema, attributes, IntPtr.Zero, error);
            if (found == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUTF8(found);
            }
            finally
            {
                secret_password_free(found);
            }
        });
    }

    public void Write(string name, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var library = Require();
        Call<object?>(library, name, (attributes, error) =>
        {
            var plain = Encoding.UTF8.GetBytes(secret + "\0");
            var password = Marshal.AllocHGlobal(plain.Length);
            try
            {
                Marshal.Copy(plain, 0, password, plain.Length);
                var stored = secret_password_storev_sync(library.Schema, attributes, null, "Borea " + name, password, IntPtr.Zero, error);
                if (!stored && Marshal.ReadIntPtr(error) == IntPtr.Zero)
                    throw new SecretStoreException(SecretStoreProblem.Refused, "The Secret Service did not store the secret.");

                return null;
            }
            finally
            {
                Marshal.Copy(new byte[plain.Length], 0, password, plain.Length);
                Marshal.FreeHGlobal(password);
                Array.Clear(plain);
            }
        });
    }

    public void Delete(string name)
    {
        var library = Require();
        Call<object?>(library, name, (attributes, error) =>
        {
            secret_password_clearv_sync(library.Schema, attributes, IntPtr.Zero, error);
            return null;
        });
    }

    private static Library Require() =>
        Loaded.Value ?? throw new SecretStoreException(SecretStoreProblem.Missing, "libsecret cannot be loaded.");

    /// <summary>Runs <paramref name="work"/> with the attributes of <paramref name="name"/> and a place for a GError, and throws the error it leaves.</summary>
    private static T Call<T>(Library library, string name, Func<IntPtr, IntPtr, T> work)
    {
        SecretNames.Validate(name);
        var key = Marshal.StringToCoTaskMemUTF8(NameAttribute);
        var value = Marshal.StringToCoTaskMemUTF8(name);
        var error = Marshal.AllocHGlobal(IntPtr.Size);
        var attributes = IntPtr.Zero;
        try
        {
            Marshal.WriteIntPtr(error, IntPtr.Zero);
            attributes = g_hash_table_new(library.StringHash, library.StringEqual);
            g_hash_table_insert(attributes, key, value);
            var result = work(attributes, error);
            var failure = Marshal.ReadIntPtr(error);
            if (failure != IntPtr.Zero)
                throw Failure(failure);

            return result;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreException(SecretStoreProblem.Missing, "libsecret cannot be loaded.", exception);
        }
        finally
        {
            if (attributes != IntPtr.Zero)
                g_hash_table_unref(attributes);

            Marshal.FreeHGlobal(error);
            Marshal.FreeCoTaskMem(value);
            Marshal.FreeCoTaskMem(key);
        }
    }

    /// <summary>A D-Bus or I/O error means that no Secret Service answers, anything else that it refused.</summary>
    private static SecretStoreException Failure(IntPtr error)
    {
        try
        {
            var domain = Marshal.PtrToStringUTF8(g_quark_to_string(Marshal.ReadInt32(error)));
            var code = Marshal.ReadInt32(error, 4);
            var missing = domain == "g-dbus-error-quark" || (domain == "g-io-error-quark" && code != CancelledCode);
            return new SecretStoreException(missing ? SecretStoreProblem.Missing : SecretStoreProblem.Refused, $"The Secret Service answered {domain} {code}.");
        }
        finally
        {
            g_error_free(error);
        }
    }

    private sealed record Library(IntPtr Schema, IntPtr StringHash, IntPtr StringEqual)
    {
        public static Library? Load()
        {
            if (!NativeLibrary.TryLoad(SecretLibrary, out _) || !NativeLibrary.TryLoad(GLibLibrary, out var glib)
                || !NativeLibrary.TryGetExport(glib, "g_str_hash", out var hash) || !NativeLibrary.TryGetExport(glib, "g_str_equal", out var equal))
            {
                return null;
            }

            // Lives as long as the process, because every call passes it.
            var schema = Marshal.AllocHGlobal(SchemaSize);
            Marshal.Copy(new byte[SchemaSize], 0, schema, SchemaSize);
            Marshal.WriteIntPtr(schema, Marshal.StringToCoTaskMemUTF8(SchemaName));
            Marshal.WriteIntPtr(schema, FirstAttributeOffset, Marshal.StringToCoTaskMemUTF8(NameAttribute));
            return new Library(schema, hash, equal);
        }
    }

    [DllImport(SecretLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool secret_password_storev_sync(IntPtr schema, IntPtr attributes, [MarshalAs(UnmanagedType.LPUTF8Str)] string? collection, [MarshalAs(UnmanagedType.LPUTF8Str)] string label, IntPtr password, IntPtr cancellable, IntPtr error);

    [DllImport(SecretLibrary)]
    private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, IntPtr error);

    [DllImport(SecretLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, IntPtr error);

    [DllImport(SecretLibrary)]
    private static extern void secret_password_free(IntPtr password);

    [DllImport(GLibLibrary)]
    private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);

    [DllImport(GLibLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

    [DllImport(GLibLibrary)]
    private static extern void g_hash_table_unref(IntPtr table);

    [DllImport(GLibLibrary)]
    private static extern IntPtr g_quark_to_string(int quark);

    [DllImport(GLibLibrary)]
    private static extern void g_error_free(IntPtr error);
}
