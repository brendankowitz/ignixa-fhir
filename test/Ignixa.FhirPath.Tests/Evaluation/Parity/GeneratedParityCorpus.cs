using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Benchmarks.Firely5;
using Ignixa.FhirFakes;
using Ignixa.Search.Definition;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal static class GeneratedParityCorpus
{
    private const int Seed = 405;
    private const string StableLastUpdated = "2024-06-15T08:00:00.0000000Z";
    private static readonly FhirVersion[] Versions =
    [
        FhirVersion.Stu3,
        FhirVersion.R4,
        FhirVersion.R4B,
        FhirVersion.R5,
        FhirVersion.R6,
    ];
    private static readonly Lazy<IReadOnlyList<GeneratedParityVersion>> Corpus = new(
        () => Versions.Select(BuildVersion).ToArray());
    private static readonly Lazy<IReadOnlyDictionary<FhirVersion, ExpressionContext>>
        ExpressionsByVersion = new(
            () => Versions.ToDictionary(
                version => version,
                CreateExpressionContext));

    public static IReadOnlyList<GeneratedParityVersion> Build() => Corpus.Value;

    public static GeneratedParityResource BuildResource(FhirVersion version, string resourceType)
    {
        var schema = version.GetSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schema, Seed)
        {
            Density = GenerationDensity.Maximum
        };

        return BuildResource(
            version,
            resourceType,
            faker,
            ExpressionsByVersion.Value[version]);
    }

    private static GeneratedParityResource BuildResource(
        FhirVersion version,
        string resourceType,
        SchemaBasedFhirResourceFaker faker,
        ExpressionContext expressions)
    {
        var resource = faker.Generate(resourceType);
        resource.Meta.LastUpdated = StableLastUpdated;

        return new GeneratedParityResource(
            version,
            resourceType,
            NormalizeGeneratedTemporals(resource.SerializeToString()),
            ApplicableExpressions(resourceType, expressions));
    }

    private static GeneratedParityVersion BuildVersion(FhirVersion version)
    {
        var schema = version.GetSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schema, Seed)
        {
            Density = GenerationDensity.Maximum
        };
        var expressions = ExpressionsByVersion.Value[version];
        var resources = schema.ResourceTypeNames.Order(StringComparer.Ordinal)
            .Select(resourceType => BuildResource(version, resourceType, faker, expressions))
            .ToArray();

        return new GeneratedParityVersion(version, resources);
    }

    private static IReadOnlyList<string> ApplicableExpressions(
        string resourceType,
        ExpressionContext context) =>
        context.Definitions.GetSearchParameters(resourceType)
            .Select(parameter => parameter.Expression)
            .Where(expression => !string.IsNullOrWhiteSpace(expression) && context.CommonExpressions.Contains(expression))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray()!;

    private static ExpressionContext CreateExpressionContext(FhirVersion version)
    {
        FirelyEngine.EnsureInitialized();
        var schema = version.GetSchemaProvider();
        var corpus = SearchParameterExpressionCorpus.Load(version);
        return new ExpressionContext(
            new SearchParameterDefinitionManager(
                schema,
                NullLogger<SearchParameterDefinitionManager>.Instance),
            corpus.CommonExpressions.ToHashSet(StringComparer.Ordinal));
    }

    private static string NormalizeGeneratedTemporals(string json)
    {
        JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Generated resource JSON was empty.");
        Normalize(root);
        return root.ToJsonString();

        static void Normalize(JsonNode node)
        {
            if (node is JsonObject jsonObject)
            {
                foreach (var property in jsonObject.ToArray())
                {
                    if (property.Value is JsonValue value
                        && value.TryGetValue<string>(out var text)
                        && TryNormalize(text, out var normalized))
                    {
                        jsonObject[property.Key] = normalized;
                    }
                    else if (property.Value is not null)
                    {
                        Normalize(property.Value);
                    }

                }
            }
            else if (node is JsonArray jsonArray)
            {
                foreach (var child in jsonArray)
                {
                    if (child is not null)
                    {
                        Normalize(child);
                    }
                }
            }
        }

        static bool TryNormalize(string value, out string normalized)
        {
            normalized = value;
            if (value.Length >= 4
                && value[0..4].All(char.IsDigit)
                && value.Length > 4
                && value[4] == '-'
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
            {
                normalized = value.Contains('T', StringComparison.Ordinal)
                    ? "2024-06-15T08:00:00Z"
                    : "2024-06-15";
                return true;
            }

            if (value.Length >= 5
                && value[0..2].All(char.IsDigit)
                && value[2] == ':'
                && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out _))
            {
                normalized = "08:00:00";
                return true;
            }

            return false;
        }
    }

    private sealed record ExpressionContext(
        ISearchParameterDefinitionManager Definitions,
        IReadOnlySet<string> CommonExpressions);
}
