using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Infrastructure.Security;

namespace SecureBudgetManager.Tests.Security;

public sealed class KeyProtectionTests
{
    private static readonly Argon2idParameters FastParameters = Argon2idParameters.Current with
    {
        MemoryKibibytes = 8192,
        Iterations = 2
    };

    [Fact]
    public void Argon2id_SameSecretAndSalt_ProducesSameKey()
    {
        var derivation = new Argon2idKeyDerivation();
        var salt = new byte[16];

        using var first = derivation.DeriveKeyEncryptionKey("a-strong-passphrase", salt, FastParameters);
        using var second = derivation.DeriveKeyEncryptionKey("a-strong-passphrase", salt, FastParameters);

        Assert.True(first.AsSpan().SequenceEqual(second.AsSpan()));
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public void Argon2id_DifferentSalt_ProducesDifferentKey()
    {
        var derivation = new Argon2idKeyDerivation();
        var saltA = new byte[16];
        var saltB = new byte[16];
        saltB[0] = 1;

        using var first = derivation.DeriveKeyEncryptionKey("a-strong-passphrase", saltA, FastParameters);
        using var second = derivation.DeriveKeyEncryptionKey("a-strong-passphrase", saltB, FastParameters);

        Assert.False(first.AsSpan().SequenceEqual(second.AsSpan()));
    }

    [Fact]
    public void Argon2id_RejectsWeakenedCostParameters()
    {
        var derivation = new Argon2idKeyDerivation();
        var weakened = Argon2idParameters.Current with { MemoryKibibytes = 64 };

        Assert.Throws<InvalidOperationException>(
            () => derivation.DeriveKeyEncryptionKey("a-strong-passphrase", new byte[16], weakened));
    }

    [Fact]
    public void AesGcm_WrapThenUnwrap_RecoversTheDatabaseKey()
    {
        var wrapper = new AesGcmKeyWrapper();
        using var kek = SecureKey.CreateRandom(32);
        using var databaseKey = SecureKey.CreateRandom(32);
        var salt = new byte[16];

        var envelope = wrapper.Wrap(kek, databaseKey, salt);
        using var unwrapped = wrapper.TryUnwrap(kek, envelope);

        Assert.NotNull(unwrapped);
        Assert.True(databaseKey.AsSpan().SequenceEqual(unwrapped!.AsSpan()));
    }

    [Fact]
    public void AesGcm_WrongKeyEncryptionKey_ReturnsNullRatherThanGarbage()
    {
        var wrapper = new AesGcmKeyWrapper();
        using var correctKek = SecureKey.CreateRandom(32);
        using var wrongKek = SecureKey.CreateRandom(32);
        using var databaseKey = SecureKey.CreateRandom(32);

        var envelope = wrapper.Wrap(correctKek, databaseKey, new byte[16]);

        Assert.Null(wrapper.TryUnwrap(wrongKek, envelope));
    }

    [Fact]
    public void AesGcm_TamperedEnvelope_FailsAuthentication()
    {
        var wrapper = new AesGcmKeyWrapper();
        using var kek = SecureKey.CreateRandom(32);
        using var databaseKey = SecureKey.CreateRandom(32);

        var envelope = wrapper.Wrap(kek, databaseKey, new byte[16]);
        envelope.Ciphertext[0] ^= 0xFF;

        Assert.Null(wrapper.TryUnwrap(kek, envelope));
    }

    [Fact]
    public void RecoveryKeyDerivation_IsDeterministicAndSaltDependent()
    {
        var derivation = new Argon2idKeyDerivation();
        var recovery = RecoveryKey.Generate();
        var saltA = new byte[16];
        var saltB = new byte[16];
        saltB[15] = 9;

        using var a1 = derivation.DeriveFromRecoveryKey(recovery, saltA);
        using var a2 = derivation.DeriveFromRecoveryKey(recovery, saltA);
        using var b1 = derivation.DeriveFromRecoveryKey(recovery, saltB);

        Assert.True(a1.AsSpan().SequenceEqual(a2.AsSpan()));
        Assert.False(a1.AsSpan().SequenceEqual(b1.AsSpan()));
    }

    [Fact]
    public void SecureKey_AfterDisposal_CannotBeRead()
    {
        var key = SecureKey.CreateRandom(32);
        key.Dispose();

        Assert.Throws<ObjectDisposedException>(() => key.AsSpan());
    }

    [Fact]
    public void SecureKey_HexEncoding_MatchesKeyMaterial()
    {
        using var key = SecureKey.CreateFrom([0x00, 0x0f, 0xa0, 0xff]);

        var hex = key.ToHexCharArray();

        Assert.Equal("000fa0ff", new string(hex));
    }
}
