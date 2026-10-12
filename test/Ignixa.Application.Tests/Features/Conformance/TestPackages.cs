using System.Text.Json.Nodes;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>Stored package SearchParameters, and a package repository that serves them for activation.</summary>
internal static class TestPackages
{
    public const string BaseIdentifier = "http://hl7.org/fhir/SearchParameter/Patient-identifier";

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
        var resource = new JsonObject
        {
            ["resourceType"] = "SearchParameter",
            ["id"] = code,
            ["url"] = canonical,
            ["code"] = code,
            ["base"] = new JsonArray((baseTypes ?? ["Patient"]).Select(baseType => JsonValue.Create(baseType)).ToArray<JsonNode?>()),
            ["type"] = type,
            ["expression"] = expression,
        };
        if (derivedFrom is not null)
        {
            resource["derivedFrom"] = derivedFrom;
        }

        return new PackageResource
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
}
