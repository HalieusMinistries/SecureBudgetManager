using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Planning;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Import;

/// <summary>
/// Idempotent merge of an incoming household snapshot into the document already on disk.
/// Matching uses stable identifiers first, then normalised names. Incoming values replace existing
/// ones only when this import supplies a more complete figure; irreconcilable differences are
/// listed as conflicts rather than silently duplicated.
/// </summary>
public static class HouseholdImportMerger
{
    public static ImportMergeResult Merge(BudgetDocument existing, BudgetDocument incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);
        incoming.Validate();

        var created = new List<string>();
        var updated = new List<string>();
        var unchanged = new List<string>();
        var conflicts = new List<string>();

        var memberMap = new Dictionary<Guid, Guid>();
        var members = MergeList(
            existing.Members,
            incoming.Members,
            member => member.Id,
            (left, right) => NamesMatch(left.Name, right.Name),
            (left, right) =>
            {
                memberMap[right.Id] = left.Id;
                if (left.IsDependant != right.IsDependant
                    || left.IsDiscretionaryEligible != right.IsDiscretionaryEligible)
                {
                    conflicts.Add($"Member \"{left.Name}\" differs in dependant or discretionary status.");
                }

                return left with
                {
                    Name = right.Name.Trim(),
                    IsDependant = right.IsDependant,
                    IsDiscretionaryEligible = right.IsDiscretionaryEligible,
                    DateOfBirth = right.DateOfBirth ?? left.DateOfBirth,
                    PersonalAllowance = right.PersonalAllowance,
                    PersonalAllowanceFrequency = right.PersonalAllowanceFrequency
                };
            },
            member =>
            {
                memberMap[member.Id] = member.Id;
                return member;
            },
            member => $"Member {member.Name}",
            created,
            updated,
            unchanged);

        foreach (var member in incoming.Members)
        {
            memberMap.TryAdd(member.Id, member.Id);
        }

        Guid MapMember(Guid id) => memberMap.TryGetValue(id, out var mapped) ? mapped : id;
        Guid? MapNullableMember(Guid? id) => id is { } value ? MapMember(value) : null;

        var householdName = existing.HouseholdName;
        if (string.IsNullOrWhiteSpace(existing.HouseholdName)
            || existing.HouseholdName == "Our household")
        {
            householdName = incoming.HouseholdName;
            if (!string.Equals(existing.HouseholdName, incoming.HouseholdName, StringComparison.Ordinal))
            {
                created.Add($"Household name {incoming.HouseholdName}");
            }
        }
        else if (!NamesMatch(existing.HouseholdName, incoming.HouseholdName))
        {
            conflicts.Add(
                $"Household is already named \"{existing.HouseholdName}\"; incoming name is \"{incoming.HouseholdName}\".");
        }
        else
        {
            unchanged.Add("Household name");
        }

        var accounts = MergeList(
            existing.Accounts,
            incoming.Accounts.Select(account => account with { OwnerMemberId = MapNullableMember(account.OwnerMemberId) }).ToList(),
            account => account.Id,
            (left, right) => NamesMatch(left.Name, right.Name) && left.OwnerMemberId == right.OwnerMemberId,
            (left, right) =>
            {
                if (left.CurrentBalance != right.CurrentBalance && !left.CurrentBalance.IsZero)
                {
                    conflicts.Add($"Account \"{left.Name}\" already has a different balance.");
                }

                return left with
                {
                    Name = right.Name,
                    CurrentBalance = right.CurrentBalance,
                    IsPrimary = right.IsPrimary,
                    OwnerMemberId = right.OwnerMemberId,
                    UpdatedOn = right.UpdatedOn
                };
            },
            account => account,
            account => $"Account {account.Name}",
            created,
            updated,
            unchanged);

        var remappedIncomingIncome = incoming.IncomeSources
            .Select(RemapIncome)
            .ToList();

        var income = MergeList(
            existing.IncomeSources,
            remappedIncomingIncome,
            source => source.Id,
            IncomeMatches,
            MergeIncome,
            source => source,
            source => $"Income {source.Name}",
            created,
            updated,
            unchanged);

        var incomeMap = new Dictionary<Guid, Guid>();
        foreach (var incomingSource in remappedIncomingIncome)
        {
            var survivor = income.First(item =>
                item.Id == incomingSource.Id || IncomeMatches(item, incomingSource));
            incomeMap[incomingSource.Id] = survivor.Id;
        }

        Guid MapIncome(Guid id) => incomeMap.TryGetValue(id, out var mapped) ? mapped : id;

        var payslips = MergeList(
            existing.Payslips,
            incoming.Payslips.Select(slip => slip with { IncomeSourceId = MapIncome(slip.IncomeSourceId) }).ToList(),
            slip => slip.Id,
            (left, right) => left.IncomeSourceId == right.IncomeSourceId && left.PayDate == right.PayDate,
            (_, right) => right,
            slip => slip,
            slip => $"Payslip {slip.PayDate:yyyy-MM-dd}",
            created,
            updated,
            unchanged);

        var payroll = MergeList(
            existing.PayrollProfiles,
            incoming.PayrollProfiles.Select(profile => profile with { MemberId = MapMember(profile.MemberId) }).ToList(),
            profile => profile.MemberId,
            (left, right) => left.MemberId == right.MemberId,
            (_, right) => right,
            profile => profile,
            profile => "Payroll profile",
            created,
            updated,
            unchanged);

        var benefits = MergeList(
            existing.Benefits,
            incoming.Benefits.Select(benefit => benefit with { MemberId = MapMember(benefit.MemberId) }).ToList(),
            benefit => benefit.Id,
            (left, right) => left.MemberId == right.MemberId
                             && left.Kind == right.Kind
                             && NamesMatch(left.Name, right.Name),
            (left, right) =>
            {
                if (left.EmployeePremiumPerPeriod != right.EmployeePremiumPerPeriod
                    && !left.EmployeePremiumPerPeriod.IsZero
                    && left.IsConfirmed)
                {
                    conflicts.Add($"Benefit \"{left.Name}\" already has a different confirmed premium.");
                }

                return right with { Id = left.Id, MemberId = left.MemberId };
            },
            benefit => benefit,
            benefit => $"Benefit {benefit.Name}",
            created,
            updated,
            unchanged);

        var expenses = MergeList(
            existing.Expenses,
            incoming.Expenses.Select(RemapExpense).ToList(),
            expense => expense.Id,
            ExpenseMatches,
            (left, right) =>
            {
                if (left.ExpectedAmount != right.ExpectedAmount && !left.ExpectedAmount.IsZero)
                {
                    conflicts.Add($"Expense \"{left.Name}\" already has a different amount.");
                }

                return right with { Id = left.Id };
            },
            expense => expense,
            expense => $"Expense {expense.Name}",
            created,
            updated,
            unchanged);

        var debts = MergeList(
            existing.Debts,
            incoming.Debts.Select(debt => debt with { OwnerMemberId = MapNullableMember(debt.OwnerMemberId) }).ToList(),
            debt => debt.Id,
            (left, right) => NamesMatch(left.Name, right.Name),
            (left, right) =>
            {
                if (left.Balance != right.Balance && !left.Balance.IsZero)
                {
                    conflicts.Add($"Debt \"{left.Name}\" already has a different balance.");
                }

                return right with { Id = left.Id, OwnerMemberId = left.OwnerMemberId ?? right.OwnerMemberId };
            },
            debt => debt,
            debt => $"Debt {debt.Name}",
            created,
            updated,
            unchanged);

        var funds = MergeList(
            existing.Funds,
            incoming.Funds.Select(fund => fund with { OwnerMemberId = MapNullableMember(fund.OwnerMemberId) }).ToList(),
            fund => fund.Id,
            (left, right) => left.Purpose == right.Purpose && NamesMatch(left.Name, right.Name),
            (left, right) => right with { Id = left.Id, CurrentBalance = left.CurrentBalance.IsZero ? right.CurrentBalance : left.CurrentBalance },
            fund => fund,
            fund => $"Fund {fund.Name}",
            created,
            updated,
            unchanged);

        var grocery = MergeList(
            existing.GroceryPlans,
            incoming.GroceryPlans,
            plan => plan.Id,
            (left, right) => left.Kind == right.Kind,
            (_, right) => right,
            plan => plan,
            plan => plan.Kind.ToDisplayName(),
            created,
            updated,
            unchanged);

        var foreign = MergeList(
            existing.ForeignAccounts,
            incoming.ForeignAccounts.Select(account => account with { OwnerMemberId = MapNullableMember(account.OwnerMemberId) }).ToList(),
            account => account.Id,
            (left, right) => NamesMatch(left.Nickname, right.Nickname) && NamesMatch(left.Institution, right.Institution),
            (left, right) => right with { Id = left.Id },
            account => account,
            account => $"Foreign account {account.Nickname}",
            created,
            updated,
            unchanged);

        var scenarios = MergeList(
            existing.Scenarios,
            incoming.Scenarios,
            scenario => scenario.Id,
            (left, right) => NamesMatch(left.Name, right.Name) && left.Kind == right.Kind,
            (left, right) => right with { Id = left.Id },
            scenario => scenario,
            scenario => $"Scenario {scenario.Name}",
            created,
            updated,
            unchanged);

        var questions = existing.OutstandingQuestions
            .Concat(incoming.OutstandingQuestions)
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .Select(question => question.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var document = existing with
        {
            HouseholdName = householdName,
            Preferences = MergePreferences(existing.Preferences, incoming.Preferences),
            Locality = MergeLocality(existing.Locality, incoming.Locality),
            Rules = MergeRules(existing.Rules, incoming.Rules),
            Members = members,
            Accounts = accounts,
            IncomeSources = income,
            Payslips = payslips,
            PayrollProfiles = payroll,
            Benefits = benefits,
            Expenses = expenses,
            Debts = debts,
            Funds = funds,
            GroceryPlans = grocery,
            ForeignAccounts = foreign,
            Scenarios = scenarios,
            OutstandingQuestions = questions
        };

        document.Validate();
        return new ImportMergeResult(document, new ImportPreview(created, updated, unchanged, conflicts));

        IncomeSource RemapIncome(IncomeSource source) => source with { MemberId = MapMember(source.MemberId) };

        ExpenseItem RemapExpense(ExpenseItem expense)
        {
            var split = expense.Split is { } rule
                ? rule with { Participants = rule.Participants.Select(MapMember).ToList() }
                : null;
            return expense with { Split = split };
        }
    }

    private static HouseholdPreferences MergePreferences(HouseholdPreferences existing, HouseholdPreferences incoming)
    {
        if (existing.MinimumBalanceReserve.IsZero && incoming.MinimumBalanceReserve > Money.Zero)
        {
            existing = existing with { MinimumBalanceReserve = incoming.MinimumBalanceReserve };
        }

        if (incoming.EmergencyFundTargetMonths > 0m)
        {
            existing = existing with { EmergencyFundTargetMonths = incoming.EmergencyFundTargetMonths };
        }

        if (incoming.ForecastHorizonMonths > 0)
        {
            existing = existing with { ForecastHorizonMonths = incoming.ForecastHorizonMonths };
        }

        return existing;
    }

    private static CostLocality MergeLocality(CostLocality existing, CostLocality incoming)
    {
        if (string.IsNullOrWhiteSpace(existing.State) && !string.IsNullOrWhiteSpace(incoming.State))
        {
            return incoming;
        }

        return existing with
        {
            Country = string.IsNullOrWhiteSpace(incoming.Country) ? existing.Country : incoming.Country,
            State = incoming.State ?? existing.State,
            County = incoming.County ?? existing.County,
            City = incoming.City ?? existing.City
        };
    }

    private static AllocationRules MergeRules(AllocationRules existing, AllocationRules incoming) => existing with
    {
        Basis = incoming.Basis,
        OptimisticIncomeMayFundEssentials = incoming.OptimisticIncomeMayFundEssentials,
        DiscretionaryMethod = incoming.DiscretionaryMethod,
        DiscretionaryPercent = incoming.DiscretionaryPercent,
        DiscretionaryPercentConfigured = incoming.DiscretionaryPercentConfigured,
        ReductionOrder = incoming.ReductionOrder.Count == 0 ? existing.ReductionOrder : incoming.ReductionOrder,
        FundingOrder = incoming.FundingOrder.Count == 0 ? existing.FundingOrder : incoming.FundingOrder,
        Notes = incoming.Notes ?? existing.Notes
    };

    private static bool IncomeMatches(IncomeSource left, IncomeSource right)
    {
        if (left.MemberId != right.MemberId || left.GetType() != right.GetType())
        {
            return false;
        }

        return NamesMatch(left.Name, right.Name);
    }

    private static IncomeSource MergeIncome(IncomeSource left, IncomeSource right)
    {
        if (left is HourlyIncome existingHourly && right is HourlyIncome incomingHourly
            && existingHourly.HourlyRate != incomingHourly.HourlyRate
            && !existingHourly.HourlyRate.IsZero)
        {
            // Still apply the incoming authoritative rate; the caller records a conflict.
        }

        return right with { Id = left.Id, MemberId = left.MemberId };
    }

    private static bool ExpenseMatches(ExpenseItem left, ExpenseItem right) =>
        NamesMatch(left.Name, right.Name)
        && string.Equals(left.Category.Name, right.Category.Name, StringComparison.OrdinalIgnoreCase)
        && left.Frequency == right.Frequency;

    private static List<T> MergeList<T>(
        IReadOnlyList<T> existing,
        IReadOnlyList<T> incoming,
        Func<T, Guid> idOf,
        Func<T, T, bool> matches,
        Func<T, T, T> merge,
        Func<T, T> onCreate,
        Func<T, string> describe,
        List<string> created,
        List<string> updated,
        List<string> unchanged)
    {
        var result = existing.ToList();
        var used = new HashSet<int>();

        foreach (var candidate in incoming)
        {
            var id = idOf(candidate);
            var index = result.FindIndex(item => idOf(item) == id && id != Guid.Empty);

            if (index < 0)
            {
                for (var position = 0; position < result.Count; position++)
                {
                    if (!used.Contains(position) && matches(result[position], candidate))
                    {
                        index = position;
                        break;
                    }
                }
            }

            if (index < 0)
            {
                result.Add(onCreate(candidate));
                created.Add(describe(candidate));
                continue;
            }

            used.Add(index);
            var merged = merge(result[index], candidate);
            if (Equals(merged, result[index]))
            {
                unchanged.Add(describe(candidate));
            }
            else
            {
                result[index] = merged;
                updated.Add(describe(candidate));
            }
        }

        return result;
    }

    private static bool NamesMatch(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();
}
