using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public class CanonicalTerminologyReplacementTests(TerminologyReplacementFixture fixture)
    : IClassFixture<TerminologyReplacementFixture>
{
    private TerminologyTestFixture Database => fixture.Database;

    [Theory]
    [InlineData("CodeSystem")]
    [InlineData("ValueSet")]
    [InlineData("ConceptMap")]
    public async Task GivenUsableTerminology_WhenSuccessLogWriteFails_ThenReplacementIsRolledBack(string resourceType)
    {
        var canonical = $"http://terminology-contract.example/{resourceType}/{Guid.NewGuid():N}";
        var package = await StoreAsync(resourceType, canonical, Json(resourceType, canonical));
        (await ImportAsync(package)).Success.ShouldBeTrue();
        var originalHash = await HashAsync(package);
        package.ResourceJson = package.ResourceJson.Replace("Car", "Replacement", StringComparison.Ordinal);
        await Database.ExecuteNonQueryAsync($"INSERT dbo.Parameters (Id, Char) VALUES ('ImportTerm{resourceType}', 'LogEvent')");
        await Database.ExecuteNonQueryAsync("""
            CREATE TRIGGER dbo.TerminologyRejectSuccessLog ON dbo.EventLog AFTER INSERT AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE Status = 'End' AND Process LIKE 'ImportTerm%')
                    THROW 51005, 'Injected terminology success log failure', 1;
            END
            """);
        try
        {
            var result = await ImportAsync(package);
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldNotBeNull();
            result.ErrorMessage.ShouldContain("Injected terminology success log failure");
            (await HashAsync(package)).ShouldBe(originalHash);
            (await StatusAsync(package)).ShouldBe("Failed");
            await AssertUsableAsync(resourceType, canonical, "Car");
        }
        finally
        {
            await Database.ExecuteNonQueryAsync("DROP TRIGGER dbo.TerminologyRejectSuccessLog");
            await Database.ExecuteNonQueryAsync($"DELETE dbo.Parameters WHERE Id = 'ImportTerm{resourceType}'");
        }
    }

    [Fact]
    public async Task GivenReplacementHasInsertedConcepts_WhenSqlCommandIsCancelled_ThenTheOldImportRemainsUsable()
    {
        var canonical = $"http://terminology-contract.example/CodeSystem/{Guid.NewGuid():N}";
        var package = await StoreAsync("CodeSystem", canonical, Json("CodeSystem", canonical));
        (await ImportAsync(package)).Success.ShouldBeTrue();
        var originalHash = await HashAsync(package);
        package.ResourceJson = package.ResourceJson.Replace("Car", "Replacement", StringComparison.Ordinal);
        await Database.ExecuteNonQueryAsync("""
            CREATE TRIGGER dbo.TerminologyPauseConcepts ON dbo.TermConcept AFTER INSERT AS
            BEGIN
                EXEC sys.sp_getapplock @Resource = 'TerminologyConceptCancellation', @LockMode = 'Exclusive', @LockOwner = 'Transaction';
                WAITFOR DELAY '00:00:30';
            END
            """);
        using var cancellation = new CancellationTokenSource();
        using var waitLimit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var import = Database.CreateSqlServerImporter().ImportCodeSystemAsync(1, package, cancellation.Token);
        try
        {
            while (await Database.ExecuteScalarAsync<int>(
                "SELECT APPLOCK_TEST('public', 'TerminologyConceptCancellation', 'Shared', 'Session')", waitLimit.Token) != 0)
            {
                await Task.Delay(20, waitLimit.Token);
            }
            await cancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => import);
        }
        finally
        {
            await cancellation.CancelAsync();
            try
            {
                await import;
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the asserted outcome; await connection cleanup before removing the trigger.
            }
            await Database.ExecuteNonQueryAsync("DROP TRIGGER dbo.TerminologyPauseConcepts");
        }

        (await HashAsync(package)).ShouldBe(originalHash);
        (await StatusAsync(package)).ShouldBe("Completed");
        await AssertUsableAsync("CodeSystem", canonical, "Car");
    }

    [Fact]
    public async Task GivenUsableTerminology_WhenReplacementIsCancelled_ThenCancellationPropagatesWithoutRecordingFailure()
    {
        var canonical = $"http://terminology-contract.example/CodeSystem/{Guid.NewGuid():N}";
        var package = await StoreAsync("CodeSystem", canonical, Json("CodeSystem", canonical));
        (await ImportAsync(package)).Success.ShouldBeTrue();
        var originalHash = await HashAsync(package);
        package.ResourceJson = package.ResourceJson.Replace("Car", "Replacement", StringComparison.Ordinal);
        var importer = new SqlServerCodeSystemImporter(
            Database.SqlExecutionService, 0, new CancelledSystemRepository(),
            NullLogger<SqlServerCodeSystemImporter>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(
            () => importer.ImportCodeSystemAsync(1, package, CancellationToken.None));

        (await HashAsync(package)).ShouldBe(originalHash);
        (await StatusAsync(package)).ShouldBe("Completed");
        await AssertUsableAsync("CodeSystem", canonical, "Car");
    }

    [Theory]
    [InlineData("CodeSystem", false)]
    [InlineData("ValueSet", false)]
    [InlineData("ConceptMap", false)]
    [InlineData("CodeSystem", true)]
    [InlineData("ValueSet", true)]
    [InlineData("ConceptMap", true)]
    public async Task GivenANewerPackageWithTheSameCanonicalVersion_WhenImported_ThenItReplacesContentAndRetainsRetryHistory(
        string resourceType, bool unversioned)
    {
        var canonical = $"http://terminology-contract.example/{resourceType}/{Guid.NewGuid():N}";
        var json = Json(resourceType, canonical);
        if (unversioned)
        {
            json = json.Replace("\"version\":\"1.0.0\",", "", StringComparison.Ordinal);
        }
        var original = await StoreAsync(resourceType, canonical, json);
        (await ImportAsync(original)).Success.ShouldBeTrue();

        var replacement = await StoreAsync(
            resourceType, canonical, json.Replace("Car", "Replacement", StringComparison.Ordinal),
            original.PackageId, "2.0.0");
        replacement.PackageResourceId.ShouldNotBe(original.PackageResourceId);

        var result = await ImportAsync(replacement);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        var table = $"Term{resourceType}";
        (await Database.ExecuteScalarAsync<long>(
            $"SELECT PackageResourceId FROM dbo.{table} WHERE " +
            (resourceType == "CodeSystem"
                ? $"SystemId = (SELECT SystemId FROM dbo.System WHERE Value = '{canonical}')"
                : $"Canonical = '{canonical}'"))).ShouldBe(replacement.PackageResourceId);
        (await HashAsync(replacement)).ShouldBe(replacement.ComputeContentHash());
        (await ImportAsync(original)).ItemCount.ShouldBe(0);
        (await ImportAsync(replacement)).ItemCount.ShouldBe(0);
        await AssertUsableAsync(resourceType, canonical, "Replacement", unversioned ? null : "1.0.0");
    }

    [Theory]
    [InlineData("ValueSet")]
    [InlineData("ConceptMap")]
    public async Task GivenOptionalNameIsAbsent_WhenImported_ThenItStoresNullAndRemainsUsable(string resourceType)
    {
        var canonical = $"http://terminology-contract.example/{resourceType}/{Guid.NewGuid():N}";
        var json = Json(resourceType, canonical)
            .Replace("\"name\":\"TerminologyTestValueSet\",", "", StringComparison.Ordinal)
            .Replace("\"name\":\"TerminologyTestConceptMap\",", "", StringComparison.Ordinal);
        var package = await StoreAsync(resourceType, canonical, json);

        var result = await ImportAsync(package);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        (await Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Term{resourceType} WHERE PackageResourceId = {package.PackageResourceId} AND Name IS NULL")).ShouldBe(1);
        await AssertUsableAsync(resourceType, canonical, "Car");
    }

    [Fact]
    public async Task GivenUsableCodeSystem_WhenReplacementFailsAfterDeletion_ThenOldConceptsHierarchyAndHashSurvive()
    {
        var canonical = $"http://terminology-contract.example/CodeSystem/{Guid.NewGuid():N}";
        var package = await StoreAsync("CodeSystem", canonical, Json("CodeSystem", canonical));
        (await ImportAsync(package)).Success.ShouldBeTrue();
        var originalHash = await HashAsync(package);
        package.ResourceJson = package.ResourceJson.Replace("\"code\":\"truck\"", "\"code\":\"car\"", StringComparison.Ordinal);

        var result = await ImportAsync(package);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("duplicate");
        (await StatusAsync(package)).ShouldBe("Failed");
        (await HashAsync(package)).ShouldBe(originalHash);
        await AssertUsableAsync("CodeSystem", canonical, "Car");
    }

    [Theory]
    [InlineData("CodeSystem")]
    [InlineData("ValueSet")]
    [InlineData("ConceptMap")]
    public async Task GivenUsableTerminology_WhenHashWriteFails_ThenReplacementIsRolledBackAndRetryCanSucceed(string resourceType)
    {
        var canonical = $"http://terminology-contract.example/{resourceType}/{Guid.NewGuid():N}";
        var package = await StoreAsync(resourceType, canonical, Json(resourceType, canonical));
        (await ImportAsync(package)).Success.ShouldBeTrue();
        var originalHash = await HashAsync(package);
        package.ResourceJson = package.ResourceJson.Replace("Car", "Replacement", StringComparison.Ordinal);
        await Database.ExecuteNonQueryAsync($"""
            CREATE TRIGGER dbo.TerminologyRejectHash ON dbo.PackageResource AFTER UPDATE AS
            BEGIN
                IF UPDATE(ContentHash) AND EXISTS (SELECT 1 FROM inserted WHERE PackageResourceId = {package.PackageResourceId})
                    THROW 51004, 'Injected terminology hash write failure', 1;
            END
            """);
        try
        {
            var result = await ImportAsync(package);
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldNotBeNull();
            result.ErrorMessage.ShouldContain("Injected terminology hash write failure");
            (await StatusAsync(package)).ShouldBe("Failed");
            (await HashAsync(package)).ShouldBe(originalHash);
            await AssertUsableAsync(resourceType, canonical, "Car");
        }
        finally
        {
            await Database.ExecuteNonQueryAsync("DROP TRIGGER dbo.TerminologyRejectHash");
        }

        (await ImportAsync(package)).Success.ShouldBeTrue();
        (await HashAsync(package)).ShouldBe(package.ComputeContentHash());
        await AssertUsableAsync(resourceType, canonical, "Replacement");
    }

    private async Task<PackageResource> StoreAsync(
        string resourceType, string canonical, string json, string? packageId = null, string packageVersion = "1.0.0")
    {
        var repository = new SqlServerPackageResourceRepository(
            Database.SqlExecutionService, 1, NullLogger<SqlServerPackageResourceRepository>.Instance);
        packageId ??= $"terminology.contract.{Guid.NewGuid():N}";
        await repository.UpsertAsync(new PackageResource
        {
            PackageId = packageId,
            PackageVersion = packageVersion,
            ResourceType = resourceType,
            Canonical = canonical,
            Version = "1.0.0",
            ResourceId = canonical.Split('/')[^1],
            ResourceJson = json,
            FhirVersion = "4.0.1",
            IsActive = true,
        }, CancellationToken.None);
        return (await repository.GetFromPackageAsync(packageId, packageVersion, canonical))!;
    }

    private Task<TerminologyImportResult> ImportAsync(PackageResource package)
    {
        var importer = Database.CreateSqlServerImporter();
        return package.ResourceType switch
        {
            "CodeSystem" => importer.ImportCodeSystemAsync(1, package, CancellationToken.None),
            "ValueSet" => importer.ImportValueSetAsync(1, package, CancellationToken.None),
            "ConceptMap" => importer.ImportConceptMapAsync(1, package, CancellationToken.None),
            _ => throw new InvalidOperationException(),
        };
    }

    private Task<string> HashAsync(PackageResource package) => Database.ExecuteScalarAsync<string>(
        $"SELECT ContentHash FROM dbo.PackageResource WHERE PackageResourceId = {package.PackageResourceId}");

    private Task<string> StatusAsync(PackageResource package) => Database.ExecuteScalarAsync<string>(
        $"SELECT TerminologyImportStatus FROM dbo.PackageResource WHERE PackageResourceId = {package.PackageResourceId}");

    private async Task AssertUsableAsync(string resourceType, string canonical, string display, string? version = "1.0.0")
    {
        var service = Database.CreateTerminologyService();
        if (resourceType == "CodeSystem")
        {
            var lookup = await service.LookupCodeAsync(canonical, "car", version, CancellationToken.None);
            lookup.Found.ShouldBeTrue();
            lookup.Display.ShouldBe(display);
            var subsumes = await service.SubsumesAsync(
                new SubsumesParameters("vehicle", "car", canonical, version), CancellationToken.None);
            subsumes.Outcome.ShouldBe("subsumes");
            (await service.LookupCodeAsync(canonical, "CAR", version, CancellationToken.None)).Found.ShouldBeFalse();
        }
        else if (resourceType == "ValueSet")
        {
            var expansion = await service.ExpandValueSetAsync(new ExpansionParameters(canonical), CancellationToken.None);
            expansion.ShouldNotBeNull();
            expansion.Contains.ShouldContain(entry => entry.Code == "car" && entry.Display == display);
        }
        else
        {
            var translated = await service.TranslateCodeAsync(
                new TranslateParameters(canonical, null, "car", "http://terminology-contract.example/source", null, null, null, null),
                CancellationToken.None);
            translated.Result.ShouldBeTrue();
            translated.Matches.ShouldContain(match => match.Concept.Code == "auto");
            (await Database.ExecuteScalarAsync<string>(
                "SELECT SourceDisplay FROM dbo.TermConceptMapElement e JOIN dbo.TermConceptMap m " +
                $"ON m.TermConceptMapId = e.TermConceptMapId WHERE m.Canonical = '{canonical}'")).ShouldBe(display);
        }
        (await ((ITerminologyImportStatusProvider)service).GetImportStatusAsync(canonical, CancellationToken.None))
            .ShouldBe(TerminologyImportStatus.Completed);
    }

    private static string Json(string resourceType, string canonical) => resourceType switch
    {
        "CodeSystem" => TerminologyTestFixture.HierarchicalCodeSystemJson(canonical),
        "ValueSet" => TerminologyTestFixture.ExpandedValueSetJson(canonical, "http://terminology-contract.example/source", "car")
            .Replace("Display car", "Car", StringComparison.Ordinal),
        "ConceptMap" => TerminologyTestFixture.ConceptMapJson(canonical, "http://terminology-contract.example/source", "http://terminology-contract.example/target"),
        _ => throw new InvalidOperationException(),
    };

    private sealed class CancelledSystemRepository : ISystemRepository
    {
        public Task<int> GetOrCreateAsync(string systemUri, CancellationToken cancellationToken)
            => throw new OperationCanceledException("Canceled terminology system resolution");

        public Task<int?> GetSystemIdAsync(string systemUri, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
