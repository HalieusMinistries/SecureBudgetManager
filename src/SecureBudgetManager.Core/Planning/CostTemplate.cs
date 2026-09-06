using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Planning;

public enum ScenarioKind
{
    BuyCar = 0,
    MoveHome = 1,
    RentHome = 2,
    BuyHome = 3,
    AdoptPet = 4,
    ChangeMedicalPlan = 5,
    LowerPayingJob = 6,
    ReduceHours = 7,
    HaveChild = 8,
    Holiday = 9,
    FurnitureOrElectronics = 10,
    StartSubscription = 11,
    PayOffDebtEarly = 12,
    IncreaseRetirementContributions = 13,
    MedicalEmergency = 14,
    CollegeOrTraining = 15,
    MobilePhone = 16,
    Appliance = 17,
    MedicalProcedure = 18,
    NewJobOrCommute = 19,
    SelfEmployment = 20,
    Loan = 21,
    BalanceTransfer = 22,
    Custom = 99
}

/// <summary>
/// A reusable checklist of costs people forget. Templates supply the questions and typical values;
/// the household supplies the figures, because the right numbers depend on where they live.
/// </summary>
public sealed record CostTemplate
{
    public required ScenarioKind Kind { get; init; }

    public required string Name { get; init; }

    public required string Purpose { get; init; }

    public required IReadOnlyList<CostLine> Lines { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A template needs a name.");
        }

        if (Lines.Count == 0)
        {
            throw new ArgumentException("A template needs at least one cost line.");
        }

        foreach (var line in Lines)
        {
            line.Validate();
        }
    }
}

public static class CostTemplateLibrary
{
    /// <summary>
    /// Car ownership: the fullest template, because a car combines financing, insurance, fuel,
    /// maintenance, registration, depreciation and irregular repairs in one decision.
    /// </summary>
    public static CostTemplate CarOwnership { get; } = new()
    {
        Kind = ScenarioKind.BuyCar,
        Name = "Car ownership",
        Purpose = "Find the true monthly cost of a car, not just the advertised payment.",
        Lines =
        [
            Line("Deposit", "Cash down payment", "How much cash are you putting down?", Frequency.OneOff, critical: true),
            Line("Deposit", "Lost savings interest", "What would that cash have earned if left in savings?", Frequency.Annual, nonCash: true),
            Line("Purchase", "Sales tax", "What is the sales tax on the purchase price?", Frequency.OneOff, critical: true),
            Line("Purchase", "Title and registration", "What are the initial title and registration fees?", Frequency.OneOff),
            Line("Purchase", "Dealer and document fees", "What documentation or dealer fees apply?", Frequency.OneOff),
            Line("Loan", "Monthly loan payment", "What is the monthly payment?", Frequency.Monthly, critical: true),
            Line("Loan", "Total interest", "How much interest will you pay over the whole term?", Frequency.OneOff, nonCash: true),
            Line("Insurance", "Premium increase", "How much will your insurance premium rise?", Frequency.Monthly, critical: true),
            Line("Insurance", "Deductible reserve", "What deductible would you have to find after an accident?", Frequency.Annual),
            Line("Fuel", "Fuel", "What will fuel cost, based on your mileage and the car's economy?", Frequency.Monthly, critical: true),
            Line("Maintenance", "Routine servicing", "What will oil, tyres, brakes and servicing cost each year?", Frequency.Annual, critical: true),
            Line("Repairs", "Repair reserve", "What will you set aside for unexpected repairs? Used cars need more.", Frequency.Monthly, critical: true),
            Line("Registration", "Annual renewal", "What is the annual registration renewal?", Frequency.Annual),
            Line("Registration", "Inspection and emissions", "What do inspection or emissions tests cost?", Frequency.Annual),
            Line("Parking", "Parking and permits", "What will parking at home, at work, permits or meters cost?", Frequency.Monthly),
            Line("Road costs", "Tolls, washes, roadside assistance", "What will tolls, car washes and roadside cover cost?", Frequency.Monthly),
            Line("Depreciation", "Loss of resale value", "How much value will the car lose each year?", Frequency.Annual, nonCash: true),
            Line("Negative equity", "Amount owed above the car's value", "Will you owe more than the car is worth?", Frequency.OneOff, nonCash: true),
            Line("Financing extras", "GAP, warranties and add-ons", "What do GAP insurance, warranties or add-ons cost?", Frequency.Monthly),
            Line("Replacement", "Replacement sinking fund", "What will you save each month towards the next car?", Frequency.Monthly),
            Line("Opportunity cost", "Savings or debt repayment forgone", "What savings or extra debt repayment will this replace?", Frequency.Monthly, nonCash: true),
            Line("Household change", "Savings from giving up another vehicle", "Will another vehicle become unnecessary? Enter the saving.", Frequency.Monthly)
        ]
    };

    public static CostTemplate HomeRental { get; } = new()
    {
        Kind = ScenarioKind.RentHome,
        Name = "Home rental",
        Purpose = "Find the true cost of renting, beyond the advertised rent.",
        Lines =
        [
            Line("Deposit", "Security deposit", "What deposit is required up front?", Frequency.OneOff, critical: true),
            Line("Deposit", "First and last month", "Is any rent required in advance?", Frequency.OneOff),
            Line("Rent", "Monthly rent", "What is the monthly rent?", Frequency.Monthly, critical: true),
            Line("Fees", "Application and admin fees", "What application or administration fees apply?", Frequency.OneOff),
            Line("Fees", "Pet rent or deposit", "Is there a pet deposit or monthly pet rent?", Frequency.Monthly),
            Line("Utilities", "Electricity and gas", "What will electricity and gas cost?", Frequency.Monthly, critical: true),
            Line("Utilities", "Water, sewer and rubbish", "What will water, sewer and rubbish cost?", Frequency.Monthly),
            Line("Utilities", "Internet", "What will internet cost?", Frequency.Monthly),
            Line("Insurance", "Renters insurance", "What will renters insurance cost?", Frequency.Monthly),
            Line("Moving", "Moving costs", "What will the move itself cost?", Frequency.OneOff),
            Line("Commute", "Change in commuting cost", "Will your commute cost more or less?", Frequency.Monthly),
            Line("Parking", "Parking", "Is parking extra?", Frequency.Monthly),
            Line("Increases", "Expected annual rent increase", "How much do you expect rent to rise each year?", Frequency.Annual)
        ]
    };

    public static CostTemplate PetOwnership { get; } = new()
    {
        Kind = ScenarioKind.AdoptPet,
        Name = "Pet ownership",
        Purpose = "Find the true cost of a pet across its whole life, not just the adoption fee.",
        Lines =
        [
            Line("Adoption", "Adoption or purchase fee", "What is the adoption or purchase fee?", Frequency.OneOff, critical: true),
            Line("Setup", "Bed, crate, bowls and carrier", "What equipment do you need to buy?", Frequency.OneOff),
            Line("Health", "Initial vaccinations and neutering", "What will the first round of veterinary care cost?", Frequency.OneOff, critical: true),
            Line("Food", "Food", "What will food cost?", Frequency.Monthly, critical: true),
            Line("Health", "Routine veterinary care", "What will annual check-ups and treatments cost?", Frequency.Annual, critical: true),
            Line("Health", "Pet insurance", "What will pet insurance cost?", Frequency.Monthly),
            Line("Health", "Emergency veterinary reserve", "What will you set aside for an emergency? Bills often exceed $1,000.", Frequency.Monthly, critical: true),
            Line("Care", "Grooming", "What will grooming cost?", Frequency.Monthly),
            Line("Care", "Boarding or sitting", "What will care cost when you travel?", Frequency.Annual),
            Line("Housing", "Pet deposit or pet rent", "Will your housing cost rise?", Frequency.Monthly),
            Line("Other", "Toys, treats and replacements", "What will toys, treats and replacements cost?", Frequency.Monthly)
        ]
    };

    public static CostTemplate NewChild { get; } = new()
    {
        Kind = ScenarioKind.HaveChild,
        Name = "New child",
        Purpose = "Find the cost of a new child, including the income change many people overlook.",
        Lines =
        [
            Line("Birth", "Medical costs after insurance", "What will you pay after insurance, up to your out-of-pocket maximum?", Frequency.OneOff, critical: true),
            Line("Setup", "Cot, pram, car seat and clothing", "What equipment do you need before the birth?", Frequency.OneOff, critical: true),
            Line("Care", "Childcare", "What will childcare cost? This is often the largest single item.", Frequency.Monthly, critical: true),
            Line("Supplies", "Nappies, formula and supplies", "What will ongoing supplies cost?", Frequency.Monthly, critical: true),
            Line("Health", "Added dependant premium", "How much will adding a dependant raise your health premium?", Frequency.Monthly, critical: true),
            Line("Income", "Lost income during leave", "How much income will be lost to unpaid leave?", Frequency.OneOff, critical: true),
            Line("Income", "Permanent change in hours", "Will anyone reduce hours permanently? Enter the monthly loss.", Frequency.Monthly),
            Line("Housing", "Larger home or utilities", "Will housing or utilities cost more?", Frequency.Monthly),
            Line("Transport", "Larger vehicle", "Will you need a different vehicle?", Frequency.OneOff),
            Line("Future", "Education saving", "What will you set aside for education?", Frequency.Monthly)
        ]
    };

    public static CostTemplate Moving { get; } = new()
    {
        Kind = ScenarioKind.MoveHome,
        Name = "Moving",
        Purpose = "Capture the one-off costs of a move, which rarely appear in a monthly budget.",
        Lines =
        [
            Line("Movers", "Removal firm or van hire", "What will movers or a hire van cost?", Frequency.OneOff, critical: true),
            Line("Deposits", "New deposits", "What deposits are needed at the new home?", Frequency.OneOff, critical: true),
            Line("Deposits", "Deposit not returned", "How much of your current deposit might be withheld?", Frequency.OneOff),
            Line("Utilities", "Connection and transfer fees", "What connection or transfer fees apply?", Frequency.OneOff),
            Line("Overlap", "Overlapping rent or mortgage", "Will you pay for two homes at once?", Frequency.OneOff),
            Line("Setup", "Furniture and household items", "What will you need to buy for the new home?", Frequency.OneOff),
            Line("Admin", "Address changes and cleaning", "What will cleaning, redirection and admin cost?", Frequency.OneOff),
            Line("Time", "Time off work", "Will you lose pay taking time off to move?", Frequency.OneOff)
        ]
    };

    public static CostTemplate Travel { get; } = new()
    {
        Kind = ScenarioKind.Holiday,
        Name = "Travel",
        Purpose = "Find the full cost of a trip, including the spending that happens on the ground.",
        Lines =
        [
            Line("Transport", "Flights or fuel", "What will getting there cost?", Frequency.OneOff, critical: true),
            Line("Accommodation", "Accommodation", "What will accommodation cost?", Frequency.OneOff, critical: true),
            Line("Local", "Food and drink", "What will you spend on food while away?", Frequency.OneOff, critical: true),
            Line("Local", "Local transport and car hire", "What will local transport cost?", Frequency.OneOff),
            Line("Local", "Activities and tickets", "What will activities cost?", Frequency.OneOff),
            Line("Admin", "Travel insurance", "What will travel insurance cost?", Frequency.OneOff),
            Line("Admin", "Passports and visas", "Do you need passports or visas?", Frequency.OneOff),
            Line("Home", "Pet or house sitting", "What will care at home cost while you are away?", Frequency.OneOff),
            Line("Income", "Unpaid leave", "Will any of the time off be unpaid?", Frequency.OneOff, critical: true),
            Line("Other", "Bills that continue at home", "Remember bills continue while you are away.", Frequency.OneOff)
        ]
    };

    public static CostTemplate Subscription { get; } = new()
    {
        Kind = ScenarioKind.StartSubscription,
        Name = "Subscription",
        Purpose = "Show the annual cost of a small monthly charge, and what happens after the introductory rate.",
        Lines =
        [
            Line("Cost", "Monthly charge", "What is the monthly charge?", Frequency.Monthly, critical: true),
            Line("Cost", "Price after the introductory period", "What will it cost once any introductory rate ends?", Frequency.Monthly, critical: true),
            Line("Cost", "Setup or hardware cost", "Is there any hardware or setup fee?", Frequency.OneOff),
            Line("Other", "Subscriptions this replaces", "Will you cancel anything else? Enter the monthly saving.", Frequency.Monthly)
        ]
    };

    public static CostTemplate MobilePhone { get; } = new()
    {
        Kind = ScenarioKind.MobilePhone,
        Name = "Mobile phone",
        Purpose = "Separate the handset cost from the airtime cost, which contracts deliberately blur.",
        Lines =
        [
            Line("Handset", "Up-front handset cost", "What do you pay for the handset today?", Frequency.OneOff, critical: true),
            Line("Handset", "Handset instalments", "What are the monthly handset instalments?", Frequency.Monthly, critical: true),
            Line("Airtime", "Monthly plan", "What is the monthly airtime plan?", Frequency.Monthly, critical: true),
            Line("Airtime", "Taxes and fees", "What taxes and regulatory fees are added?", Frequency.Monthly),
            Line("Extras", "Insurance or protection plan", "What does handset insurance cost?", Frequency.Monthly),
            Line("Exit", "Early termination fee", "What would leaving early cost?", Frequency.OneOff)
        ]
    };

    public static CostTemplate Appliance { get; } = new()
    {
        Kind = ScenarioKind.Appliance,
        Name = "Appliance",
        Purpose = "Include delivery, installation and running costs, not just the ticket price.",
        Lines =
        [
            Line("Purchase", "Purchase price", "What is the purchase price?", Frequency.OneOff, critical: true),
            Line("Purchase", "Sales tax", "What sales tax applies?", Frequency.OneOff),
            Line("Delivery", "Delivery and installation", "What will delivery and installation cost?", Frequency.OneOff),
            Line("Delivery", "Removal of the old appliance", "Is there a disposal or removal charge?", Frequency.OneOff),
            Line("Finance", "Monthly finance payment", "If financed, what is the monthly payment?", Frequency.Monthly),
            Line("Running", "Change in electricity or water", "Will running costs rise or fall?", Frequency.Monthly),
            Line("Cover", "Extended warranty", "What does an extended warranty cost?", Frequency.OneOff)
        ]
    };

    public static CostTemplate MedicalProcedure { get; } = new()
    {
        Kind = ScenarioKind.MedicalProcedure,
        Name = "Medical procedure",
        Purpose = "Show total exposure under your plan, including the costs billed separately.",
        Lines =
        [
            Line("Treatment", "Remaining deductible", "How much of your deductible is still unmet?", Frequency.OneOff, critical: true),
            Line("Treatment", "Coinsurance", "What percentage share will you owe after the deductible?", Frequency.OneOff, critical: true),
            Line("Treatment", "Out-of-pocket maximum", "What is the most you could pay this year?", Frequency.OneOff, critical: true),
            Line("Treatment", "Out-of-network charges", "Could any provider be out of network?", Frequency.OneOff),
            Line("Follow-up", "Prescriptions", "What will prescriptions cost?", Frequency.Monthly),
            Line("Follow-up", "Physiotherapy or follow-up visits", "What will follow-up care cost?", Frequency.OneOff),
            Line("Income", "Time off work", "How much pay will you lose during recovery?", Frequency.OneOff, critical: true),
            Line("Other", "Travel and childcare", "What will travel and childcare during treatment cost?", Frequency.OneOff)
        ]
    };

    public static CostTemplate NewJobOrCommute { get; } = new()
    {
        Kind = ScenarioKind.NewJobOrCommute,
        Name = "New job or commute",
        Purpose = "Compare a new job on take-home pay and benefits, not headline salary.",
        Lines =
        [
            Line("Pay", "Change in take-home pay", "How will monthly take-home pay change?", Frequency.Monthly, critical: true),
            Line("Benefits", "Change in health premium", "How will your health premium change?", Frequency.Monthly, critical: true),
            Line("Benefits", "Change in employer retirement match", "How will the employer match change?", Frequency.Monthly, critical: true),
            Line("Commute", "Fuel or fares", "How will commuting costs change?", Frequency.Monthly, critical: true),
            Line("Commute", "Parking", "How will parking costs change?", Frequency.Monthly),
            Line("Commute", "Extra vehicle wear", "Will extra mileage raise maintenance costs?", Frequency.Monthly),
            Line("Setup", "Clothing, tools or certification", "What will you need to buy to start?", Frequency.OneOff),
            Line("Risk", "Gap between jobs", "Will there be unpaid time between roles?", Frequency.OneOff, critical: true),
            Line("Other", "Change in childcare", "Will childcare needs change with new hours?", Frequency.Monthly)
        ]
    };

    public static CostTemplate Loan { get; } = new()
    {
        Kind = ScenarioKind.Loan,
        Name = "Loan",
        Purpose = "Show what a loan costs in total, not just per month.",
        Lines =
        [
            Line("Loan", "Amount borrowed", "How much are you borrowing?", Frequency.OneOff, critical: true),
            Line("Loan", "Monthly payment", "What is the monthly payment?", Frequency.Monthly, critical: true),
            Line("Loan", "Total interest over the term", "How much interest will you pay in total?", Frequency.OneOff, nonCash: true),
            Line("Fees", "Arrangement or origination fee", "What fees are charged to set it up?", Frequency.OneOff),
            Line("Fees", "Early repayment charge", "What would repaying early cost?", Frequency.OneOff),
            Line("Cover", "Payment protection insurance", "Is any insurance required or added?", Frequency.Monthly)
        ]
    };

    public static CostTemplate BalanceTransfer { get; } = new()
    {
        Kind = ScenarioKind.BalanceTransfer,
        Name = "Credit card balance transfer",
        Purpose = "Check the transfer fee and what happens when the promotional rate ends.",
        Lines =
        [
            Line("Transfer", "Balance being transferred", "How much are you transferring?", Frequency.OneOff, critical: true),
            Line("Transfer", "Transfer fee", "What is the transfer fee, usually 3 to 5 percent?", Frequency.OneOff, critical: true),
            Line("Promotion", "Payment needed to clear it in time", "What monthly payment clears the balance before the promotion ends?", Frequency.Monthly, critical: true),
            Line("Promotion", "Interest if not cleared in time", "What interest applies once the promotional rate ends?", Frequency.OneOff, critical: true),
            Line("Risk", "Cost of new spending on the card", "Will new purchases lose the promotional rate?", Frequency.Monthly)
        ]
    };

    public static CostTemplate CollegeOrTraining { get; } = new()
    {
        Kind = ScenarioKind.CollegeOrTraining,
        Name = "College or training",
        Purpose = "Include lost earnings alongside tuition, which is usually the larger cost.",
        Lines =
        [
            Line("Tuition", "Tuition and fees", "What will tuition and fees cost?", Frequency.Annual, critical: true),
            Line("Materials", "Books, equipment and software", "What will materials cost?", Frequency.Annual),
            Line("Travel", "Travel to classes", "What will travel cost?", Frequency.Monthly),
            Line("Income", "Lost earnings while studying", "How much income will you give up?", Frequency.Monthly, critical: true),
            Line("Care", "Childcare during classes", "Will you need childcare?", Frequency.Monthly),
            Line("Finance", "Student loan repayments", "What repayments will start afterwards?", Frequency.Monthly, critical: true)
        ]
    };

    public static CostTemplate HomePurchase { get; } = new()
    {
        Kind = ScenarioKind.BuyHome,
        Name = "Home purchase",
        Purpose = "Capture closing costs and ongoing ownership costs a mortgage quote leaves out.",
        Lines =
        [
            Line("Deposit", "Down payment", "How much are you putting down?", Frequency.OneOff, critical: true),
            Line("Closing", "Closing costs", "What are the closing costs, often 2 to 5 percent?", Frequency.OneOff, critical: true),
            Line("Loan", "Monthly mortgage payment", "What is the monthly principal and interest?", Frequency.Monthly, critical: true),
            Line("Loan", "Total interest over the term", "How much interest will you pay in total?", Frequency.OneOff, nonCash: true),
            Line("Tax", "Property tax", "What is the annual property tax?", Frequency.Annual, critical: true),
            Line("Insurance", "Home insurance", "What will home insurance cost?", Frequency.Annual, critical: true),
            Line("Insurance", "Mortgage insurance", "Is private mortgage insurance required?", Frequency.Monthly),
            Line("Fees", "HOA or service charges", "Are there association or service charges?", Frequency.Monthly),
            Line("Maintenance", "Maintenance reserve", "What will you set aside for maintenance, often 1 percent of value a year?", Frequency.Annual, critical: true),
            Line("Utilities", "Change in utilities", "Will utilities cost more than now?", Frequency.Monthly),
            Line("Setup", "Furniture and immediate repairs", "What must you spend on moving in?", Frequency.OneOff)
        ]
    };

    public static CostTemplate SelfEmployment { get; } = new()
    {
        Kind = ScenarioKind.SelfEmployment,
        Name = "Self-employment",
        Purpose = "Account for the taxes and benefits an employer previously covered.",
        Lines =
        [
            Line("Tax", "Self-employment tax", "Set aside both halves of Social Security and Medicare.", Frequency.Monthly, critical: true),
            Line("Tax", "Quarterly estimated tax", "What quarterly payments will you owe?", Frequency.Quarterly, critical: true),
            Line("Benefits", "Own health insurance", "What will you pay for health cover without an employer?", Frequency.Monthly, critical: true),
            Line("Benefits", "Lost employer retirement match", "What employer match are you giving up?", Frequency.Monthly, critical: true),
            Line("Benefits", "No paid leave", "What will unpaid holiday and sick days cost?", Frequency.Annual, critical: true),
            Line("Setup", "Equipment and software", "What will you need to buy?", Frequency.OneOff),
            Line("Running", "Insurance and licences", "What business insurance and licences are needed?", Frequency.Annual),
            Line("Running", "Accounting and admin", "What will accounting cost?", Frequency.Annual),
            Line("Risk", "Income variability buffer", "How much buffer do you need for slow months?", Frequency.Monthly, critical: true)
        ]
    };

    public static CostTemplate ChangeMedicalPlan { get; } = new()
    {
        Kind = ScenarioKind.ChangeMedicalPlan,
        Name = "Change medical plan",
        Purpose = "Compare plans on total exposure, not premium alone.",
        Lines =
        [
            Line("Premium", "Change in premium", "How will your per-period premium change?", Frequency.Monthly, critical: true),
            Line("Exposure", "New deductible", "What is the new deductible?", Frequency.Annual, critical: true),
            Line("Exposure", "New out-of-pocket maximum", "What is the new out-of-pocket maximum?", Frequency.Annual, critical: true),
            Line("Use", "Expected claims this year", "What care do you expect to need?", Frequency.Annual, critical: true),
            Line("Use", "Prescription costs", "How will prescription costs change?", Frequency.Monthly),
            Line("Access", "Losing a current provider", "Would you have to change doctor or clinic?", Frequency.Annual),
            Line("Savings", "HSA or FSA contribution", "Will you contribute to a health savings account?", Frequency.Monthly)
        ]
    };

    public static IReadOnlyList<CostTemplate> All { get; } =
    [
        CarOwnership,
        HomeRental,
        HomePurchase,
        Moving,
        NewChild,
        PetOwnership,
        CollegeOrTraining,
        Travel,
        MobilePhone,
        Appliance,
        Subscription,
        MedicalProcedure,
        NewJobOrCommute,
        SelfEmployment,
        Loan,
        BalanceTransfer,
        ChangeMedicalPlan
    ];

    public static CostTemplate? ForKind(ScenarioKind kind) =>
        All.FirstOrDefault(template => template.Kind == kind);

    private static CostLine Line(
        string area,
        string name,
        string question,
        Frequency frequency,
        bool critical = false,
        bool nonCash = false) =>
        new()
        {
            Area = area,
            Name = name,
            Question = question,
            Frequency = frequency,
            IsCritical = critical,
            IsNonCash = nonCash,
            State = CostLineState.Unanswered
        };
}
