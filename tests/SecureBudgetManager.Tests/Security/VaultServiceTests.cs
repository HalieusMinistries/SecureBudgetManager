using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Tests.TestSupport;

namespace SecureBudgetManager.Tests.Security;

public sealed class VaultServiceTests
{
    [Fact]
    public async Task FreshStoreOpensWithoutAPassword()
    {
        using var fixture = new VaultFixture();

        Assert.Equal(VaultStatus.Locked, fixture.Vault.Status);

        var result = await fixture.Vault.RevealAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(VaultStatus.Unlocked, fixture.Vault.Status);
        Assert.True(fixture.Store.IsOpen);
        Assert.True(File.Exists(fixture.Directory.GetDatabasePath()));
        Assert.False(fixture.Vault.RequiresMasterSecretUnlock);
        Assert.False(fixture.Vault.UsesWindowsUserProtection);
        Assert.False(File.Exists(fixture.Directory.GetVaultMetadataPath()));
    }

    [Fact]
    public async Task ConcealClosesTheDatabase()
    {
        using var fixture = new VaultFixture();
        await fixture.Vault.RevealAsync();

        fixture.Vault.Conceal();

        Assert.Equal(VaultStatus.Locked, fixture.Vault.Status);
        Assert.False(fixture.Store.IsOpen);
    }

    [Fact]
    public async Task RevealOpensAgainAfterConceal()
    {
        using var fixture = new VaultFixture();
        await fixture.Vault.RevealAsync();
        fixture.Store.RecordAuditEvent("household.opened", "smoke test");
        fixture.Vault.Conceal();

        var result = await fixture.Vault.RevealAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(VaultStatus.Unlocked, fixture.Vault.Status);
        Assert.Equal(1, fixture.Store.CountAuditEvents());
    }

    [Fact]
    public async Task ChangeSecretIsRefused()
    {
        using var fixture = new VaultFixture();
        await fixture.Vault.RevealAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Vault.ChangeSecretAsync(
                VaultFixture.Secret("unused-current"),
                VaultFixture.Secret("unused-next"),
                MasterSecretKind.Password));
    }

    [Fact]
    public async Task SecretArraysAreClearedAfterUse()
    {
        using var fixture = new VaultFixture();
        var secret = VaultFixture.Secret("unused-local-store");

        await fixture.Vault.InitialiseAsync(secret, MasterSecretKind.Password);

        Assert.All(secret, character => Assert.Equal('\0', character));
        Assert.Equal(VaultStatus.Unlocked, fixture.Vault.Status);
    }

    [Fact]
    public async Task DatabaseFileIsOrdinarySqlite()
    {
        using var fixture = new VaultFixture();
        await fixture.Vault.RevealAsync();
        fixture.Store.RecordAuditEvent("household.created", "smoke test");
        fixture.Vault.Conceal();

        var bytes = await File.ReadAllBytesAsync(fixture.Directory.GetDatabasePath());
        var header = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
        Assert.True(bytes.AsSpan(0, header.Length).SequenceEqual(header));
        Assert.Contains("audit_log", System.Text.Encoding.ASCII.GetString(bytes), StringComparison.Ordinal);
    }
}
