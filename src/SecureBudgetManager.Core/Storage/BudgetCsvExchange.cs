using System.Globalization;
using System.Text;
using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Goals;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Storage;

public sealed record CsvImportPreview(int Members, int Accounts, int Expenses, int Transactions, int Debts, int Funds, int Goals, int Duplicates)
{
    public bool HasDuplicates => Duplicates > 0;

    public string Summary =>
        $"{Members} members, {Accounts} accounts, {Expenses} expenses, {Transactions} transactions, " +
        $"{Debts} debts, {Funds} funds, {Goals} goals. {Duplicates} duplicate row(s).";
}

/// <summary>
/// Portable CSV for household records. The file contains household financial information and is
/// not a substitute for a local database backup.
/// </summary>
public static class BudgetCsvExchange
{
    public static string Export(BudgetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        builder.AppendLine("# Secure Budget Manager portable export. This file is not encrypted.");
        WriteSection(builder, "Members", ["Id", "Name", "IsDependant", "Archived", "DiscretionaryEligible"], document.Members.Select(member =>
            new[]
            {
                member.Id.ToString(),
                member.Name,
                member.IsDependant.ToString(),
                member.IsArchived.ToString(),
                member.IsDiscretionaryEligible.ToString()
            }));
        WriteSection(builder, "Income", ["Id", "Name", "Kind", "MemberId", "Frequency", "Taxable"], document.IncomeSources.Select(source =>
            new[]
            {
                source.Id.ToString(),
                source.Name,
                source.GetType().Name,
                source.MemberId.ToString(),
                source.PayFrequency.ToString(),
                source.IsTaxable.ToString()
            }));
        WriteSection(builder, "Benefits", ["Id", "Name", "MemberId", "Confirmed"], document.Benefits.Select(benefit =>
            new[]
            {
                benefit.Id.ToString(),
                benefit.Name,
                benefit.MemberId.ToString(),
                benefit.IsConfirmed.ToString()
            }));
        WriteSection(builder, "Accounts", ["Id", "Name", "Balance", "IsPrimary"], document.Accounts.Select(account =>
            new[] { account.Id.ToString(), account.Name, Format(account.CurrentBalance), account.IsPrimary.ToString() }));
        WriteSection(builder, "Expenses", ["Id", "Name", "Category", "Amount", "Frequency", "Anchor"], document.Expenses.Select(item =>
            new[]
            {
                item.Id.ToString(),
                item.Name,
                item.Category.Name,
                Format(item.ExpectedAmount),
                item.Frequency.ToString(),
                item.AnchorDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            }));
        WriteSection(builder, "Transactions", ["Id", "Date", "Description", "Amount", "Category", "Refund"], document.Transactions.Select(item =>
            new[]
            {
                item.Id.ToString(),
                item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.Description,
                Format(item.Amount),
                item.Category.Name,
                item.IsRefund.ToString()
            }));
        WriteSection(builder, "Debts", ["Id", "Name", "Kind", "Balance", "Apr", "Minimum"], document.Debts.Select(item =>
            new[]
            {
                item.Id.ToString(),
                item.Name,
                item.Kind.ToString(),
                Format(item.Balance),
                item.AnnualPercentageRate.ToString(CultureInfo.InvariantCulture),
                Format(item.MinimumPayment)
            }));
        WriteSection(builder, "Funds", ["Id", "Name", "Purpose", "Balance", "Target"], document.Funds.Select(item =>
            new[]
            {
                item.Id.ToString(),
                item.Name,
                item.Purpose.ToString(),
                Format(item.CurrentBalance),
                item.TargetAmount is { } target ? Format(target) : string.Empty
            }));
        WriteSection(builder, "Goals", ["Id", "Name", "Target", "Current", "Deadline"], document.Goals.Select(item =>
            new[]
            {
                item.Id.ToString(),
                item.Name,
                Format(item.TargetAmount),
                Format(item.CurrentAmount),
                item.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            }));
        return builder.ToString();
    }

    public static CsvImportPreview Preview(string csv)
    {
        var parsed = Parse(csv);
        return new CsvImportPreview(
            parsed.Members.Count,
            parsed.Accounts.Count,
            parsed.Expenses.Count,
            parsed.Transactions.Count,
            parsed.Debts.Count,
            parsed.Funds.Count,
            parsed.Goals.Count,
            parsed.Duplicates);
    }

    public static BudgetDocument Merge(BudgetDocument current, string csv, out IReadOnlyList<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(current);
        var parsed = Parse(csv);
        var skip = new List<string>();

        var members = current.Members.ToList();
        foreach (var member in parsed.Members)
        {
            if (members.Any(item => item.Id == member.Id || NamesMatch(item.Name, member.Name)))
            {
                skip.Add(member.Name);
                continue;
            }

            members.Add(member);
        }

        var accounts = current.Accounts.ToList();
        foreach (var account in parsed.Accounts)
        {
            if (accounts.Any(item => item.Id == account.Id || NamesMatch(item.Name, account.Name)))
            {
                skip.Add(account.Name);
                continue;
            }

            accounts.Add(account);
        }

        var expenses = current.Expenses.ToList();
        foreach (var expense in parsed.Expenses)
        {
            if (expenses.Any(item => item.Id == expense.Id || NamesMatch(item.Name, expense.Name)))
            {
                skip.Add(expense.Name);
                continue;
            }

            expenses.Add(expense);
        }

        var transactions = current.Transactions.ToList();
        foreach (var transaction in parsed.Transactions)
        {
            if (transactions.Any(item =>
                    item.Id == transaction.Id
                    || (item.Date == transaction.Date
                        && NamesMatch(item.Description, transaction.Description)
                        && item.Amount == transaction.Amount)))
            {
                skip.Add(transaction.Description);
                continue;
            }

            transactions.Add(transaction);
        }

        var debts = current.Debts.ToList();
        foreach (var debt in parsed.Debts)
        {
            if (debts.Any(item => item.Id == debt.Id || NamesMatch(item.Name, debt.Name)))
            {
                skip.Add(debt.Name);
                continue;
            }

            debts.Add(debt);
        }

        var funds = current.Funds.ToList();
        foreach (var fund in parsed.Funds)
        {
            if (funds.Any(item => item.Id == fund.Id || NamesMatch(item.Name, fund.Name)))
            {
                skip.Add(fund.Name);
                continue;
            }

            funds.Add(fund);
        }

        var goals = current.Goals.ToList();
        foreach (var goal in parsed.Goals)
        {
            if (goals.Any(item => item.Id == goal.Id || NamesMatch(item.Name, goal.Name)))
            {
                skip.Add(goal.Name);
                continue;
            }

            goals.Add(goal);
        }

        skipped = skip;
        return current with
        {
            Members = members,
            Accounts = accounts,
            Expenses = expenses,
            Transactions = transactions,
            Debts = debts,
            Funds = funds,
            Goals = goals
        };
    }

    private static Parsed Parse(string csv)
    {
        var result = new Parsed();
        if (string.IsNullOrWhiteSpace(csv))
        {
            return result;
        }

        string? section = null;
        foreach (var raw in csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith('#') || line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("SECTION,", StringComparison.OrdinalIgnoreCase))
            {
                section = line[8..];
                continue;
            }

            if (section is null || line.StartsWith("Id,", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cells = Split(line);
            try
            {
                switch (section)
                {
                    case "Members" when cells.Length >= 3:
                        result.Members.Add(new HouseholdMember
                        {
                            Id = ParseGuid(cells[0]),
                            Name = Unescape(cells[1]),
                            IsDependant = bool.Parse(cells[2])
                        });
                        break;
                    case "Accounts" when cells.Length >= 4:
                        result.Accounts.Add(new BankAccount
                        {
                            Id = ParseGuid(cells[0]),
                            Name = Unescape(cells[1]),
                            CurrentBalance = ParseMoney(cells[2]),
                            IsPrimary = bool.Parse(cells[3])
                        });
                        break;
                    case "Expenses" when cells.Length >= 6:
                        result.Expenses.Add(new ExpenseItem
                        {
                            Id = ParseGuid(cells[0]),
                            Name = Unescape(cells[1]),
                            Category = ExpenseCategory.BuiltIn.FirstOrDefault(item => item.Name == Unescape(cells[2]))
                                       ?? ExpenseCategory.Custom(Unescape(cells[2])),
                            ExpectedAmount = ParseMoney(cells[3]),
                            Frequency = Enum.Parse<Frequency>(cells[4], true),
                            AnchorDueDate = DateOnly.Parse(cells[5], CultureInfo.InvariantCulture)
                        });
                        break;
                    case "Transactions" when cells.Length >= 6:
                        result.Transactions.Add(new ExpenseTransaction
                        {
                            Id = ParseGuid(cells[0]),
                            Date = DateOnly.Parse(cells[1], CultureInfo.InvariantCulture),
                            Description = Unescape(cells[2]),
                            Amount = ParseMoney(cells[3]),
                            Category = ExpenseCategory.BuiltIn.FirstOrDefault(item => item.Name == Unescape(cells[4]))
                                       ?? ExpenseCategory.Custom(Unescape(cells[4])),
                            IsRefund = bool.Parse(cells[5])
                        });
                        break;
                    case "Debts" when cells.Length >= 6:
                        result.Debts.Add(new DebtAccount
                        {
                            Id = ParseGuid(cells[0]),
                            Name = Unescape(cells[1]),
                            Kind = Enum.Parse<DebtKind>(cells[2], true),
                            Balance = ParseMoney(cells[3]),
                            AnnualPercentageRate = decimal.Parse(cells[4], CultureInfo.InvariantCulture),
                            MinimumPayment = ParseMoney(cells[5])
                        });
                        break;
                    case "Funds" when cells.Length >= 4:
                        result.Funds.Add(new SavingsFund
                        {
                            Id = ParseGuid(cells[0]),
                            Name = Unescape(cells[1]),
                            Purpose = Enum.Parse<FundPurpose>(cells[2], true),
                            CurrentBalance = ParseMoney(cells[3]),
                            TargetAmount = cells.Length > 4 && cells[4].Length > 0 ? ParseMoney(cells[4]) : null
                        });
                        break;
                    case "Goals" when cells.Length >= 5:
                        result.Goals.Add(new Goal
                        {
                            Id = ParseGuid(cells[0]),
                            Name = Unescape(cells[1]),
                            TargetAmount = ParseMoney(cells[2]),
                            CurrentAmount = ParseMoney(cells[3]),
                            TargetDate = DateOnly.Parse(cells[4], CultureInfo.InvariantCulture)
                        });
                        break;
                }
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                result.Duplicates++;
            }
        }

        result.Duplicates += CountInternalDuplicates(result);
        return result;
    }

    private static int CountInternalDuplicates(Parsed parsed)
    {
        var count = 0;
        count += parsed.Members.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1);
        count += parsed.Transactions.GroupBy(item => (item.Date, item.Description, item.Amount)).Count(group => group.Count() > 1);
        return count;
    }

    private static void WriteSection(StringBuilder builder, string name, string[] headers, IEnumerable<string[]> rows)
    {
        builder.Append("SECTION,").AppendLine(name);
        builder.AppendLine(string.Join(',', headers));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', row.Select(Escape)));
        }
    }

    private static string[] Split(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values.ToArray();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }

    private static string Unescape(string value) => value.Trim().Trim('"');

    private static Guid ParseGuid(string value) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty ? id : Guid.NewGuid();

    private static Money ParseMoney(string value)
    {
        var trimmed = value.Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal);
        return new Money(decimal.Parse(trimmed, CultureInfo.InvariantCulture));
    }

    private static string Format(Money money) => money.Round().Amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static bool NamesMatch(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed class Parsed
    {
        public List<HouseholdMember> Members { get; } = [];
        public List<BankAccount> Accounts { get; } = [];
        public List<ExpenseItem> Expenses { get; } = [];
        public List<ExpenseTransaction> Transactions { get; } = [];
        public List<DebtAccount> Debts { get; } = [];
        public List<SavingsFund> Funds { get; } = [];
        public List<Goal> Goals { get; } = [];
        public int Duplicates { get; set; }
    }
}
