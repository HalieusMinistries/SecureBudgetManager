using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Layout;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class BillsAndAssistanceInteractionTests
{
    [Fact]
    public void PayerChoicesUseHouseholdNamesAndNeverHardCodedAdults()
    {
        var (session, vm, alex, sam, _) = OpenBills();

        Assert.Contains(vm.PayerChoices, choice => choice.Name == "Unassigned");
        Assert.Contains(vm.PayerChoices, choice => choice.Name == "Alex pays all" && choice.MemberId == alex);
        Assert.Contains(vm.PayerChoices, choice => choice.Name == "Sam pays all" && choice.MemberId == sam);
        Assert.Contains(vm.PayerChoices, choice => choice.Name == "Shared household");
        Assert.Contains(vm.PayerChoices, choice => choice.Name == "Shared account");
        Assert.DoesNotContain(vm.PayerChoices, choice => choice.Name.Contains("Matthew", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.PayerChoices, choice => choice.Name.Contains("Mel", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Alex's share", vm.FirstAdultShareLabel);
        Assert.Equal("Sam's share", vm.SecondAdultShareLabel);
        Assert.NotNull(session.Document);
    }

    [Fact]
    public async Task EveryAssignmentMethodCanBeSaved()
    {
        var (session, vm, alex, sam, row) = OpenBills();
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);

        await SaveAssignment(vm, row, "unassigned", null, "50", "50");
        AssertAssignment(session, BillAssignment.Unassigned, null);
        Assert.Empty(BillAssignmentPlanner.Shares(session.Document.Expenses.Single(), new Money(1200m)));

        await SaveAssignment(vm, row, $"member:{alex}", BillAssignment.MemberPaysAll, "50", "50");
        AssertAssignment(session, BillAssignment.MemberPaysAll, alex);
        Assert.Equal(new Money(1200m), BillAssignmentPlanner.Shares(session.Document.Expenses.Single(), new Money(1200m))[alex]);

        await SaveAssignment(vm, row, $"member:{sam}", BillAssignment.MemberPaysAll, "50", "50");
        AssertAssignment(session, BillAssignment.MemberPaysAll, sam);

        await SaveAssignment(vm, row, "shared", BillAssignment.PercentageSplit, "60", "40");
        AssertAssignment(session, BillAssignment.PercentageSplit, alex);
        Assert.Equal(60m, session.Document.Expenses.Single().Split!.Percentages[0]);

        await SaveAssignment(vm, row, "shared", BillAssignment.FixedDollarSplit, "700", "500");
        AssertAssignment(session, BillAssignment.FixedDollarSplit, alex);
        Assert.Equal(new Money(700m), session.Document.Expenses.Single().Split!.FixedAmounts[0]);

        await SaveAssignment(vm, row, "shared", BillAssignment.EnteredContributions, "400", "800");
        AssertAssignment(session, BillAssignment.EnteredContributions, alex);

        await SaveAssignment(vm, row, "account", BillAssignment.SharedAccount, "50", "50");
        AssertAssignment(session, BillAssignment.SharedAccount, null);
        Assert.Empty(BillAssignmentPlanner.Shares(session.Document.Expenses.Single(), new Money(1200m)));

        var after = PaychequeAllocator.Allocate(session.Document, today);
        Assert.NotNull(after);
    }

    [Fact]
    public void SharedSplitValidationRejectsIncompleteAndNegativeValues()
    {
        var (_, vm, _, _, row) = OpenBills();
        vm.OpenBillCommand.Execute(row);
        vm.SelectedPayerKey = "shared";

        vm.SharedSplitMethod = BillAssignment.PercentageSplit;
        vm.EditorFirstShare = "60";
        vm.EditorSecondShare = "30";
        Assert.False(vm.TryBuildAssignment(out _, out _, out var percentError));
        Assert.Contains("exactly 100%", percentError, StringComparison.Ordinal);

        vm.EditorFirstShare = "-10";
        vm.EditorSecondShare = "110";
        Assert.False(vm.TryBuildAssignment(out _, out _, out var negativePercent));
        Assert.Contains("negative", negativePercent, StringComparison.OrdinalIgnoreCase);

        vm.SharedSplitMethod = BillAssignment.FixedDollarSplit;
        vm.EditorFirstShare = "700";
        vm.EditorSecondShare = "400";
        Assert.False(vm.TryBuildAssignment(out _, out _, out var dollarError));
        Assert.Contains("equal the assigned bill amount", dollarError, StringComparison.Ordinal);

        vm.EditorFirstShare = "-5";
        vm.EditorSecondShare = "1205";
        Assert.False(vm.TryBuildAssignment(out _, out _, out var negativeDollar));
        Assert.Contains("negative", negativeDollar, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreviewingAnAssignmentDoesNotPersist()
    {
        var (session, repository, vm, _, _, row) = OpenBillsWithRepository();
        var saves = repository.SaveCount;

        vm.OpenBillCommand.Execute(row);
        vm.SelectedPayerKey = "shared";
        vm.SharedSplitMethod = BillAssignment.PercentageSplit;
        vm.EditorFirstShare = "70";
        vm.EditorSecondShare = "30";
        vm.PreviewAssignmentCommand.Execute(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.AssignmentPreview));
        Assert.Contains("Alex", vm.AssignmentPreview, StringComparison.Ordinal);
        Assert.Contains("Sam", vm.AssignmentPreview, StringComparison.Ordinal);
        Assert.Equal(BillAssignment.Unassigned, session.Document.Expenses.Single().Assignment);
        Assert.Equal(saves, repository.SaveCount);
        Assert.True(vm.IsEditorOpen);
    }

    [Fact]
    public async Task SavingAndCancellingABillAssignmentLeaveTheExpectedDocument()
    {
        var (session, vm, alex, _, row) = OpenBills();

        vm.OpenBillCommand.Execute(row);
        vm.SelectedPayerKey = $"member:{alex}";
        Assert.True(vm.HasEditorChanges);
        vm.CancelEditorCommand.Execute(null);
        Assert.False(vm.IsEditorOpen);
        Assert.Equal(BillAssignment.Unassigned, session.Document.Expenses.Single().Assignment);

        await SaveAssignment(vm, row, $"member:{alex}", BillAssignment.MemberPaysAll, "50", "50");
        Assert.Equal(BillAssignment.MemberPaysAll, session.Document.Expenses.Single().Assignment);
        Assert.False(vm.IsEditorOpen);
    }

    [Fact]
    public async Task PaymentAndReservationAreRecordedFromTheBillEditor()
    {
        var (session, vm, _, _, row) = OpenBills();
        vm.OpenBillCommand.Execute(row);

        vm.PaymentAmount = "125";
        await vm.RecordPaymentCommand.ExecuteAsync(null);
        Assert.False(vm.IsEditorOpen);
        Assert.Equal(new Money(125m), Assert.Single(session.Document.Transactions).Amount);
        Assert.Equal("Rent payment", session.Document.Transactions.Single().Description);

        vm.OpenBillCommand.Execute(row);
        vm.ReserveAmount = "80";
        await vm.SaveReserveCommand.ExecuteAsync(null);
        Assert.False(vm.IsEditorOpen);
        Assert.Equal(new Money(80m), Assert.Single(session.Document.Reserves).Reserved);
        Assert.Equal(row.ObligationId, session.Document.Reserves.Single().ObligationId);
    }

    [Fact]
    public async Task PersonalTransferUsesTheOverlayAndShowsBothPeople()
    {
        var (session, vm, alex, sam, _) = OpenBills();

        vm.BeginAddTransferCommand.Execute(null);
        Assert.True(vm.IsTransferEditor);
        Assert.Equal("Add transfer", vm.EditorTitle);
        Assert.False(vm.HasEditorChanges);

        vm.TransferFrom = vm.Members.First(member => member.Id == alex);
        vm.TransferTo = vm.Members.First(member => member.Id == sam);
        vm.TransferAmount = "40";
        vm.TransferDate = new DateTime(2026, 9, 6);
        vm.TransferPurpose = "Share of fuel";
        vm.TransferIsRecurring = false;
        Assert.True(vm.HasEditorChanges);

        vm.PreviewTransferCommand.Execute(null);
        Assert.Contains("Alex", vm.TransferPreview, StringComparison.Ordinal);
        Assert.Contains("Sam", vm.TransferPreview, StringComparison.Ordinal);
        Assert.Empty(session.Document.Transfers);

        vm.CancelEditorCommand.Execute(null);
        Assert.Empty(session.Document.Transfers);

        vm.BeginAddTransferCommand.Execute(null);
        vm.TransferFrom = vm.Members.First(member => member.Id == alex);
        vm.TransferTo = vm.Members.First(member => member.Id == sam);
        vm.TransferAmount = "40";
        vm.TransferPurpose = "Share of fuel";
        await vm.AddTransferCommand.ExecuteAsync(null);

        var transfer = Assert.Single(session.Document.Transfers);
        Assert.Equal(alex, transfer.FromMemberId);
        Assert.Equal(sam, transfer.ToMemberId);
        Assert.Equal(new Money(40m), transfer.Amount);
        Assert.Equal("Share of fuel", transfer.Purpose);
        Assert.False(transfer.IsRecurring);
        Assert.False(vm.IsEditorOpen);
        Assert.True(Assert.Single(vm.Transfers).IsSelected);
    }

    [Fact]
    public void UnassignedBillsAreNotDeductedFromEitherPerson()
    {
        var (session, vm, alex, sam, row) = OpenBills();
        vm.OpenBillCommand.Execute(row);
        vm.SelectedPayerKey = "unassigned";
        Assert.True(vm.TryBuildAssignment(out var assignment, out var split, out _));
        Assert.Equal(BillAssignment.Unassigned, assignment);
        Assert.Null(split);

        var shares = BillAssignmentPlanner.Shares(session.Document.Expenses.Single(), new Money(1200m));
        Assert.Empty(shares);
        Assert.False(shares.ContainsKey(alex));
        Assert.False(shares.ContainsKey(sam));
    }

    [Fact]
    public async Task GroceryAssistanceAddEditSaveAndCancelKeepUnknownValuesUnknown()
    {
        var (session, vm, fruitId) = OpenGrocery();

        vm.BeginAddAssistanceCommand.Execute(null);
        Assert.True(vm.IsAssistanceEditor);
        Assert.Equal("Add assistance", vm.EditorTitle);
        Assert.Equal(string.Empty, vm.AssistanceValue);
        Assert.False(vm.HasEditorChanges);

        vm.AssistanceSource = "Local pantry";
        vm.SelectedAssistanceStatus = "expected";
        vm.AssistanceEffective = new DateTime(2026, 9, 1);
        vm.AssistanceNotes = "Confirm again next month";
        vm.ToggleAssistanceCategoryCommand.Execute(fruitId);
        Assert.True(vm.HasEditorChanges);

        vm.CancelEditorCommand.Execute(null);
        Assert.False(Assert.Single(session.Document.GroceryPlans, plan => plan.Kind == GroceryPlanKind.Current)
            .Assistance.IsExpected);
        Assert.Null(session.Document.GroceryPlanOf(GroceryPlanKind.Current)!.Assistance.SourceName);

        vm.BeginAddAssistanceCommand.Execute(null);
        vm.AssistanceSource = "Local pantry";
        vm.SelectedAssistanceStatus = "expected";
        vm.AssistanceEffective = new DateTime(2026, 9, 1);
        vm.AssistanceNotes = "Confirm again next month";
        vm.ToggleAssistanceCategoryCommand.Execute(fruitId);
        await vm.SaveAssistanceCommand.ExecuteAsync(null);

        var saved = session.Document.GroceryPlanOf(GroceryPlanKind.Current)!.Assistance;
        Assert.True(saved.IsExpected);
        Assert.False(saved.IsSuspended);
        Assert.Equal("Local pantry", saved.SourceName);
        Assert.True(saved.EstimatedWeeklyValue.IsZero);
        Assert.Equal("Fruit", Assert.Single(saved.CategoriesSupplied));
        Assert.True(session.Document.GroceryPlanOf(GroceryPlanKind.Current)!.Categories
            .Single(category => category.Id == fruitId).SuppliedByAssistance);
        Assert.False(vm.IsEditorOpen);
        Assert.True(vm.HasAssistanceRecorded);

        vm.BeginEditAssistanceCommand.Execute(null);
        Assert.Equal("Edit assistance", vm.EditorTitle);
        Assert.Equal(string.Empty, vm.AssistanceValue);
        Assert.Equal("Local pantry", vm.AssistanceSource);
    }

    [Fact]
    public async Task GroceryAssistanceSavesUnknownOptionalFieldsAndCloses()
    {
        var (session, vm, fruitId) = OpenGrocery();

        vm.BeginAddAssistanceCommand.Execute(null);
        vm.AssistanceSource = "Food Banks";
        vm.SelectedAssistanceStatus = "expected";
        vm.AssistanceEffective = null;
        vm.AssistanceReview = null;
        vm.AssistanceValue = string.Empty;
        vm.ToggleAssistanceCategoryCommand.Execute(fruitId);
        await vm.SaveAssistanceCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Equal("Assistance saved", vm.StatusMessage);
        var saved = session.Document.GroceryPlanOf(GroceryPlanKind.Current)!.Assistance;
        Assert.True(saved.IsExpected);
        Assert.Equal("Food Banks", saved.SourceName);
        Assert.True(saved.EstimatedWeeklyValue.IsZero);
        Assert.Null(saved.EffectiveDate);
        Assert.Null(saved.ReviewDate);
        Assert.Equal("Fruit", Assert.Single(saved.CategoriesSupplied));
        Assert.DoesNotContain("cancelled", vm.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GroceryPlanTreatmentsStayComputedAndAssistanceIsNotPermanent()
    {
        var (_, vm, _) = OpenGrocery();

        Assert.Contains("cash a week", vm.PlanSummary, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(vm.FallbackSummary));
        Assert.Equal("No emergency minimum plan has been created.", vm.EmergencySummary);

        vm.BeginAddAssistanceCommand.Execute(null);
        Assert.Contains("not assumed to be permanent", vm.AssistanceNotPermanentNote, StringComparison.Ordinal);
        Assert.Contains("cash still required", vm.CashStillRequired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Weekly", vm.AssistanceFrequencyText, StringComparison.Ordinal);
        Assert.Contains("assistance", vm.FallbackSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectionFocusAndScrollAreRestoredForBillsAndAssistance()
    {
        var (_, bills, _, _, row) = OpenBills();
        bills.ListScrollOffset = 72;
        bills.SelectBillCommand.Execute(row);
        Assert.False(bills.IsEditorOpen);
        Assert.True(Assert.Single(bills.Reservations).IsSelected);
        Assert.Equal(row.ObligationId, bills.SelectedRecordId);

        bills.OpenBillCommand.Execute(row);
        bills.CancelEditorCommand.Execute(null);
        Assert.Equal(72, bills.ListScrollOffset);
        Assert.True(Assert.Single(bills.Reservations).IsSelected);

        var (_, grocery, fruitId) = OpenGrocery();
        grocery.ListScrollOffset = 36;
        var category = Assert.Single(grocery.Categories, item => item.Id == fruitId);
        grocery.SelectCategoryCommand.Execute(category);
        Assert.False(grocery.IsEditorOpen);
        Assert.True(Assert.Single(grocery.Categories, item => item.Id == fruitId).IsSelected);

        grocery.SelectAssistanceCommand.Execute(null);
        Assert.True(grocery.IsAssistanceSelected);
        Assert.True(grocery.IsSelected);
        Assert.False(Assert.Single(grocery.Categories, item => item.Id == fruitId).IsSelected);

        grocery.BeginAddAssistanceCommand.Execute(null);
        grocery.CancelEditorCommand.Execute(null);
        Assert.Equal(36, grocery.ListScrollOffset);
    }

    [Fact]
    public void KeyboardCommandsAndPrivacyClearTheVisibleEditors()
    {
        var (session, bills, _, _, row) = OpenBills();
        IEditablePage billPage = bills;
        Assert.NotNull(billPage.SaveEditorCommand);
        Assert.NotNull(billPage.CancelEditorCommand);

        bills.OpenBillCommand.Execute(row);
        bills.SelectedPayerKey = "account";
        Assert.True(bills.HasEditorChanges);
        var dialog = new FakeUserDialog { NextResult = false };
        var protectedVm = new AllocationsViewModel(session, dialog, TimeProvider.System);
        protectedVm.OpenBillCommand.Execute(Assert.Single(protectedVm.Reservations));
        protectedVm.SelectedPayerKey = "account";
        Assert.False(protectedVm.TryLeaveEditor());
        Assert.True(protectedVm.IsEditorOpen);
        Assert.Equal("Unsaved changes", dialog.LastTitle);

        billPage.CancelEditorCommand.Execute(null);
        Assert.False(bills.IsEditorOpen);

        bills.OpenBillCommand.Execute(row);
        session.Clear();
        Assert.False(bills.IsEditorOpen);
        Assert.Empty(bills.Reservations);

        var (grocerySession, grocery, _) = OpenGrocery();
        IEditablePage groceryPage = grocery;
        Assert.NotNull(groceryPage.SaveEditorCommand);
        Assert.NotNull(groceryPage.CancelEditorCommand);
        grocery.BeginAddAssistanceCommand.Execute(null);
        grocery.AssistanceSource = "Pantry";
        groceryPage.CancelEditorCommand.Execute(null);
        Assert.False(grocery.IsEditorOpen);

        grocery.BeginAddAssistanceCommand.Execute(null);
        grocerySession.Clear();
        Assert.False(grocery.IsEditorOpen);
        Assert.Empty(grocery.Categories);
    }

    [Fact]
    public void EditorsFitThe1366By720WorkingArea()
    {
        var bounds = EditorOverlayCalculator.FitToWorkArea(1366, 720);
        Assert.True(bounds.Width <= 1366);
        Assert.True(bounds.Height <= 720);
        Assert.True(EditorOverlayCalculator.SaveAndCancelRemainReachable(bounds.Height));
        Assert.Equal(720, bounds.Width);
        Assert.Equal(640, bounds.Height);
    }

    [Fact]
    public void DuplicateEditorsArePreventedWhileUnsavedChangesRemain()
    {
        var dialog = new FakeUserDialog { NextResult = false };
        var (session, _, _, _, _) = OpenBills();
        var vm = new AllocationsViewModel(session, dialog, TimeProvider.System);
        var row = Assert.Single(vm.Reservations);

        vm.OpenBillCommand.Execute(row);
        vm.SelectedPayerKey = "account";
        vm.BeginAddTransferCommand.Execute(null);

        Assert.True(vm.IsBillEditor);
        Assert.False(vm.IsTransferEditor);
        Assert.Equal(1, dialog.ConfirmCount);
    }

    private static async Task SaveAssignment(
        AllocationsViewModel vm,
        ReservationRow row,
        string payerKey,
        BillAssignment? sharedMethod,
        string first,
        string second)
    {
        vm.OpenBillCommand.Execute(row);
        if (sharedMethod is { } method
            && payerKey == "shared")
        {
            vm.SharedSplitMethod = method;
        }

        vm.SelectedPayerKey = payerKey;
        vm.EditorFirstShare = first;
        vm.EditorSecondShare = second;
        await vm.SaveAssignmentCommand.ExecuteAsync(null);
        Assert.False(vm.IsEditorOpen);
    }

    private static void AssertAssignment(IBudgetSession session, BillAssignment assignment, Guid? firstParticipant)
    {
        var expense = Assert.Single(session.Document.Expenses);
        Assert.Equal(assignment, expense.Assignment);
        if (firstParticipant is { } id)
        {
            Assert.Equal(id, expense.Split!.Participants[0]);
            return;
        }

        Assert.True(expense.Split is null || expense.Split.Participants.Count == 0);
    }

    private static (IBudgetSession Session, AllocationsViewModel Vm, Guid Alex, Guid Sam, ReservationRow Row)
        OpenBills()
    {
        var (session, vm, alex, sam, row) = OpenBillsCore(new FakeUserDialog());
        return (session, vm, alex, sam, row);
    }

    private static (IBudgetSession Session, FakeBudgetRepository Repository, AllocationsViewModel Vm, Guid Alex, Guid Sam, ReservationRow Row)
        OpenBillsWithRepository()
    {
        var (session, repository, vm, alex, sam, row) = OpenBillsCoreWithRepository(new FakeUserDialog());
        return (session, repository, vm, alex, sam, row);
    }

    private static (IBudgetSession Session, AllocationsViewModel Vm, Guid Alex, Guid Sam, ReservationRow Row)
        OpenBillsCore(FakeUserDialog dialog)
    {
        var (session, _, vm, alex, sam, row) = OpenBillsCoreWithRepository(dialog);
        return (session, vm, alex, sam, row);
    }

    private static (IBudgetSession Session, FakeBudgetRepository Repository, AllocationsViewModel Vm, Guid Alex, Guid Sam, ReservationRow Row)
        OpenBillsCoreWithRepository(FakeUserDialog dialog)
    {
        var (session, repository, _) = SessionFactory.Open();
        var alex = Guid.NewGuid();
        var sam = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members =
            [
                new HouseholdMember { Id = alex, Name = "Alex" },
                new HouseholdMember { Id = sam, Name = "Sam" }
            ],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = expenseId,
                    Name = "Rent",
                    ExpectedAmount = new Money(1200m),
                    Frequency = Frequency.Monthly,
                    Category = ExpenseCategory.Housing,
                    Necessity = ExpenseNecessity.Essential,
                    AnchorDueDate = new DateOnly(2026, 9, 1),
                    Assignment = BillAssignment.Unassigned
                }
            ]
        }, out _));

        var vm = new AllocationsViewModel(session, dialog, TimeProvider.System);
        var row = Assert.Single(vm.Reservations, item => item.Name == "Rent");
        return (session, repository, vm, alex, sam, row);
    }

    private static (IBudgetSession Session, GroceryPlanViewModel Vm, Guid FruitId) OpenGrocery()
    {
        var (session, _, _) = SessionFactory.Open();
        var fruitId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            GroceryPlans =
            [
                new GroceryPlan
                {
                    Id = Guid.NewGuid(),
                    Kind = GroceryPlanKind.Current,
                    Name = "Current plan",
                    Categories =
                    [
                        new GroceryCategoryPlan
                        {
                            Id = fruitId,
                            Name = "Fruit",
                            IsEssential = true,
                            WeeklyLimit = new Money(20m)
                        },
                        new GroceryCategoryPlan
                        {
                            Id = Guid.NewGuid(),
                            Name = "Snacks",
                            IsEssential = false,
                            WeeklyLimit = new Money(5m)
                        }
                    ]
                },
                new GroceryPlan
                {
                    Id = Guid.NewGuid(),
                    Kind = GroceryPlanKind.FallbackWithoutAssistance,
                    Name = "Fallback plan without assistance",
                    Categories =
                    [
                        new GroceryCategoryPlan
                        {
                            Id = Guid.NewGuid(),
                            Name = "Fruit",
                            IsEssential = true,
                            WeeklyLimit = new Money(28m)
                        }
                    ]
                }
            ]
        }, out _));

        return (session, new GroceryPlanViewModel(session, new FakeUserDialog(), TimeProvider.System), fruitId);
    }
}
