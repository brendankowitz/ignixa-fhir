using System.Globalization;

namespace Ignixa.DataLayer.SqlServer.Features.PackageManagement;

/// <summary>
/// Orders package version strings by their numeric components rather than lexically, so <c>1.10.0</c> ranks
/// above <c>1.9.0</c>. The EF repository ordered these as plain strings while carrying a comment claiming
/// <c>PARSENAME</c>-based semantic parsing that was never written, which made <c>GetLatestByCanonicalAsync</c>
/// return the wrong row for any canonical whose minor or patch number reached double digits.
/// <para>
/// Deliberately not a full SemVer 2.0 implementation: pre-release and build metadata are compared as
/// ordinal text after the numeric components, which is enough to rank real package versions and avoids
/// pulling in a dependency for a single ordering.
/// </para>
/// </summary>
internal sealed class SemanticVersionComparer : IComparer<string>
{
    public static SemanticVersionComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var (xNumbers, xSuffix) = Split(x);
        var (yNumbers, ySuffix) = Split(y);

        var shared = Math.Max(xNumbers.Count, yNumbers.Count);
        for (var i = 0; i < shared; i++)
        {
            // A missing component reads as 0, so "1.2" and "1.2.0" rank equally.
            var left = i < xNumbers.Count ? xNumbers[i] : 0;
            var right = i < yNumbers.Count ? yNumbers[i] : 0;

            if (left != right)
            {
                return left.CompareTo(right);
            }
        }

        return string.CompareOrdinal(xSuffix, ySuffix);
    }

    private static (IReadOnlyList<int> Numbers, string Suffix) Split(string version)
    {
        var suffixStart = version.IndexOfAny(['-', '+']);
        var numericPart = suffixStart >= 0 ? version[..suffixStart] : version;
        var suffix = suffixStart >= 0 ? version[suffixStart..] : string.Empty;

        var numbers = new List<int>();
        foreach (var segment in numericPart.Split('.'))
        {
            if (!int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                // A non-numeric component ends the numeric comparison; everything from here is text.
                suffix = $"{segment}{suffix}";
                break;
            }

            numbers.Add(value);
        }

        return (numbers, suffix);
    }
}
