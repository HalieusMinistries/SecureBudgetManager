using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Core.Abstractions;

public interface IVaultMetadataRepository
{
    bool Exists { get; }

    VaultMetadata Read();

    void Write(VaultMetadata metadata);
}
