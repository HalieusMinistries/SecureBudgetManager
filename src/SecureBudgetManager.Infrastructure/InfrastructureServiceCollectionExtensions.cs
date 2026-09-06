using Microsoft.Extensions.DependencyInjection;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Infrastructure.Backup;
using SecureBudgetManager.Infrastructure.Locking;
using SecureBudgetManager.Infrastructure.Security;
using SecureBudgetManager.Infrastructure.Storage;

namespace SecureBudgetManager.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILocalDataDirectory, LocalDataDirectory>();
        services.AddSingleton<IKeyDerivation, Argon2idKeyDerivation>();
        services.AddSingleton<IKeyWrapper, AesGcmKeyWrapper>();
        services.AddSingleton<IVaultMetadataRepository, VaultMetadataRepository>();

        services.AddSingleton<SqliteBudgetStore>();
        services.AddSingleton<ILocalBudgetStore>(provider => provider.GetRequiredService<SqliteBudgetStore>());
        services.AddSingleton<IBudgetDatabase>(provider => provider.GetRequiredService<SqliteBudgetStore>());
        services.AddSingleton<IBudgetRepository, BudgetRepository>();

        services.AddSingleton<IVaultService, VaultService>();
        services.AddSingleton<ISecureBackupService, EncryptedBackupService>();
        services.AddSingleton<IApplicationLock, VaultApplicationLock>();

        return services;
    }
}
