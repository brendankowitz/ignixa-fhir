using System.Text.Json.Nodes;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>Stored package SearchParameters, and a package repository that serves them for activation.</summary>
internal static class TestPackages
{
    public const string BaseIdentifier = "http://hl7.org/fhir/SearchParameter/Patient-identifier";

    public const string PackageVersion = "1.0.0";

    public static PackageResource SearchParameter(
        string packageId,
        string canonical,
        string code = "identifier",
        IReadOnlyList<string>? baseTypes = null,
        string expression = "Resource.identifier",
        string? derivedFrom = null,
        string type = "token",
        string fhirVersion = "4.0.1",
        string version = "1.0.0")
    {
        var resource = Json(canonical, code, baseTypes ?? ["Patient"], type, expression);
        if (derivedFrom is not null)
        {
            resource["derivedFrom"] = derivedFrom;
        }

        return Stored(packageId, version, fhirVersion, canonical, code, resource);
    }

    /// <summary>A composite at <see cref="Canonical"/>(<paramref name="code"/>) over the given component definitions.</summary>
    public static PackageResource Composite(
        string packageId,
        string code,
        string baseType,
        params (string Definition, string Expression)[] components)
    {
        var resource = Json(Canonical(code), code, [baseType], "composite", baseType);
        resource["component"] = new JsonArray([.. components.Select(component =>
            new JsonObject { ["definition"] = component.Definition, ["expression"] = component.Expression })]);
        return Stored(packageId, "1.0.0", "4.0.1", Canonical(code), code, resource);
    }

    public static string Canonical(string name) => $"http://example.org/SearchParameter/{name}";

    /// <summary>A package with one custom (non-shadowing) Patient parameter: activation leaves it Pending.</summary>
    public static PackageResource Custom(string packageId = "test.custom") =>
        SearchParameter(packageId, Canonical(packageId), "custom", expression: "Patient.id", type: "string");

    /// <summary>A package parameter that shadows the in-process base Patient.identifier: activation stages it.</summary>
    public static PackageResource Shadow(string packageId = "test.override", string? derivedFrom = BaseIdentifier) =>
        SearchParameter(packageId, Canonical(packageId), derivedFrom: derivedFrom);

    public static IPackageResourceRepository Repository(params PackageResource[] resources)
    {
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetResourcesForActivationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => resources
                .Where(resource => resource.PackageId == call.ArgAt<string>(0) && resource.PackageVersion == call.ArgAt<string>(1))
                .ToArray());
        return repository;
    }

    /// <summary>An implementation guide provider that stores <paramref name="packageId"/>@<see cref="PackageVersion"/> for tenant 1.</summary>
    public static IImplementationGuideProvider Provider(string packageId)
    {
        var provider = Substitute.For<IImplementationGuideProvider>();
        provider.LoadPackageAsync("1", packageId, PackageVersion, Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult { PackageId = packageId, PackageVersion = PackageVersion, ImportedResources = 1 });
        return provider;
    }

    private static JsonObject Json(string canonical, string code, IReadOnlyList<string> baseTypes, string type, string expression) =>
        new()
        {
            ["resourceType"] = "SearchParameter",
            ["id"] = code,
            ["url"] = canonical,
            ["code"] = code,
            ["base"] = new JsonArray([.. baseTypes.Select(baseType => JsonValue.Create(baseType))]),
            ["type"] = type,
            ["expression"] = expression,
        };

    private static PackageResource Stored(string packageId, string version, string fhirVersion, string canonical, string code, JsonObject resource) =>
        new()
        {
            PackageId = packageId,
            PackageVersion = version,
            ResourceType = "SearchParameter",
            ResourceId = code,
            Canonical = canonical,
            FhirVersion = fhirVersion,
            ResourceJson = resource.ToJsonString(),
        };
}
