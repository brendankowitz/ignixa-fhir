using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.FhirFakes;

public sealed class FhirFakesFixtureProvider : IFixtureProvider
{
    public ValueTask<ResourceJsonNode?> ResolveFixtureAsync(
        FixtureDefinition fixture,
        FixtureResolutionContext context,
        CancellationToken cancellationToken)
    {
        var options = FhirFakesFixtureOptionsParser.Parse(fixture);
        if (options is null)
            return ValueTask.FromResult<ResourceJsonNode?>(null);

        var resource = Generate(options, context);

        return ValueTask.FromResult<ResourceJsonNode?>(resource);
    }

    private static ResourceJsonNode Generate(FhirFakesFixtureOptions options, FixtureResolutionContext context)
    {
        var resource = options.ResourceType == "Patient" && options.Patient is not null
            ? GeneratePatient(options, context)
            : GenerateSchemaBased(options, context);

        ApplyEdgeCases(resource, options, context);
        return resource;
    }

    private static ResourceJsonNode GenerateSchemaBased(FhirFakesFixtureOptions options, FixtureResolutionContext context)
    {
        var faker = options.Seed is { } seed
            ? new SchemaBasedFhirResourceFaker(context.Schema, seed)
            : new SchemaBasedFhirResourceFaker(context.Schema);

        if (options.Density is { } density)
        {
            faker.Density = density;
        }

        if (options.Theme is { } theme)
        {
            faker.Theme = theme;
        }

        if (options.Tag is { } tag)
        {
            faker.WithTag(tag);
        }

        var resource = faker.Generate(options.ResourceType);
        if (options.Profile is { } profile)
        {
            AddProfile(resource, profile);
        }

        return resource;
    }

    private static ResourceJsonNode GeneratePatient(FhirFakesFixtureOptions options, FixtureResolutionContext context)
    {
        var builder = PatientBuilderFactory.Create(context.Schema, options.Seed);
        var patient = options.Patient!;

        if (options.Tag is { } tag) builder.WithTag(tag);
        if (options.Profile is { } profile) builder.WithProfile(profile);
        if (patient.GivenName is { } givenName) builder.WithGivenName(givenName);
        if (patient.FamilyName is { } familyName) builder.WithFamilyName(familyName);
        if (patient.Gender is { } gender) builder.WithGender(gender);
        if (patient.Age is { } age) builder.WithAge(age);
        if (patient.BirthDate is { } birthDate) ApplyBirthDate(builder, birthDate);
        if (patient.City is { } city) builder.WithCity(city);
        if (patient.State is { } state) builder.WithState(state);
        if (patient.ZipCode is { } zipCode) builder.WithZipCode(zipCode);
        if (patient.Active is { } active) builder.WithActive(active);
        if (patient.Bmi is { } bmi) builder.WithBMI(bmi);

        foreach (var identifier in patient.Identifiers)
        {
            if (identifier.System is { } system)
            {
                builder.WithIdentifier(system, identifier.Value);
            }
            else
            {
                builder.WithTypedIdentifier(identifier.Value, "http://terminology.hl7.org/CodeSystem/v2-0203", "MR");
            }
        }

        return builder.Build();
    }

    private static void ApplyBirthDate(PatientBuilder builder, string birthDate)
    {
        var parts = birthDate.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            builder.WithBirthDate(year);
            return;
        }

        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month))
        {
            builder.WithBirthDate(year, month);
            return;
        }

        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out month)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            builder.WithBirthDate(year, month, day);
            return;
        }

        throw new FormatException($"Unsupported FhirFakes patient birthDate '{birthDate}'.");
    }

    private static void ApplyEdgeCases(ResourceJsonNode resource, FhirFakesFixtureOptions options, FixtureResolutionContext context)
    {
        if (options.EdgeCases is not { } edgeCases) return;

        var catalog = EdgeCaseCatalog.CreateDefault();
        var strategies = catalog.Resolve(edgeCases.Selectors);
        var seed = edgeCases.Seed ?? options.Seed ?? 0;
        var pipeline = new EdgeCasePipeline(seed, context.Schema);
        pipeline.Apply(resource, strategies);
    }

    private static void AddProfile(ResourceJsonNode resource, string profile)
    {
        var meta = resource.MutableNode["meta"] as JsonObject;
        if (meta is null)
        {
            meta = [];
            resource.MutableNode["meta"] = meta;
        }

        var profiles = meta["profile"] as JsonArray;
        if (profiles is null)
        {
            profiles = [];
            meta["profile"] = profiles;
        }

        profiles.Add(profile);
        resource.InvalidateCaches();
    }
}
