using System.Globalization;
using Semver;

namespace Ignixa.TestScript.Evaluation;

/// <summary>
/// Shared FHIR-version compatibility matching used by both <see cref="TestScriptEvaluator"/> and the
/// Locust IR compiler, so the two runtimes never drift on what "this test targets FHIR version X"
/// means.
/// </summary>
internal static class TestScriptVersionCompatibility
{
    /// <summary>
    /// Matches a test's declared <c>fhirVersions</c> spec tokens against the actual FHIR version
    /// being targeted. Each spec token is checked two ways: an exact case-insensitive string match
    /// (preserving today's behavior for existing suites, e.g. "4.0" == "4.0"), or a granular
    /// numeric Major/Minor/Patch comparison that supports "4.*" (major-only), "4.0" (major.minor),
    /// and "4.0.1" (exact) against a semver-parsed <paramref name="fhirVersion"/>. Prerelease and
    /// build metadata are ignored by the granular comparison. Unparseable input fails safe (no
    /// match via that path) rather than throwing. An empty <paramref name="fhirVersions"/> list or
    /// a <see langword="null"/> <paramref name="fhirVersion"/> short-circuits to a match (the test
    /// runs) rather than a mismatch, so gating never turns into a silent skip when either side of
    /// the comparison is unknown.
    /// </summary>
    public static bool IsCompatible(IReadOnlyList<string> fhirVersions, string? fhirVersion)
    {
        if (fhirVersions.Count == 0) return true;
        if (fhirVersion is null) return true;

        var parsedActual = SemVersion.TryParse(fhirVersion, SemVersionStyles.Any, out var actual) ? actual : null;

        foreach (var spec in fhirVersions)
        {
            if (string.Equals(spec, fhirVersion, StringComparison.OrdinalIgnoreCase))
                return true;

            if (parsedActual is not null && MatchesVersionSpec(spec, parsedActual))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the skip reason for a version mismatch, distinguishing a legitimate scoping mismatch
    /// (at least one <paramref name="fhirVersions"/> token parses as a recognizable version spec,
    /// it just doesn't match <paramref name="fhirVersion"/>) from every token being unrecognizable
    /// (likely an authoring typo, e.g. "4,0" or "r4") — mirroring how the capability-requirement
    /// evaluation already distinguishes "not met" from "malformed" so a typo doesn't read
    /// identically to an intentional version restriction.
    /// </summary>
    public static string MismatchReason(IReadOnlyList<string> fhirVersions, string? fhirVersion)
    {
        var anyTokenRecognized = fhirVersions.Any(spec => TryParseVersionSpec(spec, out _, out _, out _));
        return anyTokenRecognized
            ? $"Test targets FHIR version(s) [{string.Join(", ", fhirVersions)}] but execution requested '{fhirVersion}'"
            : $"Test's fhirVersions [{string.Join(", ", fhirVersions)}] contain no recognizable version spec — check for a typo";
    }

    private static bool MatchesVersionSpec(string? spec, SemVersion actual)
    {
        if (!TryParseVersionSpec(spec, out var major, out var minor, out var patch))
            return false;

        if (actual.Major != major) return false;
        if (minor is not null && actual.Minor != minor) return false;
        if (patch is not null && actual.Patch != patch) return false;
        return true;
    }

    /// <summary>
    /// Parses a spec token such as "4", "4.*", "4.0", or "4.0.1" into 1-3 numeric components.
    /// A trailing "*" segment is dropped (it's the major-only wildcard marker). Returns false for
    /// <see langword="null"/> or anything that isn't 1-3 non-negative integer segments, so
    /// malformed tokens simply don't match via the granular path rather than throwing.
    /// </summary>
    private static bool TryParseVersionSpec(string? spec, out int major, out int? minor, out int? patch)
    {
        major = 0;
        minor = null;
        patch = null;

        if (spec is null) return false;

        var segments = spec.Split('.');
        if (segments.Length > 0 && segments[^1] == "*")
            segments = segments[..^1];

        if (segments.Length is 0 or > 3) return false;

        var values = new int?[3];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                return false;
            values[i] = value;
        }

        major = values[0]!.Value;
        minor = values[1];
        patch = values[2];
        return true;
    }
}
