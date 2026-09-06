using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.Core.Import;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Infrastructure.Storage;

const string ExpectedEncryptedDatabaseHash =
    "DB2816977C53D5222107737390B6C896CFE9D28E3AB95ECFA692D9E23AA9B1ED";
const string ExpectedLegacyVaultHash =
    "6C0F63B432C0651B2CDA877B1D049D1B6A94B2760500D5F405DC0DBDC169308C";
const string SqliteHeader = "SQLite format 3\0";

var dataRoot = Environment.GetEnvironmentVariable(LocalDataDirectory.DataRootEnvironmentVariable);
if (!string.IsNullOrWhiteSpace(dataRoot))
{
    Console.Error.WriteLine("Refusing to run while SECURE_BUDGET_MANAGER_DATA_ROOT is set.");
    return 2;
}

var liveRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    LocalDataDirectory.ApplicationFolderName);
var databasePath = Path.Combine(liveRoot, LocalDataDirectory.DatabaseFileName);
var vaultPath = Path.Combine(liveRoot, LocalDataDirectory.VaultMetadataFileName);
var verifyOnly = args.Any(argument =>
    string.Equals(argument, "--verify-only", StringComparison.OrdinalIgnoreCase));
var payloadOverride = args
    .SkipWhile(argument => !string.Equals(argument, "--payload", StringComparison.OrdinalIgnoreCase))
    .Skip(1)
    .FirstOrDefault();
var pendingPath = payloadOverride
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SecureBudgetManager-import",
        "pending-household-import.json");
var incidentRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "My Programs",
    "SBM-incident-recovery");

Directory.CreateDirectory(liveRoot);
Directory.CreateDirectory(incidentRoot);

if (File.Exists(databasePath) && !IsSqlite(databasePath))
{
    var dbHash = Sha256(databasePath);
    if (!HashesEqual(dbHash, ExpectedEncryptedDatabaseHash))
    {
        Console.Error.WriteLine("Live household file hash does not match the known encrypted empty-state hash.");
        Console.Error.WriteLine("Actual: " + dbHash);
        return 2;
    }

    if (File.Exists(vaultPath) && !HashesEqual(Sha256(vaultPath), ExpectedLegacyVaultHash))
    {
        Console.Error.WriteLine("Live vault.json hash does not match the known pre-conversion hash.");
        return 2;
    }

    var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var legacyDir = Path.Combine(liveRoot, "LegacyEncryptedVault-" + stamp);
    Directory.CreateDirectory(legacyDir);

    var dbInfo = new FileInfo(databasePath);
    var vaultInfo = File.Exists(vaultPath) ? new FileInfo(vaultPath) : null;
    File.Move(databasePath, Path.Combine(legacyDir, LocalDataDirectory.DatabaseFileName));
    if (vaultInfo is not null)
    {
        File.Move(vaultPath, Path.Combine(legacyDir, LocalDataDirectory.VaultMetadataFileName));
    }

    var manifest = new StringBuilder();
    manifest.AppendLine("LegacyEncryptedVault");
    manifest.AppendLine("Created: " + DateTime.Now.ToString("O"));
    manifest.AppendLine("OriginalDatabaseName: " + LocalDataDirectory.DatabaseFileName);
    manifest.AppendLine("OriginalDatabaseHash: " + dbHash);
    manifest.AppendLine("OriginalDatabaseSize: " + dbInfo.Length);
    manifest.AppendLine("OriginalDatabaseLastWriteUtc: " + dbInfo.LastWriteTimeUtc.ToString("O"));
    if (vaultInfo is not null)
    {
        manifest.AppendLine("OriginalVaultName: " + LocalDataDirectory.VaultMetadataFileName);
        manifest.AppendLine("OriginalVaultHash: " + ExpectedLegacyVaultHash);
        manifest.AppendLine("OriginalVaultSize: " + vaultInfo.Length);
        manifest.AppendLine("OriginalVaultLastWriteUtc: " + vaultInfo.LastWriteTimeUtc.ToString("O"));
    }

    File.WriteAllText(Path.Combine(legacyDir, "MANIFEST.txt"), manifest.ToString());
    Console.WriteLine("Moved leftover encrypted files to " + legacyDir);
}

if (File.Exists(vaultPath))
{
    Console.Error.WriteLine("An active vault.json remains. Move it aside before seeding.");
    return 2;
}

if (!File.Exists(pendingPath))
{
    Console.Error.WriteLine("No household payload was found.");
    return File.Exists(databasePath) && IsSqlite(databasePath) ? 0 : 2;
}

var directory = new LocalDataDirectory();
using var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);
store.Open();
if (store.SchemaVersion != BudgetSchema.LatestVersion)
{
    Console.Error.WriteLine("Schema version is " + store.SchemaVersion + ", expected " + BudgetSchema.LatestVersion);
    return 2;
}

var integrity = store.CheckIntegrity();
if (!integrity.IsHealthy)
{
    Console.Error.WriteLine("New database failed integrity: " + integrity.Summary);
    return 2;
}

var repository = new BudgetRepository(store, NullLogger<BudgetRepository>.Instance);
var existing = repository.Load();
var json = File.ReadAllText(pendingPath);
var payload = HouseholdImportSerializer.Parse(json);
var incoming = payload.ToDocument();
incoming.Validate();
var merge = HouseholdImportMerger.Merge(existing, incoming);
if (merge.Preview.HasMaterialConflicts)
{
    Console.Error.WriteLine("Seeding stopped because the merge reported conflicts.");
    return 2;
}

var today = DateOnly.FromDateTime(DateTime.Now);
var verification = HouseholdImportVerifier.Verify(merge.Document, merge.Preview, payload.Verification, today);
if (!verification.AllPassed)
{
    Console.Error.WriteLine("Seeding stopped because verification failed.");
    foreach (var check in verification.Checks.Where(item => !item.Passed))
    {
        Console.Error.WriteLine("FAIL " + check.Name);
    }

    return 2;
}

if (verifyOnly)
{
    Console.WriteLine("VERIFY_ONLY");
    Console.WriteLine("Created=" + merge.Preview.Created.Count);
    Console.WriteLine("Updated=" + merge.Preview.Updated.Count);
    Console.WriteLine("Unchanged=" + merge.Preview.Unchanged.Count);
    Console.WriteLine("Conflicts=" + merge.Preview.Conflicts.Count);
    Console.WriteLine("Checks=" + verification.Checks.Count(check => check.Passed) + "/" + verification.Checks.Count);
    foreach (var check in verification.Checks)
    {
        Console.WriteLine((check.Passed ? "PASS " : "FAIL ") + check.Name);
    }

    var overviewWatch = System.Diagnostics.Stopwatch.StartNew();
    var overview = SecureBudgetManager.Core.Budgeting.BudgetOverviewCalculator.Build(
        existing,
        SecureBudgetManager.Core.Budgeting.DisplayPeriod.AverageMonthly,
        today);
    overviewWatch.Stop();
    Console.WriteLine("OverviewMs=" + overviewWatch.ElapsedMilliseconds);
    Console.WriteLine("OverviewAttention=" + overview.Attention.Count);
    Console.WriteLine("OverviewSafeUnavailable=" + overview.SafeToSpend.IsUnavailable);

    store.Close();
    return verification.AllPassed && !merge.Preview.HasMaterialConflicts ? 0 : 2;
}

repository.Save(merge.Document);
var reloaded = repository.Load();
var reloadMerge = HouseholdImportMerger.Merge(reloaded, incoming);
var reloadVerification = HouseholdImportVerifier.Verify(
    reloaded,
    reloadMerge.Preview,
    payload.Verification,
    today);
if (!reloadVerification.AllPassed)
{
    Console.Error.WriteLine("Seeding was rolled back because reload verification failed.");
    repository.Save(existing);
    return 2;
}

var report = new
{
    seededUtc = DateTime.UtcNow,
    schemaVersion = store.SchemaVersion,
    created = merge.Preview.Created.Count,
    updated = merge.Preview.Updated.Count,
    unchanged = merge.Preview.Unchanged.Count,
    conflicts = merge.Preview.Conflicts.Count,
    checksPassed = reloadVerification.Checks.Count(check => check.Passed),
    checksTotal = reloadVerification.Checks.Count,
    members = reloaded.Members.Count,
    accounts = reloaded.Accounts.Count,
    incomeSources = reloaded.IncomeSources.Count,
    payslips = reloaded.Payslips.Count,
    expenses = reloaded.Expenses.Count,
    benefits = reloaded.Benefits.Count,
    debts = reloaded.Debts.Count,
    funds = reloaded.Funds.Count,
    transfers = reloaded.InternationalTransfers.Count,
    foreignAccounts = reloaded.ForeignAccounts.Count,
    payloadRemoved = true
};

var reportPath = Path.Combine(incidentRoot, "seed-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

File.Delete(pendingPath);
var importDir = Path.GetDirectoryName(pendingPath);
if (importDir is not null && Directory.Exists(importDir))
{
    foreach (var leftover in Directory.EnumerateFileSystemEntries(importDir))
    {
        var destination = Path.Combine(incidentRoot, Path.GetFileName(leftover));
        if (Directory.Exists(leftover))
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(leftover, destination);
        }
        else
        {
            File.Move(leftover, destination, overwrite: true);
        }
    }

    if (!Directory.EnumerateFileSystemEntries(importDir).Any())
    {
        Directory.Delete(importDir);
    }
}

store.Close();
Console.WriteLine("Seeded local SQLite database.");
Console.WriteLine("Created=" + merge.Preview.Created.Count + " Updated=" + merge.Preview.Updated.Count + " Unchanged=" + merge.Preview.Unchanged.Count);
Console.WriteLine("Checks=" + reloadVerification.Checks.Count(check => check.Passed) + "/" + reloadVerification.Checks.Count);
Console.WriteLine("DatabaseSha256=" + Sha256(databasePath));
Console.WriteLine("Report=" + reportPath);
Console.WriteLine("PendingRemoved=" + (!File.Exists(pendingPath)));
return 0;

static bool IsSqlite(string path)
{
    var expected = Encoding.ASCII.GetBytes(SqliteHeader);
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    var header = new byte[expected.Length];
    return stream.Read(header, 0, header.Length) == expected.Length && header.AsSpan().SequenceEqual(expected);
}

static string Sha256(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static bool HashesEqual(string left, string right) =>
    string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
