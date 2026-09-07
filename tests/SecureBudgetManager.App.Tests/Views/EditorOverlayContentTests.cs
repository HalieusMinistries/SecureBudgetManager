using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.Tests.Capture;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.App.Views;
using SecureBudgetManager.App.Views.Editors;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.Views;

[Collection("StaWpf")]
public sealed class EditorOverlayContentTests
{
    [Fact]
    public void ExpenseIsExpenseEditorBecomesTrueWhenEditOpens()
    {
        var (vm, item, _) = ReadyExpense();
        var seen = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExpensesViewModel.IsExpenseEditor))
            {
                seen = true;
            }
        };

        Assert.False(vm.IsExpenseEditor);
        Assert.False(vm.IsEditorContentReady);
        Assert.False(vm.SaveEntryCommand.CanExecute(null));

        vm.BeginEditCommand.Execute(item);

        Assert.True(seen);
        Assert.True(vm.IsExpenseEditor);
        Assert.True(vm.IsEditorContentReady);
        Assert.Equal("Rent", vm.EditorName);
        Assert.Equal("1450.00", vm.EditorAmount);
        Assert.True(vm.SaveEntryCommand.CanExecute(null));
    }

    [Fact]
    public void ExpenseSaveStaysDisabledWhenTheEditorIsNotReady()
    {
        var (vm, _, _) = ReadyExpense();

        Assert.False(vm.IsEditorContentReady);
        Assert.False(vm.SaveEntryCommand.CanExecute(null));
    }

    [Fact]
    public void EditExpenseRendersThePopulatedFormInTheOverlay()
    {
        Sta.Run(() =>
        {
            EnsureApplication();
            var (vm, item, _) = ReadyExpense();
            using var host = OverlayOf(vm);
            Assert.IsType<ExpenseEditorForm>(host.Overlay.FindEditorForm());
            Assert.False(host.Overlay.RefreshFormReadiness(), "Fields must stay hidden until Edit opens.");

            vm.BeginEditCommand.Execute(item);
            host.Window.UpdateLayout();

            Assert.True(host.Overlay.RefreshFormReadiness());
            var form = Assert.IsType<ExpenseEditorForm>(host.Overlay.FindEditorForm());
            Assert.True(form.ActualHeight > 1);
            Assert.True(form.ActualWidth > 1);
            Assert.Equal("Rent", Text(form, "Expense name"));
            Assert.Equal("1450.00", Text(form, "Expense amount"));
            Assert.NotNull(Find<ComboBox>(form, "Expense owner"));
            Assert.NotNull(Find<ComboBox>(form, "Expense category"));
            Assert.NotNull(Find<ComboBox>(form, "Expense necessity"));
            Assert.NotNull(Find<ComboBox>(form, "Expense frequency"));
            Assert.NotNull(Find<DatePicker>(form, "Expense due date"));
            Assert.NotNull(Find<DatePicker>(form, "Expense end date"));
            Assert.NotNull(Find<ComboBox>(form, "Expense variability"));
            Assert.NotNull(Find<ComboBox>(form, "Expense coverage"));
            Assert.NotNull(Find<CheckBox>(form, "Expense is active"));
        });
    }

    [Fact]
    public void AddExpenseRendersBlankDefaults()
    {
        Sta.Run(() =>
        {
            EnsureApplication();
            var (vm, _, _) = ReadyExpense();
            using var host = OverlayOf(vm);
            vm.BeginAddCommand.Execute(null);
            host.Window.UpdateLayout();

            Assert.True(host.Overlay.RefreshFormReadiness());
            var form = Assert.IsType<ExpenseEditorForm>(host.Overlay.FindEditorForm());
            Assert.Equal(string.Empty, Text(form, "Expense name"));
            Assert.Equal(string.Empty, Text(form, "Expense amount"));
            Assert.True(vm.IsExpenseEditor);
        });
    }

    [Fact]
    public void ExpenseCancelClosesWithoutChangingTheRecord()
    {
        var (vm, item, session) = ReadyExpense();
        vm.BeginEditCommand.Execute(item);
        vm.EditorName = "Changed";
        vm.CancelEditorCommand.Execute(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Equal("Rent", Assert.Single(vm.Items).Name);
        Assert.Equal("Rent", Assert.Single(session.Document.Expenses).Name);
    }

    [Fact]
    public void EveryEditorFormRendersWithNonZeroBounds()
    {
        Sta.Run(() =>
        {
            EnsureApplication();
            foreach (var (page, open, expected) in AllEditors())
            {
                using var host = OverlayOf(page);
                open();
                host.Window.UpdateLayout();
                Assert.True(
                    host.Overlay.RefreshFormReadiness(),
                    $"{expected.Name} form was not ready after opening.");
                var form = host.Overlay.FindEditorForm();
                Assert.NotNull(form);
                Assert.IsType(expected, form);
                Assert.True(form.ActualHeight > 1, $"{expected.Name} height was {form.ActualHeight}.");
                Assert.True(form.ActualWidth > 1, $"{expected.Name} width was {form.ActualWidth}.");
                Assert.True(((IEditablePage)page).IsEditorContentReady);
                ((IEditablePage)page).CancelEditorCommand.Execute(null);
            }
        });
    }

    [Fact]
    public void TemplateSelectorNeverReturnsAPageView()
    {
        var selector = new EditorFormTemplateSelector
        {
            ExpenseTemplate = new DataTemplate()
        };

        Assert.Same(selector.ExpenseTemplate, selector.SelectTemplate(ReadyExpense().Vm, null!));
        Assert.Null(selector.SelectTemplate(new object(), null!));
    }

    private static IEnumerable<(object Page, Action Open, Type FormType)> AllEditors()
    {
        var dialog = new FakeUserDialog();
        var clock = TimeProvider.System;
        var memberId = Guid.NewGuid();
        var fruitId = Guid.NewGuid();
        var (session, _, _) = SessionFactory.Open();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = new Money(1450m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = new DateOnly(2026, 10, 1)
                }
            ],
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
                        }
                    ]
                }
            ]
        }, out _));

        var household = new HouseholdViewModel(session, dialog);
        yield return (household, () => household.BeginAddCommand.Execute(null), typeof(HouseholdEditorForm));

        var income = new IncomeViewModel(session, dialog);
        yield return (income, () => income.BeginAddCommand.Execute(null), typeof(IncomeEditorForm));

        var expenses = new ExpensesViewModel(session, dialog);
        yield return (expenses, () => expenses.BeginAddCommand.Execute(null), typeof(ExpenseEditorForm));

        var payroll = new PayrollViewModel(session, dialog);
        yield return (payroll, () =>
        {
            payroll.SelectedMemberId = memberId;
            payroll.BeginAddBenefitCommand.Execute(null);
        }, typeof(BenefitEditorForm));

        var bills = new AllocationsViewModel(session, dialog, clock);
        yield return (bills, () => bills.BeginAddTransferCommand.Execute(null), typeof(BillEditorForm));
        yield return (bills, () => bills.OpenBillCommand.Execute(bills.Reservations[0]), typeof(BillEditorForm));

        var grocery = new GroceryPlanViewModel(session, dialog, clock);
        yield return (grocery, () => grocery.BeginAddAssistanceCommand.Execute(null), typeof(GroceryCategoryEditorForm));
        yield return (grocery, () => grocery.OpenCategoryCommand.Execute(grocery.Categories[0]), typeof(GroceryCategoryEditorForm));

        var savings = new SavingsViewModel(session, dialog, clock);
        yield return (savings, () => savings.BeginAddFundCommand.Execute(null), typeof(SavingsEditorForm));

        var debt = new DebtViewModel(session, dialog, clock);
        yield return (debt, () => debt.BeginAddCommand.Execute(null), typeof(DebtEditorForm));

        var actuals = new ActualsViewModel(session, dialog, clock);
        yield return (actuals, () => actuals.BeginAddTransactionCommand.Execute(null), typeof(ActualsEditorForm));

        var products = new ProductsViewModel(session, dialog, clock);
        yield return (products, () => products.BeginAddCommand.Execute(null), typeof(ProductEditorForm));

        var planning = new PlanningViewModel(session, dialog, clock);
        yield return (planning, () => planning.BeginAddCommand.Execute(null), typeof(PlanningEditorForm));

        var guidance = new LocalGuidanceViewModel(session, dialog, clock);
        yield return (guidance, () => guidance.BeginAddCommand.Execute(null), typeof(GuidanceEditorForm));

        var transfers = new InternationalTransfersViewModel(session, dialog, clock);
        yield return (transfers, () => transfers.BeginAddTransferCommand.Execute(null), typeof(InternationalTransferEditorForm));

        var foreign = new ForeignAccountsViewModel(session, dialog, clock);
        yield return (foreign, () => foreign.BeginAddCommand.Execute(null), typeof(ForeignAccountEditorForm));
    }

    private static (ExpensesViewModel Vm, ExpenseListItem Item, IBudgetSession Session) ReadyExpense()
    {
        var (session, _, _) = SessionFactory.Open();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex" }],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = new Money(1450m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = new DateOnly(2026, 10, 1)
                }
            ]
        }, out _));

        var vm = new ExpensesViewModel(session, new FakeUserDialog());
        var item = Assert.Single(vm.Items);
        return (vm, item, session);
    }

    private static OverlayWindow OverlayOf(object page)
    {
        var overlay = new EditorOverlay
        {
            DataContext = new OverlayHost
            {
                CurrentViewModel = page,
                OverlayWidth = 560,
                OverlayHeight = 520
            }
        };

        var window = new Window
        {
            Content = overlay,
            Width = 800,
            Height = 700,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20000,
            Top = -20000
        };
        window.Show();
        window.UpdateLayout();
        return new OverlayWindow(window, overlay);
    }

    private static void EnsureApplication()
    {
        try
        {
            Application.ResourceAssembly ??= typeof(EditorOverlay).Assembly;
        }
        catch (InvalidOperationException)
        {
        }

        if (Application.Current is not null)
        {
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var name = typeof(EditorOverlay).Assembly.GetName().Name;
        foreach (var dictionary in new[]
                 {
                     "Themes/Colors.xaml",
                     "Themes/Controls.xaml",
                     "Themes/DatePicker.xaml",
                     "Themes/SecurityControls.xaml"
                 })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/{name};component/{dictionary}", UriKind.Absolute)
            });
        }

        var factory = new FrameworkElementFactory(typeof(ExpensesView));
        app.Resources.Add(
            new DataTemplateKey(typeof(ExpensesViewModel)),
            new DataTemplate(typeof(ExpensesViewModel)) { VisualTree = factory });
    }

    private static string Text(DependencyObject root, string name) =>
        Find<TextBox>(root, name)?.Text ?? string.Empty;

    private static T? Find<T>(DependencyObject root, string name) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && AutomationProperties.GetName(match) == name)
            {
                return match;
            }

            var nested = Find<T>(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed class OverlayHost
    {
        public required object CurrentViewModel { get; init; }

        public double OverlayWidth { get; init; }

        public double OverlayHeight { get; init; }
    }

    private sealed class OverlayWindow : IDisposable
    {
        public OverlayWindow(Window window, EditorOverlay overlay)
        {
            Window = window;
            Overlay = overlay;
        }

        public Window Window { get; }

        public EditorOverlay Overlay { get; }

        public void Dispose() => Window.Close();
    }
}
