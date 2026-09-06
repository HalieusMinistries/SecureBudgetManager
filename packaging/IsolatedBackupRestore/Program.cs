using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;
using SecureBudgetManager.Infrastructure.Backup;
using SecureBudgetManager.Infrastructure.Security;
using SecureBudgetManager.Infrastructure.Storage;

const string SqliteHeader = "SQLite format 3\0";

var dataRoot = Environment.GetEnvironmentVariable(LocalDataDirectory.DataRootEnvironmentVariable);
if (string.IsNullOrWhiteSpace(dataRoot))
{
    Console.Error.WriteLine("SECURE_BUDGET_MANAGER_DATA_ROOT must point at an isolated folder.");
    return 2;
}

dataRoot = Path.GetFullPath(dataRoot);
var liveRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    LocalDataDirectory.ApplicationFolderName);
if (PathsEqual(dataRoot, liveRoot) || dataRoot.StartsWith(liveRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Refusing to run against the live household data directory.");
    return 2;
}

Directory.CreateDirectory(dataRoot);
var results = new List<string>();
var failed = false;

try
{
    var alex = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var sam = Guid.Parse("22222222-2222-2222-2222-222222222222");
    var child = Guid.Parse("33333333-3333-3333-3333-333333333333");
    var jobId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    var rentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    var benefitId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    var fundId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    var debtId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    var txId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    var accountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    var document = new BudgetDocument
    {
        HouseholdName = "Cedar Sample Household",
        Members =
        [
            new HouseholdMember { Id = alex, Name = "Alex", IsDiscretionaryEligible = true },
            new HouseholdMember { Id = sam, Name = "Sam", IsDiscretionaryEligible = true },
            new HouseholdMember { Id = child, Name = "River", IsDependant = true, ParticipatesInSharedCosts = false }
        ],
        Accounts =
        [
            new BankAccount
            {
                Id = accountId,
                Name = "Shared checking",
                CurrentBalance = new Money(214.50m),
                IsPrimary = true,
                UpdatedOn = new DateOnly(2026, 9, 4)
            }
        ],
        IncomeSources =
        [
            new HourlyIncome
            {
                Id = jobId,
                Name = "Warehouse",
                MemberId = alex,
                PayFrequency = Frequency.Weekly,
                AnchorPayDate = new DateOnly(2026, 9, 11),
                HourlyRate = new Money(18.00m),
                WeeklyHours = VariableHours.Standard
            }
        ],
        Expenses =
        [
            new ExpenseItem
            {
                Id = rentId,
                Name = "Sample rent",
                Category = ExpenseCategory.Housing,
                ExpectedAmount = new Money(950m),
                Frequency = Frequency.Monthly,
                AnchorDueDate = new DateOnly(2026, 10, 1)
            }
        ],
        Benefits =
        [
            new BenefitPlan
            {
                Id = benefitId,
                Name = "Sample medical",
                Kind = BenefitKind.Medical,
                MemberId = alex,
                EmployeePremiumPerPeriod = new Money(42.10m),
                PremiumFrequency = Frequency.Weekly,
                IsConfirmed = true
            }
        ],
        Funds =
        [
            new SavingsFund
            {
                Id = fundId,
                Name = "Sample emergency",
                Purpose = FundPurpose.EmergencyFund,
                CurrentBalance = new Money(75.00m),
                TargetAmount = new Money(300m),
                PlannedContribution = new Money(25m),
                ContributionFrequency = Frequency.Weekly
            }
        ],
        Debts =
        [
            new DebtAccount
            {
                Id = debtId,
                Name = "Sample card",
                Kind = DebtKind.CreditCard,
                Balance = new Money(180m),
                AnnualPercentageRate = 0m,
                MinimumPayment = new Money(15m),
                DueDayOfMonth = 1
            }
        ],
        Transactions =
        [
            new ExpenseTransaction
            {
                Id = txId,
                ExpenseItemId = rentId,
                Date = new DateOnly(2026, 9, 1),
                Description = "September rent",
                Amount = new Money(950m),
                Category = ExpenseCategory.Housing,
                IsConfirmed = true
            }
        ]
    };

    document.Validate();

    string backupPath;
    string manifestPath;
    string backupHash;
    string dbHashBeforeMove;
    int schema;
    long backupSize;

    {
        using var first = OpenHost();
        await first.Vault.EnsureReadyAsync();
        first.Repository.Save(document);
        first.Vault.Lock();
        Pass(results, "Created fictional local database and saved representative records.");
    }

    {
        using var unlocked = OpenHost();
        var opened = await unlocked.Vault.RevealAsync();
        if (!opened.Succeeded)
        {
            throw new InvalidOperationException("The fictional local database did not open.");
        }

        var backupDir = Path.Combine(dataRoot, "exercise-backups");
        var record = await unlocked.Backups.CreateBackupAsync(backupDir);
        backupPath = record.BackupFilePath;
        manifestPath = record.ManifestFilePath;
        backupHash = Sha256(record.BackupFilePath);
        schema = unlocked.Store.SchemaVersion;
        backupSize = record.Manifest.SizeBytes;
        Pass(results, $"Local database backup created. schema={schema} size={backupSize} sha256={backupHash}");

        if (!IsPlainSqlite(record.BackupFilePath))
        {
            throw new InvalidOperationException("A backup was not stored as ordinary SQLite.");
        }

        unlocked.Vault.Lock();
        unlocked.Store.Close();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        dbHashBeforeMove = Sha256(unlocked.Directory.GetDatabasePath());
        if (!IsPlainSqlite(unlocked.Directory.GetDatabasePath()))
        {
            throw new InvalidOperationException("The isolated working database was not ordinary SQLite.");
        }
    }

    var workingDb = Path.Combine(dataRoot, LocalDataDirectory.DatabaseFileName);
    var relocated = workingDb + ".relocated";
    MoveWhenReleased(workingDb, relocated);
    Pass(results, "Relocated the isolated working database.");

    var restoreRoot = Path.Combine(dataRoot, "restore-target");
    Directory.CreateDirectory(restoreRoot);
    Environment.SetEnvironmentVariable(LocalDataDirectory.DataRootEnvironmentVariable, restoreRoot);

    using (var restoreHost = OpenHost())
    {
        await restoreHost.Backups.RestoreBackupAsync(backupPath);
        var right = await restoreHost.Vault.RevealAsync();
        if (!right.Succeeded)
        {
            throw new InvalidOperationException("The restored local database did not open.");
        }

        var restored = restoreHost.Repository.Load();
        AssertEqual("Cedar Sample Household", restored.HouseholdName);
        AssertEqual("Alex", restored.Members.Single(member => member.Id == alex).Name);
        AssertEqual("Sam", restored.Members.Single(member => member.Id == sam).Name);
        AssertEqual("River", restored.Members.Single(member => member.Id == child).Name);
        if (!restored.Members.Single(member => member.Id == child).IsDependant)
        {
            throw new InvalidOperationException("River must remain a dependant after restore.");
        }
        AssertEqual(new Money(214.50m), restored.Accounts.Single().CurrentBalance);
        AssertEqual("Warehouse", restored.IncomeSources.Single().Name);
        AssertEqual(new Money(18.00m), ((HourlyIncome)restored.IncomeSources.Single()).HourlyRate);
        AssertEqual(new Money(950m), restored.Expenses.Single().ExpectedAmount);
        AssertEqual(new Money(42.10m), restored.Benefits.Single().EmployeePremiumPerPeriod);
        AssertEqual(new Money(75.00m), restored.Funds.Single().CurrentBalance);
        AssertEqual(new Money(180m), restored.Debts.Single().Balance);
        AssertEqual(new Money(950m), restored.Transactions.Single().Amount);
        AssertEqual(schema, restoreHost.Store.SchemaVersion);
        AssertEqual(BudgetSchema.LatestVersion, restoreHost.Store.SchemaVersion);
        if (!IsPlainSqlite(restoreHost.Directory.GetDatabasePath()))
        {
            throw new InvalidOperationException("The restored database was not ordinary SQLite.");
        }

        Pass(results, $"Reloaded restored records. schema={restoreHost.Store.SchemaVersion} dbSha256={Sha256(restoreHost.Directory.GetDatabasePath())}");

        var corruptPath = Path.Combine(restoreRoot, "corrupt.sbmbak");
        File.Copy(backupPath, corruptPath);
        File.Copy(manifestPath, corruptPath + EncryptedBackupService.ManifestExtension);
        var corruptBytes = File.ReadAllBytes(corruptPath);
        corruptBytes[^1] ^= 0x5A;
        File.WriteAllBytes(corruptPath, corruptBytes);
        var corruptCheck = await restoreHost.Backups.ValidateBackupAsync(corruptPath);
        if (corruptCheck.IsValid)
        {
            throw new InvalidOperationException("A corrupted backup was accepted.");
        }

        var liveBeforeFailedRestore = Sha256(restoreHost.Directory.GetDatabasePath());
        var failedRestore = false;
        try
        {
            await restoreHost.Backups.RestoreBackupAsync(corruptPath);
        }
        catch (DatabaseCorruptException)
        {
            failedRestore = true;
        }

        if (!failedRestore)
        {
            throw new InvalidOperationException("A corrupted backup was restored.");
        }

        var liveAfterFailedRestore = Sha256(restoreHost.Directory.GetDatabasePath());
        if (!string.Equals(liveBeforeFailedRestore, liveAfterFailedRestore, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A failed restore changed the previous valid database.");
        }

        var stillLoaded = restoreHost.Store.IsOpen
            ? restoreHost.Repository.Load()
            : ReloadAfter(restoreHost);
        AssertEqual(new Money(214.50m), stillLoaded.Accounts.Single().CurrentBalance);
        Pass(results, $"Corrupt backup refused ({string.Join("; ", corruptCheck.Problems)}). Failed restore left the valid database in place.");
        Pass(results, $"Backup hash before relocate={backupHash}; original isolated db hash={dbHashBeforeMove}.");
    }
}
catch (Exception exception)
{
    failed = true;
    results.Add("FAIL " + exception.GetType().Name + ": " + exception.Message);
}

try
{
    if (Directory.Exists(dataRoot))
    {
        Directory.Delete(dataRoot, recursive: true);
        Pass(results, "Deleted isolated fictional test data.");
    }
}
catch (Exception exception)
{
    failed = true;
    results.Add("FAIL cleanup: " + exception.Message);
}

foreach (var line in results)
{
    Console.WriteLine(line);
}

return failed ? 1 : 0;

static Host OpenHost()
{
    var directory = new LocalDataDirectory();
    directory.EnsureCreated();
    var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);
    var vault = new VaultService(store, NullLogger<VaultService>.Instance);
    return new Host(
        directory,
        store,
        vault,
        new BudgetRepository(store, NullLogger<BudgetRepository>.Instance),
        new EncryptedBackupService(store, directory, NullLogger<EncryptedBackupService>.Instance, TimeProvider.System));
}

static BudgetDocument ReloadAfter(Host host)
{
    host.Vault.Lock();
    var opened = host.Vault.RevealAsync().GetAwaiter().GetResult();
    if (!opened.Succeeded)
    {
        throw new InvalidOperationException("Could not reopen the isolated database after the failed restore.");
    }

    return host.Repository.Load();
}

static void Pass(List<string> results, string message) => results.Add("PASS " + message);

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, found {actual}.");
    }
}

static void MoveWhenReleased(string source, string destination)
{
    const int attempts = 20;
    for (var attempt = 1; attempt <= attempts; attempt++)
    {
        try
        {
            File.Move(source, destination);
            return;
        }
        catch (IOException) when (attempt < attempts)
        {
            Thread.Sleep(100 * attempt);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}

static bool IsPlainSqlite(string path)
{
    var expected = Encoding.ASCII.GetBytes(SqliteHeader);
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    var header = new byte[expected.Length];
    var read = stream.Read(header, 0, header.Length);
    return read == expected.Length && header.AsSpan().SequenceEqual(expected);
}

static string Sha256(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static bool PathsEqual(string left, string right) =>
    string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);

sealed record Host(
    LocalDataDirectory Directory,
    SqliteBudgetStore Store,
    VaultService Vault,
    BudgetRepository Repository,
    EncryptedBackupService Backups) : IDisposable
{
    public void Dispose()
    {
        Vault.Dispose();
        Store.Dispose();
    }
}
