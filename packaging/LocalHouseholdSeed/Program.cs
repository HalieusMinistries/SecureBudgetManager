using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Import;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;
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
var applyOperational = args.Any(argument =>
    string.Equals(argument, "--apply-operational", StringComparison.OrdinalIgnoreCase));
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

if (!applyOperational && !File.Exists(pendingPath))
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

if (applyOperational)
{
    return ApplyOperationalActuals(store, today: DateOnly.FromDateTime(DateTime.Today));
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

static int ApplyOperationalActuals(SqliteBudgetStore store, DateOnly today)
{
    var repository = new BudgetRepository(store, NullLogger<BudgetRepository>.Instance);
    var document = repository.Load();
    var tobaccoOwner = document.Members.FirstOrDefault(member =>
        !member.IsDependant && member.IsDiscretionaryEligible);

    var accounts = document.Accounts.Select(account =>
        account.IsPrimary || document.Accounts.Count == 1
            ? account with { CurrentBalance = Money.Zero, UpdatedOn = today }
            : account).ToList();

    var expenses = document.Expenses.Select(expense =>
    {
        if (ContainsAny(expense.Name, "t-mobile", "internet", "wifi", "wi-fi"))
        {
            if (expense.ExpectedAmount == new Money(58m) && expense.DueDateUnknown && !expense.ScheduleConfirmed)
            {
                return expense;
            }

            return expense with
            {
                ExpectedAmount = new Money(58m),
                DueDateUnknown = true,
                ScheduleConfirmed = false,
                Notes = JoinNotes(expense.Notes, "September paid at 58.00. Next amount and due date await confirmation.")
            };
        }

        if (ContainsAny(expense.Name, "rent") && expense.Category.Name == ExpenseCategory.Housing.Name)
        {
            return expense with
            {
                ExpectedAmount = new Money(1450m),
                Frequency = Frequency.Monthly,
                AnchorDueDate = new DateOnly(2026, 10, 1),
                DueDateUnknown = false,
                ScheduleConfirmed = true,
                Necessity = ExpenseNecessity.Essential
            };
        }

        if (ContainsAny(expense.Name, "uscis", "iom", "flight"))
        {
            return expense with
            {
                ExpectedAmount = new Money(116m),
                Frequency = Frequency.Monthly,
                AnchorDueDate = new DateOnly(2026, 10, 1),
                DueDateUnknown = false,
                ScheduleConfirmed = true,
                Necessity = ExpenseNecessity.Essential
            };
        }

        if (ContainsAny(expense.Name, "electric"))
        {
            return expense with
            {
                ExpectedAmount = new Money(110m),
                Frequency = Frequency.Monthly,
                DueDateUnknown = true,
                ScheduleConfirmed = false,
                Variability = ExpenseVariability.Variable,
                Necessity = ExpenseNecessity.Essential
            };
        }

        if (ContainsAny(expense.Name, "natural gas", "gas bill", "gas utility")
            || (ContainsAny(expense.Name, "gas") && expense.Category.Name == ExpenseCategory.Utilities.Name))
        {
            return expense with
            {
                ExpectedAmount = new Money(125m),
                Frequency = Frequency.Monthly,
                DueDateUnknown = true,
                ScheduleConfirmed = false,
                Variability = ExpenseVariability.Variable,
                Necessity = ExpenseNecessity.Essential
            };
        }

        if (ContainsAny(expense.Name, "insurance") && expense.Frequency == Frequency.Fortnightly)
        {
            return expense.ExpectedAmount == new Money(53m) && !expense.ScheduleConfirmed
                ? expense
                : expense with { ExpectedAmount = new Money(53m), ScheduleConfirmed = false };
        }

        return expense;
    }).ToList();

    var internet = expenses.FirstOrDefault(expense => ContainsAny(expense.Name, "t-mobile", "internet", "wifi", "wi-fi"));
    var fuel = expenses.FirstOrDefault(expense => ContainsAny(expense.Name, "fuel", "petrol", "gas") && expense.Category.Name == ExpenseCategory.Transport.Name)
               ?? expenses.FirstOrDefault(expense => ContainsAny(expense.Name, "fuel", "petrol"));
    var cigarettes = expenses.FirstOrDefault(expense => ContainsAny(expense.Name, "cigarette", "tobacco"));
    var beer = expenses.FirstOrDefault(expense => ContainsAny(expense.Name, "beer", "alcohol"));

    var income = document.IncomeSources.Select(source =>
    {
        if (source is HourlyIncome hourly && source.PayFrequency == Frequency.Weekly && source.PayScheduleConfirmed)
        {
            return hourly with
            {
                StartsOn = hourly.StartsOn ?? new DateOnly(2026, 9, 8),
                AnchorPayDate = hourly.AnchorPayDate == default ? new DateOnly(2026, 9, 11) : hourly.AnchorPayDate
            };
        }

        if (source.IsOneTimeIncome || ContainsAny(source.Name, "grifols", "plasma"))
        {
            return source with { Role = IncomeRole.OneTime, PayFrequency = source.PayFrequency };
        }

        return source;
    }).ToList();

    var originalTransactionCount = document.Transactions.Count;
    var existing = DeduplicateOperational(document.Transactions);
    var additions = new List<ExpenseTransaction>();
    AddIfMissing(existing, additions, today, "Fuel — essential transport", new Money(25m), ExpenseCategory.Transport, fuel?.Id, null, "Paid. Essential transport.");
    if (internet is not null)
    {
        AddIfMissing(existing, additions, today, "T-Mobile Home Internet — September", new Money(58m), ExpenseCategory.Utilities, internet.Id, null, "Paid. Replaces the previous September estimate. Next October amount and due date unknown.");
    }

    var storePurchaseId = Guid.NewGuid();
    if (!existing.Any(transaction => transaction.Amount == new Money(15m) && ContainsAny(transaction.Description, "cigarettes", "beer", "store")))
    {
        additions.Add(new ExpenseTransaction
        {
            Id = storePurchaseId,
            Date = today,
            Description = "Cigarettes and beer",
            Amount = new Money(15.00m),
            Category = ExpenseCategory.Personal,
            IsConfirmed = true,
            SpentByMemberId = tobaccoOwner?.Id,
            Notes = "Actual against tobacco and alcohol envelopes. Combined receipt."
        });
        additions.Add(new ExpenseTransaction
        {
            Id = Guid.NewGuid(),
            SplitParentId = storePurchaseId,
            ExpenseItemId = cigarettes?.Id,
            Date = today,
            Description = "Cigarettes before tax",
            Amount = new Money(6.09m),
            Category = ExpenseCategory.Personal,
            IsConfirmed = true,
            SpentByMemberId = tobaccoOwner?.Id,
            Notes = "Personal discretionary / tobacco. Shelf amount before tax."
        });
        additions.Add(new ExpenseTransaction
        {
            Id = Guid.NewGuid(),
            SplitParentId = storePurchaseId,
            ExpenseItemId = beer?.Id,
            Date = today,
            Description = "Beer before tax",
            Amount = new Money(7.45m),
            Category = ExpenseCategory.Personal,
            IsConfirmed = true,
            Notes = "Five beers at 1.49 each before tax. Alcohol discretionary."
        });
        additions.Add(new ExpenseTransaction
        {
            Id = Guid.NewGuid(),
            SplitParentId = storePurchaseId,
            Date = today,
            Description = "Combined sales or tobacco-related tax",
            Amount = new Money(1.46m),
            Category = ExpenseCategory.Personal,
            IsConfirmed = true,
            Notes = "Difference between 15.00 paid and 13.54 pre-tax subtotal. Combined tax pending receipt confirmation. Not a invented split of the tax."
        });
    }

    var updated = document with
    {
        Accounts = accounts,
        Expenses = expenses,
        IncomeSources = income,
        Transactions = existing.Concat(additions).ToList()
    };
    updated.Validate();
    var changed = additions.Count > 0
                  || existing.Count != originalTransactionCount
                  || document.Accounts.Zip(updated.Accounts, (left, right) => !Equals(left, right)).Any(item => item)
                  || document.Expenses.Zip(updated.Expenses, (left, right) => !Equals(left, right)).Any(item => item)
                  || document.IncomeSources.Zip(updated.IncomeSources, (left, right) => !Equals(left, right)).Any(item => item);
    if (changed)
    {
        repository.Save(updated);
    }

    var reloaded = changed ? repository.Load() : updated;
    Console.WriteLine(changed ? "Updated=" + Math.Max(1, additions.Count) : "Updated=0");
    Console.WriteLine("Created=" + additions.Count);
    return ReportOperational(reloaded, today, additions.Count);
}

static int ReportOperational(BudgetDocument document, DateOnly today, int added)
{
    var position = OperationalPositionCalculator.Build(document, today);
    var register = ObligationRegister.Build(document, today);
    var topLevel = document.Transactions.Where(transaction => transaction.SplitParentId is null).ToList();
    var operational = topLevel.Where(transaction =>
        (transaction.Amount == new Money(25m) && ContainsAny(transaction.Description, "fuel"))
        || (transaction.Amount == new Money(58m) && ContainsAny(transaction.Description, "internet", "t-mobile"))
        || (transaction.Amount == new Money(15m) && ContainsAny(transaction.Description, "cigarettes", "beer"))).ToList();
    var spent = Money.Sum(operational.Select(transaction => transaction.Amount)).Round();
    var internetPaid = document.Transactions.Count(transaction =>
        transaction.SplitParentId is null
        && transaction.Amount == new Money(58m)
        && ContainsAny(transaction.Description, "internet", "t-mobile"));
    var internetExpenses = document.Expenses.Count(expense =>
        ContainsAny(expense.Name, "t-mobile", "internet", "wifi", "wi-fi")
        && !expense.IsArchived);
    var internetLine = register.Lines.FirstOrDefault(line =>
        ContainsAny(line.Name, "t-mobile", "internet", "wifi", "wi-fi"));
    var paidReservedStill = (register.TotalPaid + register.TotalReserved + register.TotalStillRequired).Round();

    Console.WriteLine("Operational actuals recorded through BudgetRepository.");
    Console.WriteLine("Added=" + added);
    Console.WriteLine("AvailableNow=" + position.AvailableNow.ToDisplayString());
    Console.WriteLine("SafeToSpend=" + position.SafeToSpend.ToDisplayString());
    Console.WriteLine("Reserved=" + position.Reserved.ToDisplayString());
    Console.WriteLine("Required=" + position.EssentialRequired.ToDisplayString());
    Console.WriteLine("Funded=" + position.EssentialFunded.ToDisplayString());
    Console.WriteLine("Shortfall=" + position.EssentialShortfall.ToDisplayString());
    Console.WriteLine("NextIncome=" + (position.NextConfirmedIncomeDate?.ToString("yyyy-MM-dd") ?? "none"));
    Console.WriteLine("OperationalTransactions=" + spent.ToDisplayString());
    Console.WriteLine("InternetPaidRows=" + internetPaid);
    Console.WriteLine("InternetExpenses=" + internetExpenses);
    Console.WriteLine("InternetStatus=" + (internetLine?.StatusText ?? "missing"));
    Console.WriteLine("InternetPaidAmount=" + (internetLine?.AmountPaid.ToDisplayString() ?? "none"));
    Console.WriteLine("ForecastSurplus=" + position.ForecastMonthlySurplus.ToDisplayString());
    Console.WriteLine("SafetyNotice=" + position.SafetyNotice);
    Console.WriteLine("HarmedObligation=" + position.HarmedObligation);
    Console.WriteLine("RegisterRequired=" + register.TotalRequired.ToDisplayString());
    Console.WriteLine("RegisterPaidReservedStill=" + paidReservedStill.ToDisplayString());
    Console.WriteLine("BillsBeforeIncome=");
    foreach (var item in position.BillsRequiringAttention)
    {
        Console.WriteLine("  " + item);
    }

    var ok = position.AvailableNow.IsZero
             && position.SafeToSpend.IsZero
             && spent == new Money(98m)
             && internetPaid == 1
             && internetExpenses == 1
             && internetLine is { AmountPaid: var paid } && paid == new Money(58m)
             && internetLine.Statuses.Contains("Paid", StringComparer.Ordinal)
             && (position.EssentialFunded + position.EssentialShortfall).Round() == position.EssentialRequired.Round();
    return ok ? 0 : 2;
}

static void AddIfMissing(
    IReadOnlyList<ExpenseTransaction> existing,
    List<ExpenseTransaction> additions,
    DateOnly today,
    string description,
    Money amount,
    ExpenseCategory category,
    Guid? expenseId,
    Guid? memberId,
    string notes)
{
    var token = description.Split(['—', '-'], 2)[0].Trim();
    if (existing.Any(transaction =>
            transaction.SplitParentId is null
            && transaction.Amount == amount
            && ContainsAny(transaction.Description, token)))
    {
        return;
    }

    additions.Add(new ExpenseTransaction
    {
        Id = Guid.NewGuid(),
        ExpenseItemId = expenseId,
        Date = today,
        Description = description,
        Amount = amount,
        Category = category,
        IsConfirmed = true,
        SpentByMemberId = memberId,
        Notes = notes
    });
}

static IReadOnlyList<ExpenseTransaction> DeduplicateOperational(IReadOnlyList<ExpenseTransaction> existing)
{
    var keep = existing.ToList();
    RemoveExtra(keep, amount => amount == new Money(25m), description => ContainsAny(description, "fuel") && ContainsAny(description, "essential transport"));
    RemoveExtra(keep, amount => amount == new Money(58m), description => ContainsAny(description, "t-mobile") || ContainsAny(description, "home internet"));
    RemoveExtra(keep, amount => amount == new Money(15m), description => ContainsAny(description, "cigarettes") && ContainsAny(description, "beer"));
    RemoveExtra(keep, amount => amount == new Money(6.09m), description => ContainsAny(description, "cigarettes before tax"));
    RemoveExtra(keep, amount => amount == new Money(7.45m), description => ContainsAny(description, "beer before tax"));
    RemoveExtra(keep, amount => amount == new Money(1.46m), description => ContainsAny(description, "tobacco-related tax"));
    return keep;
}

static void RemoveExtra(
    List<ExpenseTransaction> transactions,
    Func<Money, bool> amountMatch,
    Func<string, bool> descriptionMatch)
{
    var matches = transactions
        .Where(transaction => amountMatch(transaction.Amount) && descriptionMatch(transaction.Description))
        .OrderBy(transaction => transaction.Date)
        .ThenBy(transaction => transaction.Id)
        .ToList();
    foreach (var extra in matches.Skip(1))
    {
        transactions.Remove(extra);
    }
}

static bool ContainsAny(string value, params string[] tokens) =>
    tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

static string JoinNotes(string? existing, string extra) =>
    string.IsNullOrWhiteSpace(existing) ? extra : existing.Trim() + " " + extra;
