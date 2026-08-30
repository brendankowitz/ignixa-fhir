using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal static class TargetedParityCorpus
{
    private static readonly FhirVersion[] Versions =
    [
        FhirVersion.Stu3,
        FhirVersion.R4,
        FhirVersion.R4B,
        FhirVersion.R5,
        FhirVersion.R6,
    ];

    private static readonly Lazy<IReadOnlyList<TargetedParityResource>> Resources = new(
        () => Versions.SelectMany(BuildVersion).ToArray());

    public static IReadOnlyList<TargetedParityResource> Build() => Resources.Value;

    private static IEnumerable<TargetedParityResource> BuildVersion(FhirVersion version)
    {
        yield return Choice(version, "quantity", "valueQuantity", Quantity(9, "mg"), ParityResourceFeature.ChoiceQuantity);
        yield return Choice(version, "dateTime", "valueDateTime", "2012-01", ParityResourceFeature.ChoiceDateTime);
        yield return Choice(version, "string", "valueString", "parity", ParityResourceFeature.ChoiceString);
        yield return Choice(
            version,
            "ratio",
            "valueRatio",
            new JsonObject
            {
                ["numerator"] = Quantity(1, "mg"),
                ["denominator"] = Quantity(1, "mL"),
            },
            ParityResourceFeature.ChoiceRatio);

        yield return Components(
            version,
            "cardinality-0",
            [],
            [ParityResourceFeature.CardinalityZero],
            CardinalityProbes);
        yield return Components(
            version,
            "cardinality-1",
            [Quantity(9, "mg")],
            [ParityResourceFeature.CardinalityOne],
            CardinalityProbes);
        yield return Components(
            version,
            "cardinality-3",
            [Quantity(10, "mg"), Quantity(9, "mg"), Quantity(11, "mg")],
            [ParityResourceFeature.CardinalityMany],
            CardinalityProbes);
        yield return Components(
            version,
            "quantity-equivalence",
            [Quantity(1, "m"), Quantity(104, "cm"), Quantity(100, "cm")],
            [ParityResourceFeature.QuantityEquivalence, ParityResourceFeature.CompatibleUnits],
            QuantityEquivalenceProbes,
            "de-DE");
        yield return Components(
            version,
            "quantity-units",
            [Quantity(1, "mg"), Quantity(1, "m"), Quantity(1, "year")],
            [ParityResourceFeature.IncompatibleUnits, ParityResourceFeature.CalendarQuantity],
            CardinalityProbes,
            "de-DE");
        yield return TemporalComponents(version);
        yield return References(version);
    }

    private static TargetedParityResource Choice(
        FhirVersion version,
        string name,
        string propertyName,
        JsonNode value,
        ParityResourceFeature feature)
    {
        var source = GeneratedParityCorpus.BuildResource(version, "Observation");
        var json = JsonNode.Parse(source.Json)!.AsObject();
        RemoveChoiceProperties(json);
        json[propertyName] = value;

        return Resource(version, $"choice-{name}", json, [feature], source.Expressions, ["Observation.value"]);
    }

    private static TargetedParityResource Components(
        FhirVersion version,
        string name,
        IReadOnlyList<JsonObject> values,
        IReadOnlyList<ParityResourceFeature> features,
        IReadOnlyList<string> probes,
        string? cultureName = null)
    {
        var source = GeneratedParityCorpus.BuildResource(version, "Observation");
        var json = JsonNode.Parse(source.Json)!.AsObject();
        json.Remove("component");
        if (values.Count > 0)
        {
            json["component"] = new JsonArray(values.Select(Component).ToArray());
        }

        return Resource(version, name, json, features, source.Expressions, probes, cultureName);
    }

    private static TargetedParityResource TemporalComponents(FhirVersion version)
    {
        var source = GeneratedParityCorpus.BuildResource(version, "Observation");
        var json = JsonNode.Parse(source.Json)!.AsObject();
        json["component"] = new JsonArray(
            TemporalComponent("2012"),
            TemporalComponent("2012-01"),
            TemporalComponent("2024-06-15T08:00:00Z"),
            TemporalComponent("2024-06-15T10:00:00+02:00"));

        return Resource(
            version,
            "temporal-precision-offset",
            json,
            [ParityResourceFeature.PartialPrecisionTemporal, ParityResourceFeature.EquivalentOffsetTemporal],
            source.Expressions,
            [
                "component.value.first() = component.value.skip(1).first()",
                "component.value.skip(2).first() = component.value.last()",
                "component.value.sort()",
            ],
            "th-TH");
    }

    private static TargetedParityResource References(FhirVersion version)
    {
        var source = GeneratedParityCorpus.BuildResource(version, "Patient");
        var json = JsonNode.Parse(source.Json)!.AsObject();
        json["contained"] = new JsonArray(
            new JsonObject
            {
                ["resourceType"] = "Practitioner",
                ["id"] = "contained",
            });
        json["generalPractitioner"] = new JsonArray(
            Reference("Practitioner/present"),
            Reference("not-a-reference"),
            Reference("#contained"));

        return Resource(
            version,
            "resolve-present-absent-contained",
            json,
            [
                ParityResourceFeature.ResolvePresent,
                ParityResourceFeature.ResolveAbsent,
                ParityResourceFeature.ResolveContained,
            ],
            source.Expressions,
            [
                "generalPractitioner.first().resolve()",
                "generalPractitioner.skip(1).first().resolve()",
                "generalPractitioner.last().resolve()",
            ]);
    }

    private static TargetedParityResource Resource(
        FhirVersion version,
        string name,
        JsonObject json,
        IReadOnlyList<ParityResourceFeature> features,
        IReadOnlyList<string> searchParameterExpressions,
        IReadOnlyList<string> probes,
        string? cultureName = null) =>
        new(
            version,
            name,
            json.ToJsonString(),
            features,
            searchParameterExpressions,
            probes,
            cultureName);

    private static JsonObject Component(JsonObject value) =>
        new()
        {
            ["code"] = Code(),
            ["valueQuantity"] = value,
        };

    private static JsonObject TemporalComponent(string value) =>
        new()
        {
            ["code"] = Code(),
            ["valueDateTime"] = value,
        };

    private static JsonObject Code() =>
        new()
        {
            ["coding"] = new JsonArray(
                new JsonObject
                {
                    ["system"] = "http://loinc.org",
                    ["code"] = "test",
                }),
        };

    private static JsonObject Quantity(decimal value, string code) =>
        new()
        {
            ["value"] = value,
            ["unit"] = code,
            ["system"] = "http://unitsofmeasure.org",
            ["code"] = code,
        };

    private static JsonObject Reference(string value) => new() { ["reference"] = value };

    private static void RemoveChoiceProperties(JsonObject json)
    {
        foreach (string property in json.Select(item => item.Key)
                     .Where(key => key.StartsWith("value", StringComparison.Ordinal))
                     .ToArray())
        {
            json.Remove(property);
        }
    }

    private static IReadOnlyList<string> CardinalityProbes { get; } =
    [
        "component.value.min()",
        "component.value.max()",
        "component.value.sum()",
        "component.value.avg()",
        "component.value.sort()",
    ];

    private static IReadOnlyList<string> QuantityEquivalenceProbes { get; } =
    [
        "component.value.first() ~ component.value.skip(1).first()",
        "component.value.skip(1).first() ~ component.value.first()",
        "component.value.first() ~ component.value.last()",
        "component.value.last() ~ component.value.first()",
    ];
}
