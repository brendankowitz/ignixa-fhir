using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Terminology;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

/// <summary>
/// Phase F Task 5b — the terminology oracle. These run against the <b>EF</b> implementation and exist to
/// capture its behaviour before the port, because nothing else does: there is no terminology test project
/// and no terminology test class anywhere in the repository, and the implementation stops existing when the
/// EF project is deleted.
/// <para>
/// They ship no production code, which makes them the tempting thing to skip. They are the only reason the
/// 2,645-line terminology port will be checkable against anything.
/// </para>
/// </summary>
public class TerminologyOracleImportTests : IAsyncLifetime
{
    private const string SystemUrl = "http://example.org/fhir/CodeSystem/oracle-vehicles";

    private TerminologyOracleFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyOracleFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<TerminologyImportResult> ImportHierarchyAsync(string url)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", url, TerminologyOracleFixture.HierarchicalCodeSystemJson(url));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            return await importer.ImportCodeSystemAsync(
                _fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }
    }

    [Fact]
    public async Task GivenAHierarchicalCodeSystem_WhenImported_ThenEveryConceptLandsButTheHierarchyIsNotPersisted()
    {
        // RECORDS A DEFECT. All four concepts arrive, but every ParentConceptId is null, so the nested
        // concept[] structure is lost.
        //
        // FlattenConcepts returns concepts with null parents plus a separate parent map, and only the bulk
        // path (taken above 1,000 concepts) applies that map via UpdateParentReferencesAsync. At or below
        // the threshold the importer calls AddRange and never runs the second pass. Since most custom and
        // IG CodeSystems are well under 1,000 concepts, their hierarchy is silently discarded on import --
        // which is what makes $subsumes wrong for them (see TerminologyOracleServiceTests).
        //
        // Phase F Task 6 fixes this as part of the port; these assertions flip then, in a commit containing
        // only that flip, so a later failure stays attributable to the fix rather than the port.
        await ImportHierarchyAsync(SystemUrl);

        var conceptCount = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept tc " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{SystemUrl}'", CancellationToken.None);

        conceptCount.ShouldBe(4);

        var childrenOfVehicle = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept child " +
            "JOIN dbo.TermConcept parent ON parent.TermConceptId = child.ParentConceptId " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = child.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{SystemUrl}' AND parent.Code = 'vehicle'", CancellationToken.None);

        // Correct answer is 2. Recorded as 0 because the second pass never ran.
        childrenOfVehicle.ShouldBe(0);

        var withoutParent = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept tc " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{SystemUrl}' AND tc.ParentConceptId IS NULL", CancellationToken.None);

        // Correct answer is 2 (vehicle and building). All four are parentless today.
        withoutParent.ShouldBe(4);
    }

    [Fact]
    public async Task GivenAHierarchicalCodeSystem_WhenImported_ThenConceptLevelsAreStillRecordedCorrectly()
    {
        // Level is computed during flattening and written on the insert, so it survives even though the
        // parent links do not. That asymmetry is worth pinning: the depth information needed to rebuild the
        // hierarchy is already in the table, which is why the fix is a second pass rather than a re-import.
        await ImportHierarchyAsync(SystemUrl);

        var depthOneConcepts = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept tc " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{SystemUrl}' AND tc.Level = 1", CancellationToken.None);

        depthOneConcepts.ShouldBe(2);
    }

    [Fact]
    public async Task GivenACodeSystem_WhenImported_ThenTheSystemRowIsCreatedOnce()
    {
        await ImportHierarchyAsync(SystemUrl);

        var systemRows = await _fixture.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.System WHERE Value = '{SystemUrl}'", CancellationToken.None);

        systemRows.ShouldBe(1);
    }

    [Fact]
    public async Task GivenACodeSystem_WhenImported_ThenTheResultReportsWhatItDid()
    {
        var result = await ImportHierarchyAsync(SystemUrl);

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenTwoCodeSystems_WhenBothImported_ThenTheirConceptsStaySeparate()
    {
        // Concepts are scoped by TermCodeSystemId, not by code. Two systems sharing a code must not merge,
        // which is the property SubsumesAsync's system filter depends on.
        const string otherUrl = "http://example.org/fhir/CodeSystem/oracle-other";

        await ImportHierarchyAsync(SystemUrl);
        await ImportHierarchyAsync(otherUrl);

        var perSystem = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept tc " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{otherUrl}'", CancellationToken.None);

        perSystem.ShouldBe(4);

        var totalVehicleCodes = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept WHERE Code = 'vehicle'", CancellationToken.None);

        totalVehicleCodes.ShouldBe(2);
    }

    [Fact]
    public async Task GivenANonCodeSystemResource_WhenImportedAsACodeSystem_ThenItIsRejected()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet",
            "http://example.org/fhir/ValueSet/wrong-type",
            "{\"resourceType\":\"ValueSet\"}");

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await Should.ThrowAsync<ArgumentException>(
                () => importer.ImportCodeSystemAsync(
                    _fixture.SystemPartitionId, packageResource, CancellationToken.None));
        }
    }

    [Fact]
    public async Task GivenAPackageResourceThatWasNeverPersisted_WhenImported_ThenItThrows()
    {
        // The importer resolves the row by PackageResourceId rather than trusting the model, so an id that
        // does not exist is an error rather than an insert. This is why every oracle test seeds first.
        var unsaved = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyOracleFixture.HierarchicalCodeSystemJson(SystemUrl));
        unsaved.PackageResourceId = 999_999_999;

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await Should.ThrowAsync<InvalidOperationException>(
                () => importer.ImportCodeSystemAsync(
                    _fixture.SystemPartitionId, unsaved, CancellationToken.None));
        }
    }
}
