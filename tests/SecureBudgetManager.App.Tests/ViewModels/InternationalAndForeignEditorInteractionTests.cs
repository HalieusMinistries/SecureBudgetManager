using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class InternationalAndForeignEditorInteractionTests
{
    [Fact]
    public void TransferAddOpensABlankEditor()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new InternationalTransfersViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddTransferCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.True(vm.IsTransferEditor);
        Assert.Equal("Add transfer", vm.EditorTitle);
        Assert.Equal(string.Empty, vm.Sender);
        Assert.Equal(string.Empty, vm.AmountSent);
        Assert.Equal(string.Empty, vm.ExchangeRate);
        Assert.False(vm.HasEditorChanges);
    }

    [Fact]
    public async Task TransferClickSelectsWithoutOpening()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new InternationalTransfersViewModel(session, new FakeUserDialog(), TimeProvider.System);
        vm.BeginAddTransferCommand.Execute(null);
        vm.Sender = "Adult";
        vm.Recipient = "Relative";
        vm.AmountSent = "100";
        vm.AmountReceived = "1820";
        vm.ExchangeRate = "18.2";
        await vm.SaveTransferCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Transfers);
        vm.SelectTransferCommand.Execute(row);

        Assert.False(vm.IsEditorOpen);
        Assert.True(Assert.Single(vm.Transfers).IsSelected);
        Assert.Equal(row.Id, vm.SelectedRecordId);
    }

    [Fact]
    public void TransferCancelLeavesTheDocumentUnchanged()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new InternationalTransfersViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddTransferCommand.Execute(null);
        vm.Sender = "Adult";
        vm.Recipient = "Relative";
        Assert.True(vm.HasEditorChanges);

        vm.CancelEditorCommand.Execute(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Empty(session.Document.InternationalTransfers);
    }

    [Fact]
    public void TransferUnsavedChangesAreProtected()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = false };
        var vm = new InternationalTransfersViewModel(session, dialog, TimeProvider.System);

        vm.BeginAddTransferCommand.Execute(null);
        vm.Sender = "Adult";

        Assert.False(vm.TryLeaveEditor());
        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Unsaved changes", dialog.LastTitle);
    }

    [Fact]
    public async Task TransferSaveValidatesPersistsAndRestoresSelection()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new InternationalTransfersViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddTransferCommand.Execute(null);
        await vm.SaveTransferCommand.ExecuteAsync(null);
        Assert.True(vm.IsEditorOpen);
        Assert.Contains("sender, recipient, amounts", vm.EditorError, StringComparison.Ordinal);
        Assert.Empty(session.Document.InternationalTransfers);

        vm.Sender = "Adult";
        vm.Recipient = "Relative";
        vm.AmountSent = "100";
        vm.AmountReceived = "1820";
        vm.ExchangeRate = "18.2";
        vm.Provider = "Remittance desk";
        vm.ProviderFee = "4";
        vm.Purpose = TransferPurpose.FamilySupport;
        vm.TransferDate = new DateTime(2026, 8, 1);
        vm.TransferFrequency = Frequency.OneOff;
        await vm.SaveTransferCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditorOpen);
        var saved = Assert.Single(session.Document.InternationalTransfers);
        Assert.Equal("Adult", saved.Sender);
        Assert.Equal("Relative", saved.Recipient);
        Assert.Equal(18.2m, saved.ExchangeRate);
        Assert.Equal(new Money(4m), saved.ProviderFee);
        Assert.Equal(Frequency.OneOff, saved.Frequency);
        Assert.True(Assert.Single(vm.Transfers).IsSelected);
    }

    [Fact]
    public async Task TransferEditOpensTheSelectedRecord()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new InternationalTransfersViewModel(session, new FakeUserDialog(), TimeProvider.System);
        vm.BeginAddTransferCommand.Execute(null);
        vm.Sender = "Adult";
        vm.Recipient = "Relative";
        vm.AmountSent = "100";
        vm.AmountReceived = "1820";
        vm.ExchangeRate = "18.2";
        await vm.SaveTransferCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Transfers);
        vm.BeginEditTransferCommand.Execute(row);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Edit transfer", vm.EditorTitle);
        Assert.Equal("Adult", vm.Sender);
        Assert.Equal("18.2", vm.ExchangeRate);

        vm.Recipient = "Household relative";
        await vm.SaveTransferCommand.ExecuteAsync(null);

        Assert.Equal("Household relative", Assert.Single(session.Document.InternationalTransfers).Recipient);
        Assert.Equal(18.2m, Assert.Single(session.Document.InternationalTransfers).OriginalExchangeRate);
        Assert.True(Assert.Single(vm.Transfers).IsSelected);
    }

    [Fact]
    public async Task CommitmentAddSaveAndCancelFollowTheOverlayContract()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new InternationalTransfersViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommitmentCommand.Execute(null);
        Assert.True(vm.IsCommitmentEditor);
        Assert.Equal("Add support commitment", vm.EditorTitle);
        Assert.Equal(string.Empty, vm.ExpectedRate);

        vm.CommitmentName = "Monthly support";
        vm.CancelEditorCommand.Execute(null);
        Assert.Empty(session.Document.SupportCommitments);

        vm.BeginAddCommitmentCommand.Execute(null);
        vm.CommitmentName = "Monthly support";
        vm.FixedUsd = "150";
        vm.ExpectedRate = "18";
        await vm.SaveCommitmentCommand.ExecuteAsync(null);

        var saved = Assert.Single(session.Document.SupportCommitments);
        Assert.Equal("Monthly support", saved.Name);
        Assert.Equal(18m, saved.ExpectedRateUsdToZar);
        Assert.True(Assert.Single(vm.Commitments).IsSelected);
    }

    [Fact]
    public void ForeignAccountAddOpensABlankEditor()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ForeignAccountsViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Add foreign account", vm.EditorTitle);
        Assert.Equal(string.Empty, vm.Institution);
        Assert.Equal(string.Empty, vm.Nickname);
        Assert.Equal(string.Empty, vm.YearEndBalance);
        Assert.Equal(string.Empty, vm.ReportingRate);
        Assert.False(vm.HasEditorChanges);
    }

    [Fact]
    public async Task ForeignAccountClickSelectsWithoutOpening()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ForeignAccountsViewModel(session, new FakeUserDialog(), TimeProvider.System);
        vm.BeginAddCommand.Execute(null);
        vm.Institution = "SA Bank";
        vm.Nickname = "Current-ZA";
        await vm.SaveAccountCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Accounts);
        vm.SelectAccountCommand.Execute(row);

        Assert.False(vm.IsEditorOpen);
        Assert.True(Assert.Single(vm.Accounts).IsSelected);
    }

    [Fact]
    public void ForeignAccountCancelAndUnsavedPromptLeaveStoredDataAlone()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = false };
        var vm = new ForeignAccountsViewModel(session, dialog, TimeProvider.System)
        {
            ListScrollOffset = 96
        };

        vm.BeginAddCommand.Execute(null);
        vm.Institution = "SA Bank";
        vm.Nickname = "Current-ZA";
        Assert.False(vm.TryLeaveEditor());
        Assert.True(vm.IsEditorOpen);
        Assert.Equal(96, vm.ListScrollOffset);

        dialog.NextResult = true;
        vm.CancelEditorCommand.Execute(null);
        Assert.False(vm.IsEditorOpen);
        Assert.Empty(session.Document.ForeignAccounts);
        Assert.Equal(96, vm.ListScrollOffset);
    }

    [Fact]
    public async Task ForeignAccountSaveValidatesPersistsAndRestoresSelection()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ForeignAccountsViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);
        await vm.SaveAccountCommand.ExecuteAsync(null);
        Assert.True(vm.IsEditorOpen);
        Assert.Contains("institution and a nickname", vm.EditorError, StringComparison.Ordinal);
        Assert.Empty(session.Document.ForeignAccounts);

        vm.Institution = "SA Bank";
        vm.Nickname = "Current-ZA";
        vm.Country = "South Africa";
        vm.Currency = "ZAR";
        vm.YearEndBalance = "8000";
        await vm.SaveAccountCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditorOpen);
        var saved = Assert.Single(session.Document.ForeignAccounts);
        Assert.Equal("Current-ZA", saved.Nickname);
        Assert.Equal(new Money(8000m), saved.YearEndBalance);
        Assert.Null(saved.ReportingExchangeRate);
        Assert.True(Assert.Single(vm.Accounts).IsSelected);
    }

    [Fact]
    public async Task ForeignAccountEditOpensTheSelectedRecord()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ForeignAccountsViewModel(session, new FakeUserDialog(), TimeProvider.System);
        vm.BeginAddCommand.Execute(null);
        vm.Institution = "SA Bank";
        vm.Nickname = "Current-ZA";
        vm.YearEndBalance = "8000";
        await vm.SaveAccountCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Accounts);
        vm.BeginEditCommand.Execute(row);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Edit foreign account", vm.EditorTitle);
        Assert.Equal("Current-ZA", vm.Nickname);
        Assert.Equal("8000.00", vm.YearEndBalance);

        vm.IsArchived = true;
        await vm.SaveAccountCommand.ExecuteAsync(null);

        Assert.NotNull(Assert.Single(session.Document.ForeignAccounts).ClosedOn);
        Assert.Equal("Archived", Assert.Single(vm.Accounts).Review);
    }

    [Fact]
    public void BothPagesImplementTheSharedEditorContract()
    {
        var (session, _, _) = SessionFactory.Open();
        IEditablePage transfers = new InternationalTransfersViewModel(session, new FakeUserDialog(), TimeProvider.System);
        IEditablePage accounts = new ForeignAccountsViewModel(session, new FakeUserDialog(), TimeProvider.System);

        Assert.NotNull(transfers.SaveEditorCommand);
        Assert.NotNull(transfers.CancelEditorCommand);
        Assert.NotNull(accounts.SaveEditorCommand);
        Assert.NotNull(accounts.CancelEditorCommand);
    }
}
