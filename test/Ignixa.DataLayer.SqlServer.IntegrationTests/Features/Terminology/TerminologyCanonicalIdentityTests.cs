using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public sealed class TerminologyCanonicalIdentityTests(TerminologyReplacementFixture fixture)
    : IClassFixture<TerminologyReplacementFixture>
{
    private const string SourceSystem = "http://terminology-contract.example/source";
    private TerminologyTestFixture Database => fixture.Database;
    private SqlServerPackageResourceRepository Packages => new(
        Database.SqlExecutionService, 1, NullLogger<SqlServerPackageResourceRepository>.Instance);

    [Theory]
    [InlineData("ValueSet", null)]
    [InlineData("ConceptMap", null)]
    [InlineData("ValueSet", "Release")]
    [InlineData("ConceptMap", "Release")]
    public async Task GivenCaseDistinctCanonicalUrls_WhenImported_ThenBothPackagesRemainQueryable(string resourceType, string? version)
    {
        var root = $"http://terminology-contract.example/{Guid.NewGuid():N}";
        var upper = await ImportAsync(resourceType, root + "/A", version, "upper");
        var lower = await ImportAsync(resourceType, root + "/a", version, "lower");

        (await Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Term{resourceType} WHERE PackageResourceId IN ({upper.PackageResourceId}, {lower.PackageResourceId})"))
            .ShouldBe(2);
        await AssertContentAsync(resourceType, upper.Canonical, version, "upper");
        await AssertContentAsync(resourceType, lower.Canonical, version, "lower");
        (await Packages.GetByCanonicalAsync(upper.Canonical, version))!.PackageResourceId.ShouldBe(upper.PackageResourceId);
        (await Packages.GetByCanonicalAsync(lower.Canonical, version))!.PackageResourceId.ShouldBe(lower.PackageResourceId);
    }

    [Theory]
    [InlineData("ValueSet")]
    [InlineData("ConceptMap")]
    public async Task GivenOnlyUpperCaseCanonical_WhenQueryingLowerCaseCanonical_ThenItIsNotFound(string resourceType)
    {
        var root = $"http://terminology-contract.example/{Guid.NewGuid():N}";
        await ImportAsync(resourceType, root + "/A", "Release", "upper");
        var service = Database.CreateTerminologyService();
        if (resourceType == "ValueSet")
        {
            (await service.ExpandValueSetAsync(new ExpansionParameters(root + "/a"), CancellationToken.None)).ShouldBeNull();
        }
        else
        {
            (await service.TranslateCodeAsync(Translate(root + "/a", "Release"), CancellationToken.None)).Result.ShouldBeFalse();
        }
        (await ((ITerminologyImportStatusProvider)service).GetImportStatusAsync(root + "/a", CancellationToken.None)).ShouldBeNull();
        (await Packages.GetByCanonicalAsync(root + "/a", "Release")).ShouldBeNull();
    }

    [Theory]
    [InlineData("CodeSystem")]
    [InlineData("ValueSet")]
    [InlineData("ConceptMap")]
    public async Task GivenCaseDistinctResourceVersions_WhenImported_ThenBothVersionsRemainQueryable(string resourceType)
    {
        var canonical = $"http://terminology-contract.example/{Guid.NewGuid():N}";
        var upper = await ImportAsync(resourceType, canonical, "ReleaseA", "upper");
        var lower = await ImportAsync(resourceType, canonical, "releasea", "lower");

        (await Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Term{resourceType} WHERE PackageResourceId IN ({upper.PackageResourceId}, {lower.PackageResourceId})"))
            .ShouldBe(2);
        await AssertContentAsync(resourceType, canonical, "ReleaseA", "upper");
        await AssertContentAsync(resourceType, canonical, "releasea", "lower");
        (await Packages.GetByCanonicalAsync(canonical, "ReleaseA"))!.PackageResourceId.ShouldBe(upper.PackageResourceId);
        (await Packages.GetByCanonicalAsync(canonical, "releasea"))!.PackageResourceId.ShouldBe(lower.PackageResourceId);
        (await Packages.GetByCanonicalAsync(canonical, "RELEASEA")).ShouldBeNull();
        var service = Database.CreateTerminologyService();
        if (resourceType == "CodeSystem")
        {
            (await service.LookupCodeAsync(canonical, "car", "RELEASEA", CancellationToken.None)).Found.ShouldBeFalse();
        }
        else if (resourceType == "ConceptMap")
        {
            (await service.TranslateCodeAsync(Translate(canonical, "RELEASEA"), CancellationToken.None)).Result.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task GivenCaseDistinctReferencedValueSets_WhenComposed_ThenOnlyTheExactCanonicalContributesCodes()
    {
        var root = $"http://terminology-contract.example/{Guid.NewGuid():N}";
        await ImportAsync("ValueSet", root + "/A", null, "upper");
        await ImportAsync("ValueSet", root + "/a", null, "lower");
        var composed = root + "/composed";
        var package = await TerminologyContractResources.StoreAsync(Database, "ValueSet", composed, null, $$$"""
            {"resourceType":"ValueSet","url":"{{{composed}}}","name":"Composed","status":"active",
             "compose":{"include":[{"valueSet":["{{{root}}}/A"]}]}}
            """);

        var result = await Database.CreateSqlServerImporter().ImportValueSetAsync(1, package, CancellationToken.None);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        (await Database.CreateTerminologyService().ExpandValueSetAsync(new ExpansionParameters(composed), CancellationToken.None))!
            .Contains.Single().Code.ShouldBe("upper");
    }

    private async Task<PackageResource> ImportAsync(string resourceType, string canonical, string? version, string marker)
    {
        var json = resourceType switch
        {
            "CodeSystem" => TerminologyContractResources.CodeSystem(canonical, version, marker),
            "ValueSet" => TerminologyContractResources.ValueSet(canonical, version, SourceSystem, marker),
            "ConceptMap" => TerminologyContractResources.ConceptMap(canonical, version, marker),
            _ => throw new InvalidOperationException(),
        };
        var package = await TerminologyContractResources.StoreAsync(Database, resourceType, canonical, version, json);
        var importer = Database.CreateSqlServerImporter();
        var result = resourceType switch
        {
            "CodeSystem" => await importer.ImportCodeSystemAsync(1, package, CancellationToken.None),
            "ValueSet" => await importer.ImportValueSetAsync(1, package, CancellationToken.None),
            "ConceptMap" => await importer.ImportConceptMapAsync(1, package, CancellationToken.None),
            _ => throw new InvalidOperationException(),
        };
        result.Success.ShouldBeTrue(result.ErrorMessage);
        return package;
    }

    private async Task AssertContentAsync(string resourceType, string canonical, string? version, string marker)
    {
        var service = Database.CreateTerminologyService();
        if (resourceType == "CodeSystem")
        {
            (await service.LookupCodeAsync(canonical, "car", version, CancellationToken.None)).Display.ShouldBe(marker);
        }
        else if (resourceType == "ConceptMap")
        {
            (await service.TranslateCodeAsync(Translate(canonical, version), CancellationToken.None))
                .Matches.Single().Concept.Code.ShouldBe(marker);
        }
        else if (version is null || version == "Release")
        {
            (await service.ExpandValueSetAsync(new ExpansionParameters(canonical), CancellationToken.None))!
                .Contains.Single().Code.ShouldBe(marker);
        }
        else
        {
            // ExpansionParameters has no resource-version selector; pin the versioned SQL identity
            // and package lookup without introducing a new operation parameter.
            (await Database.ExecuteScalarAsync<string>(
                "SELECT e.Code FROM dbo.TermValueSetExpansion e JOIN dbo.TermValueSet v ON v.TermValueSetId = e.TermValueSetId " +
                $"WHERE v.Canonical = '{canonical}' AND v.Version = '{version}'")).ShouldBe(marker);
        }
    }

    private static TranslateParameters Translate(string canonical, string? version)
        => new(canonical, version, "car", SourceSystem, null, null, null, null);
}
