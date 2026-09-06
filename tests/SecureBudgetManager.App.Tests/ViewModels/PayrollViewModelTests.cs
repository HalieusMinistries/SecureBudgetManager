using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Tax;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class PayrollViewModelTests
{
    [Fact]
    public async Task RetirementAndDeductionsAndBenefitsPersistSeparately()
    {
        var (session, repository, _) = SessionFactory.Open();
        var memberId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }]
        }, out _));

        var vm = new PayrollViewModel(session, new FakeUserDialog { NextResult = true });
        vm.SelectedMemberId = memberId;
        vm.EmployeeContributionPercent = "6";
        vm.EmployerMatchPercent = "4";
        vm.EmployerMatchLimitPercent = "6";
        await vm.SaveRetirementCommand.ExecuteAsync(null);

        vm.DeductionName = "Union dues";
        vm.DeductionAmount = "12.50";
        vm.DeductionTreatment = DeductionTaxTreatment.PostTax;
        await vm.AddDeductionCommand.ExecuteAsync(null);

        vm.BeginAddBenefitCommand.Execute(null);
        vm.BenefitName = "Medical";
        vm.BenefitKind = BenefitKind.Medical;
        vm.BenefitPremium = "45";
        await vm.SaveBenefitCommand.ExecuteAsync(null);

        var profile = Assert.Single(repository.Stored.PayrollProfiles);
        Assert.Equal(6m, profile.Retirement.EmployeeContributionPercent);
        Assert.Equal(4m, profile.Retirement.EmployerMatchPercent);
        Assert.Equal("Union dues", Assert.Single(profile.Deductions).Name);
        Assert.Equal(DeductionTaxTreatment.PostTax, profile.Deductions[0].TaxTreatment);
        Assert.Equal("Medical", Assert.Single(repository.Stored.Benefits).Name);
        Assert.Contains("not take-home", vm.EmployerMatchSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeductionValidationRejectsAnEmptyName()
    {
        var (session, _, _) = SessionFactory.Open();
        var memberId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }]
        }, out _));

        var vm = new PayrollViewModel(session, new FakeUserDialog());
        vm.SelectedMemberId = memberId;
        vm.DeductionName = "";
        vm.DeductionAmount = "10";

        Assert.Equal("A deduction needs a name.", vm.ValidateDeduction());
    }
}
