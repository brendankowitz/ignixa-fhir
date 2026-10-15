// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.Search.Models;

/// <summary>
/// Vector/semantic search configuration carried by the fhir-server <c>vector-search-config</c> extension
/// on a <c>special</c>-typed SearchParameter. A successfully parsed instance on
/// <see cref="SearchParameterInfo.VectorConfig"/> is what makes <see cref="SearchParameterInfo.IsSemantic"/>
/// true; query and indexing behavior that act on it are implemented separately from this parse step.
/// </summary>
/// <param name="ExtractionPolicy">How semantic text is extracted when more than one value matches.</param>
/// <param name="MaxInputTokens">Per-input token cap enforced before chunking. Defaults to 8000.</param>
/// <param name="MinimumScore">The minimum similarity score (0..1) a match must clear. Defaults to 0.</param>
/// <param name="ChunkSizeTokens">Optional chunk size in tokens. When present, must be at least 16.</param>
/// <param name="ChunkOverlapTokens">Optional chunk overlap in tokens. When present, must be non-negative
/// and strictly less than <paramref name="ChunkSizeTokens"/>.</param>
/// <remarks>
/// <c>distanceMetric</c> is validated but not retained: the only legal value is <c>cosine</c>, so a
/// parsed instance never needs to distinguish it from the fixed cosine-ranking behavior downstream.
/// </remarks>
public sealed record VectorSearchConfig(
    VectorTextExtractionPolicy ExtractionPolicy,
    int MaxInputTokens,
    decimal MinimumScore,
    int? ChunkSizeTokens,
    int? ChunkOverlapTokens)
{
    /// <summary>Canonical URL of the vector-search-config extension.</summary>
    public const string ExtensionUrl = "http://microsoft.com/fhir/StructureDefinition/vector-search-config";

    private const string ExtractionPolicyUrl = "extractionPolicy";
    private const string MaxInputTokensUrl = "maxInputTokens";
    private const string MinimumScoreUrl = "minimumScore";
    private const string ChunkSizeTokensUrl = "chunkSizeTokens";
    private const string ChunkOverlapTokensUrl = "chunkOverlapTokens";
    private const string DistanceMetricUrl = "distanceMetric";

    private const int DefaultMaxInputTokens = 8000;
    private const decimal DefaultMinimumScore = 0m;
    private const int MinimumChunkSizeTokens = 16;
    private const string CosineDistanceMetric = "cosine";

    /// <summary>
    /// Parses a <c>vector-search-config</c> extension already navigated to (see
    /// <see cref="Ignixa.Search.Definition.BundleNavigators.SearchParameterNavigator.VectorConfigExtension"/>),
    /// reading sub-extension values with <see cref="TypedElementExtensions.Scalar"/> rather than node
    /// traversal.
    /// </summary>
    /// <param name="extension">The top-level <c>vector-search-config</c> extension element.</param>
    /// <exception cref="FormatException">A sub-extension carries a value that fails validation. The
    /// message names the offending sub-extension.</exception>
    public static VectorSearchConfig Parse(IElement extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        return Build(
            extension.Scalar($"extension('{ExtractionPolicyUrl}').value"),
            extension.Scalar($"extension('{MaxInputTokensUrl}').value"),
            extension.Scalar($"extension('{MinimumScoreUrl}').value"),
            extension.Scalar($"extension('{ChunkSizeTokensUrl}').value"),
            extension.Scalar($"extension('{ChunkOverlapTokensUrl}').value"),
            extension.Scalar($"extension('{DistanceMetricUrl}').value"));
    }

    /// <summary>
    /// Parses a <c>vector-search-config</c> extension from its raw JSON representation, for the package
    /// activation path that reads <see cref="JsonDocument"/> directly rather than building an
    /// <see cref="IElement"/> tree.
    /// </summary>
    /// <param name="extension">The top-level <c>vector-search-config</c> extension JSON object, i.e. an
    /// entry of the resource's <c>extension</c> array whose <c>url</c> equals <see cref="ExtensionUrl"/>.</param>
    /// <exception cref="FormatException">A sub-extension carries a value that fails validation. The
    /// message names the offending sub-extension.</exception>
    public static VectorSearchConfig Parse(JsonElement extension)
    {
        return Build(
            GetSubExtensionValue(extension, ExtractionPolicyUrl),
            GetSubExtensionValue(extension, MaxInputTokensUrl),
            GetSubExtensionValue(extension, MinimumScoreUrl),
            GetSubExtensionValue(extension, ChunkSizeTokensUrl),
            GetSubExtensionValue(extension, ChunkOverlapTokensUrl),
            GetSubExtensionValue(extension, DistanceMetricUrl));
    }

    private static VectorSearchConfig Build(
        object extractionPolicyRaw,
        object maxInputTokensRaw,
        object minimumScoreRaw,
        object chunkSizeTokensRaw,
        object chunkOverlapTokensRaw,
        object distanceMetricRaw)
    {
        VectorTextExtractionPolicy extractionPolicy = ParseExtractionPolicy(extractionPolicyRaw);
        int maxInputTokens = ParseMaxInputTokens(maxInputTokensRaw);
        decimal minimumScore = ParseMinimumScore(minimumScoreRaw);
        int? chunkSizeTokens = ParseChunkSizeTokens(chunkSizeTokensRaw);
        int? chunkOverlapTokens = ParseChunkOverlapTokens(chunkOverlapTokensRaw);
        ValidateDistanceMetric(distanceMetricRaw);

        if (chunkSizeTokens is not null && chunkOverlapTokens is not null && chunkOverlapTokens >= chunkSizeTokens)
        {
            throw new FormatException(
                $"vector-search-config chunkOverlapTokens ({chunkOverlapTokens}) must be less than chunkSizeTokens ({chunkSizeTokens}).");
        }

        return new VectorSearchConfig(extractionPolicy, maxInputTokens, minimumScore, chunkSizeTokens, chunkOverlapTokens);
    }

    private static VectorTextExtractionPolicy ParseExtractionPolicy(object raw)
    {
        if (raw is null) return VectorTextExtractionPolicy.Concatenate;

        string code = Convert.ToString(raw, CultureInfo.InvariantCulture);
        return code switch
        {
            "firstValue" => VectorTextExtractionPolicy.FirstValue,
            "concatenate" => VectorTextExtractionPolicy.Concatenate,
            "perValueRow" => VectorTextExtractionPolicy.PerValueRow,
            _ => throw new FormatException(
                $"vector-search-config extractionPolicy '{code}' is not one of firstValue, concatenate, perValueRow.")
        };
    }

    private static int ParseMaxInputTokens(object raw)
    {
        if (raw is null) return DefaultMaxInputTokens;

        int value = ToInt32(raw, MaxInputTokensUrl);
        if (value <= 0)
        {
            throw new FormatException($"vector-search-config maxInputTokens must be greater than 0, got {value}.");
        }

        return value;
    }

    private static decimal ParseMinimumScore(object raw)
    {
        if (raw is null) return DefaultMinimumScore;

        decimal value = ToDecimal(raw, MinimumScoreUrl);
        if (value < 0 || value > 1)
        {
            throw new FormatException($"vector-search-config minimumScore must be between 0 and 1, got {value}.");
        }

        return value;
    }

    private static int? ParseChunkSizeTokens(object raw)
    {
        if (raw is null) return null;

        int value = ToInt32(raw, ChunkSizeTokensUrl);
        if (value < MinimumChunkSizeTokens)
        {
            throw new FormatException(
                $"vector-search-config chunkSizeTokens must be at least {MinimumChunkSizeTokens}, got {value}.");
        }

        return value;
    }

    private static int? ParseChunkOverlapTokens(object raw)
    {
        if (raw is null) return null;

        int value = ToInt32(raw, ChunkOverlapTokensUrl);
        if (value < 0)
        {
            throw new FormatException($"vector-search-config chunkOverlapTokens must not be negative, got {value}.");
        }

        return value;
    }

    private static void ValidateDistanceMetric(object raw)
    {
        if (raw is null) return;

        string code = Convert.ToString(raw, CultureInfo.InvariantCulture);
        if (!string.Equals(code, CosineDistanceMetric, StringComparison.Ordinal))
        {
            throw new FormatException($"vector-search-config distanceMetric must be '{CosineDistanceMetric}', got '{code}'.");
        }
    }

    private static int ToInt32(object raw, string subExtensionUrl)
    {
        return raw switch
        {
            int i => i,
            decimal d when d == decimal.Truncate(d) => (int)d,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
            _ => throw new FormatException($"vector-search-config {subExtensionUrl} must be a whole number, got '{raw}'.")
        };
    }

    private static decimal ToDecimal(object raw, string subExtensionUrl)
    {
        return raw switch
        {
            decimal d => d,
            int i => i,
            string s when decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
            _ => throw new FormatException($"vector-search-config {subExtensionUrl} must be a decimal number, got '{raw}'.")
        };
    }

    /// <summary>
    /// Finds <paramref name="subExtensionUrl"/> among <paramref name="extension"/>'s nested
    /// <c>extension</c> array and returns its <c>value[x]</c>, mirroring what FHIRPath's <c>value</c>
    /// accessor does for the <see cref="Parse(IElement)"/> overload: the raw string or number regardless
    /// of which <c>value[x]</c> variant the author chose (e.g. <c>valuePositiveInt</c> vs.
    /// <c>valueInteger</c>).
    /// </summary>
    private static object GetSubExtensionValue(JsonElement extension, string subExtensionUrl)
    {
        if (!extension.TryGetProperty("extension", out JsonElement subExtensions) ||
            subExtensions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement sub in subExtensions.EnumerateArray())
        {
            if (!sub.TryGetProperty("url", out JsonElement urlProperty) ||
                urlProperty.ValueKind != JsonValueKind.String ||
                !string.Equals(urlProperty.GetString(), subExtensionUrl, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (JsonProperty property in sub.EnumerateObject())
            {
                if (!property.Name.StartsWith("value", StringComparison.Ordinal))
                {
                    continue;
                }

                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.TryGetInt32(out int i) ? i : property.Value.GetDecimal(),
                    _ => null
                };
            }

            return null;
        }

        return null;
    }
}
