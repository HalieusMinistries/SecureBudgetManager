using SecureBudgetManager.Core.Expenses;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// The named spending categories the guidance system reasons about, beyond the coarse
/// <see cref="ExpenseCategory"/> set used for ordinary expense recording.
///
/// These are plain names rather than an enum so a household can add its own and still have it
/// placed in the hierarchy. The constants exist so the defaults and the tests refer to the same
/// spelling.
/// </summary>
public static class GuidanceCategories
{
    // Survival
    public const string EssentialFood = "Essential food";
    public const string Water = "Water";
    public const string EssentialMedicine = "Essential medicine";

    // Safety
    public const string EssentialTransport = "Essential transport";
    public const string HouseholdSecurity = "Household security";

    // Stability
    public const string SinkingFunds = "Sinking funds";
    public const string EmergencyReserve = "Emergency reserve";
    public const string WorkCommunication = "Work communication and internet";
    public const string VehicleMaintenance = "Vehicle maintenance";

    // Family and belonging
    public const string SharedFamilyEntertainment = "Shared family entertainment";
    public const string ChurchAndCommunity = "Church, community and social activities";
    public const string Hospitality = "Hospitality";

    // Growth
    public const string Education = "Education";
    public const string Retirement = "Retirement";
    public const string PlannedPurchases = "Planned purchases";
    public const string HolidaysAndTravel = "Holidays and travel";

    // Discretionary freedom
    public const string Hobbies = "Hobbies";
    public const string DiningOut = "Dining out and takeaway meals";
    public const string StreamingAndSubscriptions = "Streaming and subscriptions";
    public const string Alcohol = "Alcohol";
    public const string Tobacco = "Cigarettes or tobacco";
    public const string ClothingBeyondNecessity = "Clothing beyond immediate necessity";
    public const string NonEssentialShopping = "Non-essential shopping";
    public const string GiftsOutsidePlannedFunds = "Gifts outside planned gift funds";

    /// <summary>
    /// The personal spending category for one household member. Personal categories are named
    /// after the member so one person's spending can never be charged to another's allowance.
    /// </summary>
    public static string PersonalSpendingFor(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        return $"Personal spending — {memberName.Trim()}";
    }

    /// <summary>
    /// The shared and personal categories offered by default on the allocation screens. Personal
    /// categories are not included here because they depend on who is eligible.
    /// </summary>
    public static IReadOnlyList<string> SharedDiscretionary { get; } =
    [
        SharedFamilyEntertainment,
        DiningOut,
        StreamingAndSubscriptions,
        Hobbies,
        Alcohol,
        Tobacco,
        ClothingBeyondNecessity,
        NonEssentialShopping,
        ChurchAndCommunity,
        GiftsOutsidePlannedFunds
    ];

    /// <summary>Every category the default hierarchy has an opinion about, for the rules screen.</summary>
    public static IReadOnlyList<string> KnownCategories { get; } =
    [
        ExpenseCategory.Housing.Name,
        EssentialFood,
        Water,
        EssentialMedicine,
        ExpenseCategory.Healthcare.Name,
        ExpenseCategory.Utilities.Name,
        ExpenseCategory.Insurance.Name,
        EssentialTransport,
        ExpenseCategory.Transport.Name,
        ExpenseCategory.DebtPayments.Name,
        HouseholdSecurity,
        SinkingFunds,
        EmergencyReserve,
        WorkCommunication,
        VehicleMaintenance,
        ExpenseCategory.Childcare.Name,
        SharedFamilyEntertainment,
        ChurchAndCommunity,
        Hospitality,
        ExpenseCategory.Savings.Name,
        Education,
        Retirement,
        PlannedPurchases,
        HolidaysAndTravel,
        Hobbies,
        DiningOut,
        StreamingAndSubscriptions,
        ExpenseCategory.Subscriptions.Name,
        Alcohol,
        Tobacco,
        ClothingBeyondNecessity,
        NonEssentialShopping,
        GiftsOutsidePlannedFunds,
        ExpenseCategory.Personal.Name
    ];
}
