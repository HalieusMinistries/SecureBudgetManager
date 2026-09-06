using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Goals;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Planning;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;
using SecureBudgetManager.Infrastructure.Storage;
using SecureBudgetManager.Tests.TestSupport;

namespace SecureBudgetManager.Tests.Storage;

/// <summary>
/// A saved household must come back exactly as it went in. Money that changes by a cent across a
/// save is a data-loss bug, so the round trip is asserted value by value.
/// </summary>
public sealed class BudgetRepositoryTests : IDisposable
{
    private readonly TempVaultDirectory _directory = new();
    private readonly SqliteBudgetStore _store;
    private readonly BudgetRepository _repository;

    public BudgetRepositoryTests()
    {
        _store = new SqliteBudgetStore(_directory, NullLogger<SqliteBudgetStore>.Instance);
        _store.Open();
        _repository = new BudgetRepository(_store, NullLogger<BudgetRepository>.Instance);
    }

    [Fact]
    public void AnEmptyDatabaseLoadsAnEmptyDocument()
    {
        var document = _repository.Load();

        Assert.True(document.IsEmpty);
        Assert.False(_repository.HasData());
        Assert.Empty(document.Members);
    }

    [Fact]
    public void HouseholdAndPreferencesRoundTrip()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        Assert.Equal("Alex and Sam", loaded.HouseholdName);
        Assert.Equal("$", loaded.Preferences.CurrencySymbol);
        Assert.Equal(new Money(400m), loaded.Preferences.MinimumBalanceReserve);
        Assert.Equal(new Money(250m), loaded.Preferences.MinimumBreathingRoom);
        Assert.Equal(3.5m, loaded.Preferences.EmergencyFundTargetMonths);
        Assert.True(_repository.HasData());
    }

    [Fact]
    public void MembersRoundTripIncludingDependants()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        Assert.Equal(3, loaded.Members.Count);
        Assert.Equal(2, loaded.Members.Count(member => !member.IsDependant));

        var dependant = Assert.Single(loaded.Members, member => member.IsDependant);
        Assert.Equal(new DateOnly(2018, 4, 2), dependant.DateOfBirth);

        var me = loaded.Members.First(member => member.Name == "Me");
        Assert.Equal(new Money(40m), me.PersonalAllowance);
        Assert.Equal(Frequency.Weekly, me.PersonalAllowanceFrequency);
        Assert.True(me.IsDiscretionaryEligible);
        Assert.True(loaded.Members.First(member => member.Name == "Sam").IsDiscretionaryEligible);
        Assert.False(dependant.IsDiscretionaryEligible);
    }

    [Fact]
    public void AllocationGuidanceRoundTrips()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        Assert.Equal(IncomeBasis.Conservative, loaded.Rules.Basis);
        Assert.Equal(15m, loaded.Rules.DiscretionaryPercent);
        Assert.Equal(new Money(50m), loaded.Rules.SafetyBuffer);
        Assert.Equal("Utah", loaded.Locality.State);
        Assert.Equal("Salt Lake County", loaded.Locality.County);
        Assert.Single(loaded.GroceryPlans);
        Assert.Single(loaded.CostGuidance);
        Assert.Single(loaded.Reserves);
        Assert.Single(loaded.Transfers);
        Assert.DoesNotContain(loaded.DiscretionaryEligibleMembers, member => member.IsDependant);
    }

    [Fact]
    public void HourlyIncomeKeepsItsThreeHourEstimates()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        var hourly = Assert.IsType<HourlyIncome>(
            loaded.IncomeSources.Single(source => source.Name == "Warehouse work"));

        Assert.Equal(new Money(18.375m), hourly.HourlyRate);
        Assert.Equal(35m, hourly.WeeklyHours.Conservative);
        Assert.Equal(37.5m, hourly.WeeklyHours.Normal);
        Assert.Equal(40m, hourly.WeeklyHours.Optimistic);
        Assert.Equal(1.5m, hourly.OvertimeMultiplier);
        Assert.Equal(Frequency.Weekly, hourly.PayFrequency);
    }

    [Fact]
    public void EveryIncomeKindSurvivesTheRoundTrip()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        Assert.Contains(loaded.IncomeSources, source => source is HourlyIncome);
        Assert.Contains(loaded.IncomeSources, source => source is SalaryIncome);
        Assert.Contains(loaded.IncomeSources, source => source is VariableIncome);

        var mileage = Assert.IsType<MileageReimbursement>(
            loaded.IncomeSources.Single(source => source is MileageReimbursement));

        Assert.False(mileage.IsTaxable);
        Assert.Equal(new Money(0.67m), mileage.RatePerMile);
    }

    [Fact]
    public void ExpensesKeepTheirSplitRules()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        var rent = loaded.Expenses.Single(expense => expense.Name == "Rent");
        var split = Assert.IsType<SplitRule>(rent.Split);

        Assert.Equal(SplitMethod.Percentage, split.Method);
        Assert.Equal(2, split.Participants.Count);
        Assert.Equal([60m, 40m], split.Percentages);

        // The stored rule must still divide money exactly.
        var shares = split.Divide(new Money(1000m));
        Assert.Equal(new Money(1000m), Money.Sum(shares.Values));
    }

    [Fact]
    public void APausedExpenseStaysPaused()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        var paused = loaded.Expenses.Single(expense => expense.Name == "Streaming service");

        Assert.True(paused.IsPaused);
        Assert.Empty(paused.DueDates(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void BenefitsKeepDeductiblesAndBeneficiaries()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        var medical = loaded.Benefits.Single(plan => plan.Kind == BenefitKind.Medical);

        Assert.Equal(new Money(3200m), medical.Deductible);
        Assert.Equal(new Money(7500m), medical.OutOfPocketMaximum);
        Assert.Equal(DeductionTaxTreatment.PreTaxIncludingFica, medical.TaxTreatment);

        var life = loaded.Benefits.Single(plan => plan.Kind == BenefitKind.Life);
        Assert.Equal(["Sam"], life.Beneficiaries);
    }

    [Fact]
    public void PayrollProfileKeepsTheW4AndDeductions()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        var profile = Assert.Single(loaded.PayrollProfiles);

        Assert.Equal(FilingStatus.MarriedFilingJointly, profile.W4.FilingStatus);
        Assert.Equal(1, profile.W4.QualifyingChildren);
        Assert.True(profile.W4.IsNonResidentAlien);
        Assert.Equal(0.0307m, profile.StateFlatRate);
        Assert.Equal(6m, profile.Retirement.EmployeeContributionPercent);
        Assert.Equal(3, profile.Retirement.VestingYears);

        Assert.Equal(2, profile.Deductions.Count);
        Assert.Equal("Medical premium", profile.Deductions[0].Name);
        Assert.Equal(DeductionTaxTreatment.PreTaxIncludingFica, profile.Deductions[0].TaxTreatment);
    }

    [Fact]
    public void DebtsFundsAndGoalsRoundTrip()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        var card = Assert.Single(loaded.Debts);
        Assert.Equal(22.9m, card.AnnualPercentageRate);
        Assert.Equal(0m, card.PromotionalRate);
        Assert.Equal(new DateOnly(2026, 6, 1), card.PromotionalRateEnds);

        var fund = loaded.Funds.Single(item => item.Purpose == FundPurpose.EmergencyFund);
        Assert.True(fund.IsRevolving);
        Assert.Equal(new Money(6000m), fund.TargetAmount);

        var goal = Assert.Single(loaded.Goals);
        Assert.Equal(GoalPriority.High, goal.Priority);
        Assert.Equal(new DateOnly(2027, 3, 1), goal.TargetDate);
    }

    [Fact]
    public void ScenariosKeepEveryCostLineInOrder()
    {
        var document = BuildDocument();

        _repository.Save(document);
        var loaded = _repository.Load();

        var scenario = Assert.Single(loaded.Scenarios);
        var original = document.Scenarios[0];

        Assert.Equal(original.Lines.Count, scenario.Lines.Count);
        Assert.Equal(
            original.Lines.Select(line => line.Name),
            scenario.Lines.Select(line => line.Name));

        var payment = scenario.Lines.Single(line => line.Name == "Monthly loan payment");
        Assert.Equal(CostLineState.Provided, payment.State);
        Assert.Equal(new Money(235m), payment.Amount);

        var notApplicable = scenario.Lines.Single(line => line.Name == "Parking and permits");
        Assert.Equal(CostLineState.NotApplicable, notApplicable.State);

        // Unanswered lines must stay unanswered, or the affordability verdict would be dishonest.
        Assert.Contains(scenario.Lines, line => line.State == CostLineState.Unanswered);
    }

    [Fact]
    public void SavingTwiceReplacesRatherThanDuplicates()
    {
        var document = BuildDocument();

        _repository.Save(document);
        _repository.Save(document);

        var loaded = _repository.Load();

        Assert.Equal(3, loaded.Members.Count);
        Assert.Equal(document.Expenses.Count, loaded.Expenses.Count);
        Assert.Single(loaded.Scenarios);
    }

    [Fact]
    public void RemovingAMemberAlsoRemovesTheirIncome()
    {
        var document = BuildDocument();
        _repository.Save(document);

        var melId = document.Members.Single(member => member.Name == "Sam").Id;

        var trimmed = document with
        {
            Members = document.Members.Where(member => member.Id != melId).ToList(),
            IncomeSources = document.IncomeSources.Where(source => source.MemberId != melId).ToList(),
            Expenses = document.Expenses
                .Select(expense => expense.Split is null ? expense : expense with { Split = null })
                .ToList(),
            Benefits = document.Benefits.Where(plan => plan.MemberId != melId).ToList(),
            Debts = document.Debts
                .Select(debt => debt.OwnerMemberId == melId ? debt with { OwnerMemberId = null } : debt)
                .ToList(),
            Transfers = document.Transfers
                .Where(transfer => transfer.FromMemberId != melId && transfer.ToMemberId != melId)
                .ToList()
        };

        _repository.Save(trimmed);
        var loaded = _repository.Load();

        Assert.DoesNotContain(loaded.Members, member => member.Id == melId);
        Assert.DoesNotContain(loaded.IncomeSources, source => source.MemberId == melId);
    }

    [Fact]
    public void AnInvalidDocumentIsRejectedBeforeAnythingIsWritten()
    {
        var good = BuildDocument();
        _repository.Save(good);

        // An income source pointing at a member who no longer exists would corrupt every projection.
        var broken = good with
        {
            IncomeSources = good.IncomeSources
                .Append(new SalaryIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Ghost job",
                    MemberId = Guid.NewGuid(),
                    PayFrequency = Frequency.Monthly,
                    AnchorPayDate = new DateOnly(2026, 1, 31),
                    AnnualSalary = new Money(50_000m)
                })
                .ToList()
        };

        Assert.Throws<ArgumentException>(() => _repository.Save(broken));

        // The earlier good data must be untouched.
        var loaded = _repository.Load();
        Assert.Equal(good.IncomeSources.Count, loaded.IncomeSources.Count);
    }

    [Fact]
    public void TwoPayslipsOnTheSameDateForOneSourceAreRejected()
    {
        var document = BuildDocument();
        var sourceId = document.IncomeSources.First(source => source is HourlyIncome).Id;

        var duplicated = document with
        {
            Payslips =
            [
                Payslip(sourceId, new DateOnly(2026, 1, 9)),
                Payslip(sourceId, new DateOnly(2026, 1, 9))
            ]
        };

        var exception = Assert.Throws<ArgumentException>(() => _repository.Save(duplicated));
        Assert.Contains("more than one payslip", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavingIsRefusedWhenTheVaultIsLocked()
    {
        var document = BuildDocument();
        _store.Close();

        Assert.Throws<VaultLockedException>(() => _repository.Save(document));
    }

    [Fact]
    public void TheDatabaseStaysHealthyAfterAFullSave()
    {
        _repository.Save(BuildDocument());

        var report = _store.CheckIntegrity();

        Assert.True(report.IsHealthy);
        Assert.Empty(report.Problems);
    }

    private static Payslip Payslip(Guid sourceId, DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        IncomeSourceId = sourceId,
        PayDate = date,
        GrossPay = new Money(690m),
        FederalWithholding = new Money(52.14m),
        SocialSecurity = new Money(42.78m),
        Medicare = new Money(10.01m),
        HoursWorked = 37.5m
    };

    private static BudgetDocument BuildDocument()
    {
        var meId = Guid.NewGuid();
        var melId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var hourlyId = Guid.NewGuid();

        var scenario = Scenario.FromTemplate(
                CostTemplateLibrary.CarOwnership,
                "Sam's car",
                new DateOnly(2026, 3, 1)) with
            {
                AdvertisedAmount = new Money(235m)
            };

        scenario = scenario
            .WithLine("Monthly loan payment", new Money(235m), Frequency.Monthly)
            .WithLine("Fuel", new Money(140m), Frequency.Monthly)
            .WithLineNotApplicable("Parking and permits");

        return new BudgetDocument
        {
            HouseholdName = "Alex and Sam",
            Preferences = new HouseholdPreferences
            {
                MinimumBalanceReserve = new Money(400m),
                MinimumBreathingRoom = new Money(250m),
                EmergencyFundTargetMonths = 3.5m
            },
            Members =
            [
                new HouseholdMember
                {
                    Id = meId,
                    Name = "Me",
                    PersonalAllowance = new Money(40m),
                    PersonalAllowanceFrequency = Frequency.Weekly,
                    IsDiscretionaryEligible = true
                },
                new HouseholdMember { Id = melId, Name = "Sam", IsDiscretionaryEligible = true },
                new HouseholdMember
                {
                    Id = childId,
                    Name = "Child",
                    IsDependant = true,
                    DateOfBirth = new DateOnly(2018, 4, 2)
                }
            ],
            Accounts =
            [
                new BankAccount
                {
                    Id = Guid.NewGuid(),
                    Name = "Joint checking",
                    CurrentBalance = new Money(1234.56m),
                    IsPrimary = true,
                    UpdatedOn = new DateOnly(2026, 1, 5)
                }
            ],
            IncomeSources =
            [
                new HourlyIncome
                {
                    Id = hourlyId,
                    Name = "Warehouse work",
                    MemberId = meId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = new DateOnly(2026, 1, 2),
                    HourlyRate = new Money(18.375m),
                    WeeklyHours = VariableHours.Standard,
                    WeeklyOvertimeHours = new VariableHours(0m, 2m, 6m)
                },
                new SalaryIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Sam's salary",
                    MemberId = melId,
                    PayFrequency = Frequency.Fortnightly,
                    AnchorPayDate = new DateOnly(2026, 1, 9),
                    AnnualSalary = new Money(31_200m)
                },
                new VariableIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Weekend work",
                    MemberId = meId,
                    PayFrequency = Frequency.Monthly,
                    AnchorPayDate = new DateOnly(2026, 1, 15),
                    AmountPerPeriod = new VariableHours(0m, 120m, 300m)
                },
                new MileageReimbursement
                {
                    Id = Guid.NewGuid(),
                    Name = "Mileage",
                    MemberId = meId,
                    PayFrequency = Frequency.Monthly,
                    AnchorPayDate = new DateOnly(2026, 1, 20),
                    IsTaxable = false,
                    MilesPerPeriod = 220m,
                    RatePerMile = new Money(0.67m)
                }
            ],
            Payslips = [Payslip(hourlyId, new DateOnly(2026, 1, 2))],
            PayrollProfiles =
            [
                new PayrollProfile
                {
                    MemberId = meId,
                    TaxYear = 2025,
                    W4 = new W4Settings
                    {
                        FilingStatus = FilingStatus.MarriedFilingJointly,
                        QualifyingChildren = 1,
                        IsNonResidentAlien = true,
                        ExtraWithholdingPerPeriod = new Money(15m)
                    },
                    Retirement = new RetirementPlan
                    {
                        EmployeeContributionPercent = 6m,
                        EmployerMatchPercent = 3m,
                        EmployerMatchLimitPercent = 6m,
                        VestingYears = 3
                    },
                    Deductions =
                    [
                        new PayrollDeduction
                        {
                            Name = "Medical premium",
                            AmountPerPeriod = new Money(72.31m),
                            TaxTreatment = DeductionTaxTreatment.PreTaxIncludingFica
                        },
                        new PayrollDeduction
                        {
                            Name = "Legal plan",
                            AmountPerPeriod = new Money(4.15m),
                            TaxTreatment = DeductionTaxTreatment.PostTax
                        }
                    ],
                    StateFlatRate = 0.0307m,
                    YearsOfService = 2.5m
                }
            ],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = new Money(1200m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = new DateOnly(2026, 1, 1),
                    Ownership = Ownership.Shared,
                    Split = new SplitRule
                    {
                        Method = SplitMethod.Percentage,
                        Participants = [meId, melId],
                        Percentages = [60m, 40m]
                    },
                    AnnualIncreasePercent = 4m
                },
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Groceries",
                    Category = ExpenseCategory.Groceries,
                    ExpectedAmount = new Money(160m),
                    Frequency = Frequency.Weekly,
                    AnchorDueDate = new DateOnly(2026, 1, 3),
                    Variability = ExpenseVariability.Variable
                },
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Streaming service",
                    Category = ExpenseCategory.Subscriptions,
                    ExpectedAmount = new Money(15.99m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = new DateOnly(2026, 1, 12),
                    Necessity = ExpenseNecessity.Optional,
                    IsPaused = true
                },
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Car registration",
                    Category = ExpenseCategory.Transport,
                    ExpectedAmount = new Money(96m),
                    Frequency = Frequency.Annual,
                    AnchorDueDate = new DateOnly(2026, 8, 14),
                    Notes = "Renewal plus emissions test."
                }
            ],
            Transactions =
            [
                new ExpenseTransaction
                {
                    Id = Guid.NewGuid(),
                    Date = new DateOnly(2026, 1, 3),
                    Description = "Weekly shop",
                    Amount = new Money(171.42m),
                    Category = ExpenseCategory.Groceries
                },
                new ExpenseTransaction
                {
                    Id = Guid.NewGuid(),
                    Date = new DateOnly(2026, 1, 6),
                    Description = "Returned item",
                    Amount = new Money(24.99m),
                    Category = ExpenseCategory.Groceries,
                    IsRefund = true
                }
            ],
            Benefits =
            [
                new BenefitPlan
                {
                    Id = Guid.NewGuid(),
                    Name = "Medical PPO",
                    Kind = BenefitKind.Medical,
                    MemberId = meId,
                    Coverage = CoverageLevel.Family,
                    EmployeePremiumPerPeriod = new Money(72.31m),
                    PremiumFrequency = Frequency.Weekly,
                    EmployerContributionPerPeriod = new Money(180m),
                    Deductible = new Money(3200m),
                    OutOfPocketMaximum = new Money(7500m),
                    EffectiveDate = new DateOnly(2026, 1, 1),
                    RenewalDate = new DateOnly(2026, 12, 31)
                },
                new BenefitPlan
                {
                    Id = Guid.NewGuid(),
                    Name = "Basic life",
                    Kind = BenefitKind.Life,
                    MemberId = meId,
                    EmployeePremiumPerPeriod = new Money(2.4m),
                    PremiumFrequency = Frequency.Weekly,
                    CoverageAmount = new Money(50_000m),
                    Beneficiaries = ["Sam"]
                }
            ],
            Debts =
            [
                new DebtAccount
                {
                    Id = Guid.NewGuid(),
                    Name = "Credit card",
                    Kind = DebtKind.CreditCard,
                    Balance = new Money(2480.17m),
                    AnnualPercentageRate = 22.9m,
                    MinimumPayment = new Money(75m),
                    PromotionalRate = 0m,
                    PromotionalRateEnds = new DateOnly(2026, 6, 1),
                    OwnerMemberId = melId,
                    DueDayOfMonth = 18
                }
            ],
            Funds =
            [
                new SavingsFund
                {
                    Id = Guid.NewGuid(),
                    Name = "Emergency fund",
                    Purpose = FundPurpose.EmergencyFund,
                    CurrentBalance = new Money(1500m),
                    TargetAmount = new Money(6000m),
                    Priority = 10,
                    IsRevolving = true
                },
                new SavingsFund
                {
                    Id = Guid.NewGuid(),
                    Name = "Car repairs",
                    Purpose = FundPurpose.CarRepairs,
                    CurrentBalance = new Money(220m),
                    TargetAmount = new Money(1200m),
                    TargetDate = new DateOnly(2026, 12, 1),
                    PlannedContribution = new Money(25m),
                    ContributionFrequency = Frequency.Weekly,
                    Priority = 7
                }
            ],
            Goals =
            [
                new Goal
                {
                    Id = Guid.NewGuid(),
                    Name = "Car deposit",
                    TargetAmount = new Money(3000m),
                    TargetDate = new DateOnly(2027, 3, 1),
                    CurrentAmount = new Money(450m),
                    Priority = GoalPriority.High,
                    PlannedContribution = new Money(50m),
                    ContributionFrequency = Frequency.Weekly
                }
            ],
            Scenarios = [scenario],
            Rules = new AllocationRules
            {
                Basis = IncomeBasis.Conservative,
                DiscretionaryPercent = 15m,
                SafetyBuffer = new Money(50m)
            },
            Locality = CostLocality.SaltLakeCounty,
            GroceryPlans =
            [
                new GroceryPlan
                {
                    Id = Guid.NewGuid(),
                    Kind = GroceryPlanKind.Current,
                    Name = "Weekly shop",
                    Categories =
                    [
                        new GroceryCategoryPlan
                        {
                            Id = Guid.NewGuid(),
                            Name = "Vegetables",
                            WeeklyLimit = new Money(25m),
                            IsEssential = true
                        }
                    ]
                }
            ],
            CostGuidance =
            [
                new CostGuidanceRecord
                {
                    Id = Guid.NewGuid(),
                    Locality = CostLocality.SaltLakeCounty,
                    Category = "Vegetables",
                    EffectiveDate = new DateOnly(2026, 1, 1),
                    SourceName = "Local shop observation",
                    SourceType = CostGuidanceSourceType.LocallyObservedPrice,
                    Low = new Money(18m),
                    Typical = new Money(25m),
                    Comfortable = new Money(35m),
                    ObservedOn = new DateOnly(2026, 1, 4),
                    ReviewByDate = new DateOnly(2026, 7, 1)
                }
            ],
            Reserves =
            [
                new ObligationReserve
                {
                    Id = Guid.NewGuid(),
                    ObligationId = Guid.NewGuid(),
                    Kind = ObligationKind.Expense,
                    Reserved = new Money(90m),
                    UpdatedOn = new DateOnly(2026, 1, 5)
                }
            ],
            Transfers =
            [
                new PersonalTransfer
                {
                    Id = Guid.NewGuid(),
                    FromMemberId = meId,
                    ToMemberId = melId,
                    Amount = new Money(10m),
                    Date = new DateOnly(2026, 1, 5),
                    Purpose = "One-time help"
                }
            ]
        };
    }

    public void Dispose()
    {
        _store.Dispose();
        _directory.Dispose();
    }
}
