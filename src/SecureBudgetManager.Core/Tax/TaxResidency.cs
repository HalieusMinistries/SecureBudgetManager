namespace SecureBudgetManager.Core.Tax;

/// <summary>
/// United States tax residency is recorded explicitly. It is never inferred from nationality,
/// mailing address, W-4 selection, immigration status or an incomplete physical-presence count.
/// </summary>
public enum UsTaxResidency
{
    UnitedStatesCitizen = 0,
    ResidentAlien = 1,
    NonResidentAlien = 2,
    DualStatusYear = 3,
    NotYetDetermined = 4,
    ProfessionalReviewRequired = 5
}

public static class UsTaxResidencyExtensions
{
    public static string ToDisplayName(this UsTaxResidency status) => status switch
    {
        UsTaxResidency.UnitedStatesCitizen => "United States citizen",
        UsTaxResidency.ResidentAlien => "Resident alien for tax purposes",
        UsTaxResidency.NonResidentAlien => "Non-resident alien for tax purposes",
        UsTaxResidency.DualStatusYear => "Dual-status tax year",
        UsTaxResidency.NotYetDetermined => "Status not yet determined",
        UsTaxResidency.ProfessionalReviewRequired => "Professional review required",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown tax-residency status.")
    };

    public static bool IsUncertain(this UsTaxResidency status) =>
        status is UsTaxResidency.NotYetDetermined
            or UsTaxResidency.ProfessionalReviewRequired
            or UsTaxResidency.DualStatusYear;
}
