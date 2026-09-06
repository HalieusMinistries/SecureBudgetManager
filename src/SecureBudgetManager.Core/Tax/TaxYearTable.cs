using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Tax;

public enum FilingStatus
{
    Single = 0,
    MarriedFilingJointly = 1,
    MarriedFilingSeparately = 2,
    HeadOfHousehold = 3
}

/// <summary>A marginal rate band. <paramref name="UpTo"/> is null for the top band.</summary>
public sealed record TaxBracket(Money UpTo, decimal Rate)
{
    public static TaxBracket Top(decimal rate) => new(new Money(decimal.MaxValue), rate);
}

/// <summary>
/// Tax rules for one year. Everything is data, so a new year is added by supplying a new table
/// rather than by changing calculation code.
/// </summary>
public sealed record TaxYearTable
{
    public required int Year { get; init; }

    /// <summary>Where these figures came from, so a user can check them.</summary>
    public required string Provenance { get; init; }

    /// <summary>
    /// False until a human has checked the figures against the current IRS publications.
    /// The UI must warn whenever an unverified table is used.
    /// </summary>
    public bool IsVerified { get; init; }

    public required IReadOnlyDictionary<FilingStatus, IReadOnlyList<TaxBracket>> FederalBrackets { get; init; }

    public required IReadOnlyDictionary<FilingStatus, Money> StandardDeduction { get; init; }

    public decimal SocialSecurityRate { get; init; } = 0.062m;

    public required Money SocialSecurityWageBase { get; init; }

    public decimal MedicareRate { get; init; } = 0.0145m;

    public decimal AdditionalMedicareRate { get; init; } = 0.009m;

    public required Money AdditionalMedicareThreshold { get; init; }

    /// <summary>
    /// Amount added to a non-resident alien's wages for federal withholding purposes, because an
    /// NRA generally cannot claim the standard deduction. See IRS Notice 1392.
    /// </summary>
    public required Money NonResidentAlienWageAddition { get; init; }

    /// <summary>Credit per qualifying child claimed on the W-4.</summary>
    public Money CreditPerQualifyingChild { get; init; } = new(2000m);

    /// <summary>Credit per other dependant claimed on the W-4.</summary>
    public Money CreditPerOtherDependant { get; init; } = new(500m);

    public void Validate()
    {
        if (Year < 2000 || Year > 2100)
        {
            throw new ArgumentException("The tax year looks implausible.");
        }

        if (string.IsNullOrWhiteSpace(Provenance))
        {
            throw new ArgumentException("A tax table must record where its figures came from.");
        }

        foreach (var (status, brackets) in FederalBrackets)
        {
            if (brackets.Count == 0)
            {
                throw new ArgumentException($"No brackets were supplied for {status}.");
            }

            for (var i = 1; i < brackets.Count; i++)
            {
                if (brackets[i].UpTo <= brackets[i - 1].UpTo)
                {
                    throw new ArgumentException($"Brackets for {status} must be in ascending order.");
                }

                if (brackets[i].Rate < brackets[i - 1].Rate)
                {
                    throw new ArgumentException($"Bracket rates for {status} must not decrease.");
                }
            }

            if (brackets.Any(bracket => bracket.Rate is < 0m or > 1m))
            {
                throw new ArgumentException($"Bracket rates for {status} must be between 0 and 1.");
            }
        }

        foreach (var status in Enum.GetValues<FilingStatus>())
        {
            if (!FederalBrackets.ContainsKey(status))
            {
                throw new ArgumentException($"The table is missing brackets for {status}.");
            }

            if (!StandardDeduction.ContainsKey(status))
            {
                throw new ArgumentException($"The table is missing a standard deduction for {status}.");
            }
        }
    }

    /// <summary>Applies the marginal bands to a taxable amount.</summary>
    public Money CalculateFederalTax(Money taxableIncome, FilingStatus status)
    {
        if (taxableIncome <= Money.Zero)
        {
            return Money.Zero;
        }

        var brackets = FederalBrackets[status];
        var tax = 0m;
        var previousCeiling = 0m;

        foreach (var bracket in brackets)
        {
            var ceiling = bracket.UpTo.Amount;
            var amountInBand = Math.Min(taxableIncome.Amount, ceiling) - previousCeiling;

            if (amountInBand > 0m)
            {
                tax += amountInBand * bracket.Rate;
            }

            if (taxableIncome.Amount <= ceiling)
            {
                break;
            }

            previousCeiling = ceiling;
        }

        return new Money(tax);
    }
}

public static class TaxYearLibrary
{
    public const string NotTaxAdviceWarning =
        "These figures are an estimate to help with budgeting. They are not tax advice. " +
        "Always check them against your own payslip and the current IRS and state publications.";

    public const string UnverifiedTableWarning =
        "This tax table has not been checked against the current published rates. " +
        "Review it in Settings before relying on the withholding estimate.";

    /// <summary>
    /// Built-in default table. The figures are the 2025 federal amounts to the best of this
    /// build's knowledge and are marked unverified on purpose: the household must confirm them.
    /// </summary>
    public static TaxYearTable Default2025 { get; } = new()
    {
        Year = 2025,
        Provenance =
            "IRS 2025 Publication 15-T / Rev. Proc. 2024-40 amounts used for 2025 payroll " +
            "withholding in this build. Social Security wage base $176,100 (SSA 2025). " +
            "Later 2025 legislation may have changed some return-filing amounts; historical " +
            "payslips keep the dollars recorded on the slip.",
        IsVerified = false,
        SocialSecurityWageBase = new Money(176_100m),
        AdditionalMedicareThreshold = new Money(200_000m),
        NonResidentAlienWageAddition = new Money(15_000m),
        StandardDeduction = new Dictionary<FilingStatus, Money>
        {
            [FilingStatus.Single] = new(15_000m),
            [FilingStatus.MarriedFilingJointly] = new(30_000m),
            [FilingStatus.MarriedFilingSeparately] = new(15_000m),
            [FilingStatus.HeadOfHousehold] = new(22_500m)
        },
        FederalBrackets = new Dictionary<FilingStatus, IReadOnlyList<TaxBracket>>
        {
            [FilingStatus.Single] =
            [
                new(new Money(11_925m), 0.10m),
                new(new Money(48_475m), 0.12m),
                new(new Money(103_350m), 0.22m),
                new(new Money(197_300m), 0.24m),
                new(new Money(250_525m), 0.32m),
                new(new Money(626_350m), 0.35m),
                TaxBracket.Top(0.37m)
            ],
            [FilingStatus.MarriedFilingJointly] =
            [
                new(new Money(23_850m), 0.10m),
                new(new Money(96_950m), 0.12m),
                new(new Money(206_700m), 0.22m),
                new(new Money(394_600m), 0.24m),
                new(new Money(501_050m), 0.32m),
                new(new Money(751_600m), 0.35m),
                TaxBracket.Top(0.37m)
            ],
            [FilingStatus.MarriedFilingSeparately] =
            [
                new(new Money(11_925m), 0.10m),
                new(new Money(48_475m), 0.12m),
                new(new Money(103_350m), 0.22m),
                new(new Money(197_300m), 0.24m),
                new(new Money(250_525m), 0.32m),
                new(new Money(375_800m), 0.35m),
                TaxBracket.Top(0.37m)
            ],
            [FilingStatus.HeadOfHousehold] =
            [
                new(new Money(17_000m), 0.10m),
                new(new Money(64_850m), 0.12m),
                new(new Money(103_350m), 0.22m),
                new(new Money(197_300m), 0.24m),
                new(new Money(250_500m), 0.32m),
                new(new Money(626_350m), 0.35m),
                TaxBracket.Top(0.37m)
            ]
        }
    };

    public static TaxYearTable Default2026 { get; } = new()
    {
        Year = 2026,
        Provenance =
            "IRS newsroom inflation adjustments for tax year 2026; Publication 15 (2026) Social " +
            "Security wage base $184,500; Publication 15-T (2026) Table 2 annual NRA addition " +
            "$16,100. Not a substitute for a qualified tax professional.",
        IsVerified = false,
        SocialSecurityWageBase = new Money(184_500m),
        AdditionalMedicareThreshold = new Money(200_000m),
        NonResidentAlienWageAddition = new Money(16_100m),
        StandardDeduction = new Dictionary<FilingStatus, Money>
        {
            [FilingStatus.Single] = new(16_100m),
            [FilingStatus.MarriedFilingJointly] = new(32_200m),
            [FilingStatus.MarriedFilingSeparately] = new(16_100m),
            [FilingStatus.HeadOfHousehold] = new(24_150m)
        },
        FederalBrackets = new Dictionary<FilingStatus, IReadOnlyList<TaxBracket>>
        {
            [FilingStatus.Single] =
            [
                new(new Money(12_400m), 0.10m),
                new(new Money(50_400m), 0.12m),
                new(new Money(105_700m), 0.22m),
                new(new Money(201_775m), 0.24m),
                new(new Money(256_225m), 0.32m),
                new(new Money(640_600m), 0.35m),
                TaxBracket.Top(0.37m)
            ],
            [FilingStatus.MarriedFilingJointly] =
            [
                new(new Money(24_800m), 0.10m),
                new(new Money(100_800m), 0.12m),
                new(new Money(211_400m), 0.22m),
                new(new Money(403_550m), 0.24m),
                new(new Money(512_450m), 0.32m),
                new(new Money(768_700m), 0.35m),
                TaxBracket.Top(0.37m)
            ],
            [FilingStatus.MarriedFilingSeparately] =
            [
                new(new Money(12_400m), 0.10m),
                new(new Money(50_400m), 0.12m),
                new(new Money(105_700m), 0.22m),
                new(new Money(201_775m), 0.24m),
                new(new Money(256_225m), 0.32m),
                new(new Money(384_350m), 0.35m),
                TaxBracket.Top(0.37m)
            ],
            [FilingStatus.HeadOfHousehold] =
            [
                new(new Money(17_700m), 0.10m),
                new(new Money(67_450m), 0.12m),
                new(new Money(105_700m), 0.22m),
                new(new Money(201_775m), 0.24m),
                new(new Money(256_200m), 0.32m),
                new(new Money(640_600m), 0.35m),
                TaxBracket.Top(0.37m)
            ]
        }
    };

    public static IReadOnlyList<TaxYearTable> BuiltIn { get; } = [Default2025, Default2026];

    public const string Unavailable = "Tax rules unavailable or require review";

    /// <summary>
    /// Exact year only. A missing year is not silently replaced with an older table.
    /// </summary>
    public static bool TryGetYear(
        int year,
        out TaxYearTable? table,
        out string warning,
        IEnumerable<TaxYearTable>? userTables = null)
    {
        var exact = (userTables ?? []).Concat(BuiltIn).FirstOrDefault(item => item.Year == year);
        if (exact is null)
        {
            table = null;
            warning = Unavailable;
            return false;
        }

        table = exact;
        warning = exact.IsVerified ? TaxYearLibrary.NotTaxAdviceWarning : UnverifiedTableWarning;
        return true;
    }

    public static TaxYearTable ForYear(int year, IEnumerable<TaxYearTable>? userTables = null)
    {
        if (TryGetYear(year, out var table, out _, userTables) && table is not null)
        {
            return table;
        }

        throw new InvalidOperationException(Unavailable);
    }
}
