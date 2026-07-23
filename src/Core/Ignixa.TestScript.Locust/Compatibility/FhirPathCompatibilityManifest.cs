using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignixa.TestScript.Locust.Compatibility;

/// <summary>
/// The reviewed set of FHIRPath expression/usage pairs known to evaluate differently between Ignixa's
/// FhirPath engine and the Locust runtime's <c>fhirpathpy</c> adapter. The compiler consults this
/// manifest before lowering: any definition using a listed expression in its listed usage is rejected
/// with a LOCUST009 diagnostic instead of producing a workload whose runtime behavior would silently
/// disagree with the authoritative Ignixa semantics.
/// </summary>
internal sealed class FhirPathCompatibilityManifest
{
    private const string EmbeddedAssetSuffix = ".Compatibility.fhirpath-incompatibilities.json";

    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Dictionary<(string Expression, FhirPathUsage Usage), string> _reasons;

    /// <summary>
    /// Creates a manifest from the supplied incompatibility entries.
    /// </summary>
    /// <param name="entries">The reviewed incompatibilities. Duplicate (expression, usage) pairs are rejected.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> or one of its members is null.</exception>
    /// <exception cref="ArgumentException">An entry has a null/blank expression or reason.</exception>
    /// <exception cref="InvalidOperationException">Two entries share the same (expression, usage) pair.</exception>
    internal FhirPathCompatibilityManifest(IEnumerable<FhirPathIncompatibility> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _reasons = [];
        foreach (FhirPathIncompatibility entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Expression);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Reason);

            var key = (entry.Expression, entry.Usage);
            if (!_reasons.TryAdd(key, entry.Reason))
            {
                throw new InvalidOperationException(
                    $"Duplicate FHIRPath incompatibility entry for expression '{entry.Expression}' " +
                    $"usage '{entry.Usage}'.");
            }
        }
    }

    /// <summary>
    /// Returns the reviewed incompatibility reason for the given expression and usage, or
    /// <see langword="null"/> when the pair is compatible (not listed).
    /// </summary>
    internal string? FindReason(string expression, FhirPathUsage usage)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return _reasons.TryGetValue((expression, usage), out string? reason) ? reason : null;
    }

    /// <summary>
    /// Loads the manifest from the reviewed denylist asset embedded in this assembly. This is the
    /// source the parameterless <see cref="Compilation.LocustIrCompiler"/> constructor uses.
    /// </summary>
    internal static FhirPathCompatibilityManifest LoadEmbedded()
    {
        Assembly assembly = typeof(FhirPathCompatibilityManifest).Assembly;
        string[] matches =
        [
            .. assembly.GetManifestResourceNames()
                .Where(name => name.EndsWith(EmbeddedAssetSuffix, StringComparison.Ordinal))
        ];

        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one embedded resource ending with '{EmbeddedAssetSuffix}' in assembly " +
                $"'{assembly.FullName}', but found {matches.Length}.");
        }

        using Stream stream = assembly.GetManifestResourceStream(matches[0])
            ?? throw new InvalidOperationException($"Embedded resource '{matches[0]}' could not be opened.");

        IReadOnlyList<FhirPathIncompatibility> entries =
            JsonSerializer.Deserialize<IReadOnlyList<FhirPathIncompatibility>>(stream, s_serializerOptions)
            ?? throw new InvalidOperationException($"Embedded resource '{matches[0]}' deserialized to null.");

        return new FhirPathCompatibilityManifest(entries);
    }
}
