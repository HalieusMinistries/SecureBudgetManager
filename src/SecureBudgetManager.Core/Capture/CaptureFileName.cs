using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SecureBudgetManager.Core.Capture;

/// <summary>
/// Builds a screenshot filename that never includes household, member or financial data.
/// </summary>
public static class CaptureFileName
{
    private static readonly Regex Disallowed = new(
        @"[^A-Za-z0-9\-]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Suggest(string pageTitle, DateTimeOffset timestamp)
    {
        var page = SanitisePageLabel(pageTitle);
        var stamp = timestamp.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        return $"SecureBudgetManager-{page}-{stamp}.png";
    }

    public static string SanitisePageLabel(string pageTitle)
    {
        if (string.IsNullOrWhiteSpace(pageTitle))
        {
            return "Page";
        }

        var trimmed = pageTitle.Trim();
        var builder = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }

        var compact = Disallowed.Replace(builder.ToString(), "-");
        while (compact.Contains("--", StringComparison.Ordinal))
        {
            compact = compact.Replace("--", "-", StringComparison.Ordinal);
        }

        compact = compact.Trim('-');
        if (compact.Length == 0)
        {
            return "Page";
        }

        return compact.Length > 32 ? compact[..32] : compact;
    }

    public static bool ContainsPersonalOrFinancialHint(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var name = Path.GetFileName(fileName);
        return name.Contains('$')
            || Regex.IsMatch(name, @"\d+\.\d{2}")
            || Regex.IsMatch(name, @"\b(ssn|social-security)\b", RegexOptions.IgnoreCase);
    }

    public static IReadOnlyList<string> SegmentPaths(string destinationPath, int segmentCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentCount);

        if (segmentCount == 1)
        {
            return [destinationPath];
        }

        var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".png";
        }

        var paths = new string[segmentCount];
        for (var i = 0; i < segmentCount; i++)
        {
            paths[i] = Path.Combine(directory, $"{stem}-{i + 1:00}{extension}");
        }

        return paths;
    }
}
