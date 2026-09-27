using System.Text;
using Borea.Core.Secrets;
using Borea.Storage.Secrets;
using Borea.Storage.Tests.Launch;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Secrets;

public sealed class SecretStoreTests : IDisposable
{
    private const string Name = "github-refresh-token";
    private const string Secret = "ghr_test_secret";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;

    public SecretStoreTests()
    {
        _paths = new TestGamePathProvider(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [WindowsFact("DPAPI is part of Windows.")]
    public void ForCurrentUser_OnWindows_KeepsTheSecretEncryptedInTheSecretsFolder()
    {
        var store = SecretStores.ForCurrentUser(_paths);

        Assert.Null(store.Read(Name));
        store.Write(Name, Secret);

        Assert.Equal(Secret, store.Read(Name));
        var file = Assert.Single(Directory.GetFiles(_paths.GetSecretsFolder()));
        var bytes = File.ReadAllBytes(file);
        Assert.DoesNotContain(Secret, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, Encoding.Unicode.GetString(bytes), StringComparison.Ordinal);
    }

    [WindowsFact("DPAPI is part of Windows.")]
    public void Write_OnWindows_ReplacesTheSecretAndDeleteRemovesIt()
    {
        var store = SecretStores.ForCurrentUser(_paths);
        store.Write(Name, Secret);

        store.Write(Name, "ghr_newer");

        Assert.Equal("ghr_newer", store.Read(Name));
        store.Delete(Name);
        Assert.Null(store.Read(Name));
        Assert.Empty(Directory.GetFiles(_paths.GetSecretsFolder()));
        store.Delete(Name);
    }

    [WindowsFact("DPAPI is part of Windows.")]
    public void Read_OnWindows_AFileThisAccountCannotDecrypt_ReadsAsNothingKept()
    {
        var store = SecretStores.ForCurrentUser(_paths);
        store.Write(Name, Secret);
        var file = Assert.Single(Directory.GetFiles(_paths.GetSecretsFolder()));
        File.WriteAllBytes(file, [1, 2, 3, 4]);

        Assert.Null(store.Read(Name));
    }

    [Theory]
    [InlineData(SecretStoreProblem.Unsupported)]
    [InlineData(SecretStoreProblem.Missing)]
    public void UnavailableStore_RefusesEveryCallWithItsProblem(SecretStoreProblem problem)
    {
        var store = new UnavailableSecretStore(problem);

        var refusals = new[]
        {
            Assert.Throws<SecretStoreException>(() => store.Read(Name)),
            Assert.Throws<SecretStoreException>(() => store.Write(Name, Secret)),
            Assert.Throws<SecretStoreException>(() => store.Delete(Name)),
        };

        Assert.All(refusals, refusal => Assert.Equal(problem, refusal.Problem));
        Assert.All(refusals, refusal => Assert.DoesNotContain(Secret, refusal.Message, StringComparison.Ordinal));
        Assert.False(Directory.Exists(_paths.GetSecretsFolder()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../app-preferences")]
    [InlineData("GitHub")]
    [InlineData("github refresh")]
    public void SecretNames_OtherThanLowercaseDigitsAndHyphens_AreRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => SecretNames.Validate(name));
    }
}
