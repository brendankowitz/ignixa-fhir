using System.Data;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.Tests.Features.Terminology;

/// <summary>
/// Pins that a CodeSystem, ValueSet expansion or ConceptMap which loses malformed entries on import is
/// reported and persisted as <see cref="TerminologyImportStatus.PartiallyCompleted"/> rather than as a clean
/// <see cref="TerminologyImportStatus.Completed"/>. The import procedures always write <c>Completed</c>, so
/// the importer must follow a lossy import with its own status UPDATE. No real database is involved: a
/// recording <see cref="ISqlExecutionService"/> captures every command instead of executing it.
/// </summary>
public class SqlServerCodeSystemImporterPartialImportTests
{
    private const int PartitionId = 0;
    private const long PackageResourceId = 42;
    private const string ExampleSystem = "http://example.org/fhir/CodeSystem/example";

    [Fact]
    public async Task GivenConceptsWithoutACode_WhenACodeSystemIsImported_ThenTheResultAndPackageRowArePartiallyCompleted()
    {
        // Arrange: the code-less concept carries three descendants, which are dropped with it.
        const string json = $$"""
            {
                "resourceType": "CodeSystem",
                "url": "{{ExampleSystem}}",
                "content": "complete",
                "concept": [
                    { "code": "a" },
                    {
                        "display": "no code",
                        "concept": [ { "code": "b" }, { "code": "c", "concept": [ { "code": "d" } ] } ]
                    }
                ]
            }
            """;
        var sql = new RecordingSqlExecutionService();

        // Act
        var result = await CreateImporter(sql).ImportCodeSystemAsync(
            PartitionId, CreatePackageResource("CodeSystem", json), CancellationToken.None);

        // Assert
        result.Status.ShouldBe(TerminologyImportStatus.PartiallyCompleted);
        result.Success.ShouldBeTrue();
        result.ItemCount.ShouldBe(1);
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldStartWith("4 concept(s) not imported");

        TableParameter(sql.ProcedureCall("dbo.ImportTermCodeSystem"), "@Concepts").Rows.Count.ShouldBe(1);
        sql.ShouldHaveRecordedPartiallyCompletedAfter("dbo.ImportTermCodeSystem", result.ErrorMessage);
    }

    [Fact]
    public async Task GivenAWellFormedCodeSystem_WhenImported_ThenItIsCompletedWithoutAStatusUpdate()
    {
        const string json = $$"""
            {
                "resourceType": "CodeSystem",
                "url": "{{ExampleSystem}}",
                "content": "complete",
                "concept": [ { "code": "a", "concept": [ { "code": "b" } ] } ]
            }
            """;
        var sql = new RecordingSqlExecutionService();

        var result = await CreateImporter(sql).ImportCodeSystemAsync(
            PartitionId, CreatePackageResource("CodeSystem", json), CancellationToken.None);

        result.Status.ShouldBe(TerminologyImportStatus.Completed);
        result.ItemCount.ShouldBe(2);
        result.ErrorMessage.ShouldBeNull();
        sql.NonQueries.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAnExpansionEntryWithACodeButNoSystem_WhenAValueSetIsImported_ThenTheResultAndPackageRowArePartiallyCompleted()
    {
        const string json = $$"""
            {
                "resourceType": "ValueSet",
                "url": "http://example.org/fhir/ValueSet/example",
                "expansion": {
                    "contains": [
                        { "system": "{{ExampleSystem}}", "code": "a" },
                        { "code": "orphan" },
                        { "display": "grouper", "contains": [ { "system": "{{ExampleSystem}}", "code": "b" } ] }
                    ]
                }
            }
            """;
        var sql = new RecordingSqlExecutionService();

        var result = await CreateImporter(sql).ImportValueSetAsync(
            PartitionId, CreatePackageResource("ValueSet", json), CancellationToken.None);

        result.Status.ShouldBe(TerminologyImportStatus.PartiallyCompleted);
        result.Success.ShouldBeTrue();
        result.ItemCount.ShouldBe(2);
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldStartWith("1 expansion entr(y/ies) not imported");

        var procedure = sql.ProcedureCall("dbo.ImportTermValueSet");
        TableParameter(procedure, "@Entries").Rows.Count.ShouldBe(2);
        procedure.Parameters["@IsPartialExpansion"].Value.ShouldBe(false);
        sql.ShouldHaveRecordedPartiallyCompletedAfter("dbo.ImportTermValueSet", result.ErrorMessage);
    }

    /// <summary>
    /// A <c>contains</c> entry without a code is a legal grouper: it carries no code, so skipping it loses
    /// nothing and must not mark a hierarchical expansion partial.
    /// </summary>
    [Fact]
    public async Task GivenAnExpansionWithACodelessGrouper_WhenAValueSetIsImported_ThenItIsCompletedWithoutAStatusUpdate()
    {
        const string json = $$"""
            {
                "resourceType": "ValueSet",
                "url": "http://example.org/fhir/ValueSet/example",
                "expansion": {
                    "contains": [
                        { "abstract": true, "display": "grouper", "contains": [ { "system": "{{ExampleSystem}}", "code": "b" } ] }
                    ]
                }
            }
            """;
        var sql = new RecordingSqlExecutionService();

        var result = await CreateImporter(sql).ImportValueSetAsync(
            PartitionId, CreatePackageResource("ValueSet", json), CancellationToken.None);

        result.Status.ShouldBe(TerminologyImportStatus.Completed);
        result.ItemCount.ShouldBe(1);
        sql.NonQueries.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAConceptMapElementWithoutASourceCode_WhenImported_ThenTheResultAndPackageRowArePartiallyCompleted()
    {
        const string json = $$"""
            {
                "resourceType": "ConceptMap",
                "url": "http://example.org/fhir/ConceptMap/example",
                "group": [
                    {
                        "source": "{{ExampleSystem}}",
                        "target": "http://example.org/fhir/CodeSystem/target",
                        "element": [
                            { "code": "a", "target": [ { "code": "x", "equivalence": "equivalent" } ] },
                            { "display": "no code", "target": [ { "code": "y", "equivalence": "equivalent" } ] }
                        ]
                    }
                ]
            }
            """;
        var sql = new RecordingSqlExecutionService();

        var result = await CreateImporter(sql).ImportConceptMapAsync(
            PartitionId, CreatePackageResource("ConceptMap", json), CancellationToken.None);

        result.Status.ShouldBe(TerminologyImportStatus.PartiallyCompleted);
        result.Success.ShouldBeTrue();
        result.ItemCount.ShouldBe(1);
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldStartWith("1 ConceptMap element(s) not imported");

        TableParameter(sql.ProcedureCall("dbo.ImportTermConceptMap"), "@Elements").Rows.Count.ShouldBe(1);
        sql.ShouldHaveRecordedPartiallyCompletedAfter("dbo.ImportTermConceptMap", result.ErrorMessage);
    }

    [Fact]
    public async Task GivenAWellFormedConceptMap_WhenImported_ThenItIsCompletedWithoutAStatusUpdate()
    {
        const string json = $$"""
            {
                "resourceType": "ConceptMap",
                "url": "http://example.org/fhir/ConceptMap/example",
                "group": [
                    { "source": "{{ExampleSystem}}", "element": [ { "code": "a" } ] }
                ]
            }
            """;
        var sql = new RecordingSqlExecutionService();

        var result = await CreateImporter(sql).ImportConceptMapAsync(
            PartitionId, CreatePackageResource("ConceptMap", json), CancellationToken.None);

        result.Status.ShouldBe(TerminologyImportStatus.Completed);
        result.ItemCount.ShouldBe(1);
        sql.NonQueries.ShouldBeEmpty();
    }

    /// <summary>
    /// <c>PartiallyCompleted</c> is terminal: unchanged content drops the same entries again, so re-importing
    /// it on every package load would be repeated work for an identical outcome.
    /// </summary>
    [Fact]
    public async Task GivenUnchangedContentAlreadyPartiallyCompleted_WhenImportedAgain_ThenNothingIsWrittenAndTheStatusIsRetained()
    {
        const string json = $$"""
            { "resourceType": "CodeSystem", "url": "{{ExampleSystem}}", "content": "complete", "concept": [ {} ] }
            """;
        var packageResource = CreatePackageResource("CodeSystem", json);
        var sql = new RecordingSqlExecutionService
        {
            StoredContentHash = packageResource.ComputeContentHash(),
            StoredStatus = nameof(TerminologyImportStatus.PartiallyCompleted),
        };

        var result = await CreateImporter(sql).ImportCodeSystemAsync(PartitionId, packageResource, CancellationToken.None);

        result.Status.ShouldBe(TerminologyImportStatus.PartiallyCompleted);
        result.ItemCount.ShouldBe(0);
        sql.Procedures.ShouldBeEmpty();
        sql.NonQueries.ShouldBeEmpty();
    }

    /// <summary>
    /// If the follow-up UPDATE fails after the procedure committed, the import is reported and recorded as
    /// Failed — which is retried — rather than leaving the procedure's clean <c>Completed</c> in place.
    /// </summary>
    [Fact]
    public async Task GivenThePartiallyCompletedUpdateFails_WhenACodeSystemIsImported_ThenTheImportIsRecordedAsFailed()
    {
        const string json = $$"""
            { "resourceType": "CodeSystem", "url": "{{ExampleSystem}}", "content": "complete", "concept": [ { "code": "a" }, {} ] }
            """;
        var sql = new RecordingSqlExecutionService { FailPartiallyCompletedUpdate = true };

        var result = await CreateImporter(sql).ImportCodeSystemAsync(
            PartitionId, CreatePackageResource("CodeSystem", json), CancellationToken.None);

        result.Status.ShouldBe(TerminologyImportStatus.Failed);
        result.Success.ShouldBeFalse();
        sql.NonQueries.Count.ShouldBe(2);
        sql.NonQueries[1].CommandText.ShouldContain("= 'Failed'");
    }

    private static SqlServerCodeSystemImporter CreateImporter(ISqlExecutionService sql) =>
        new(
            sql,
            PartitionId,
            new FixedSystemRepository(),
            NullLogger<SqlServerCodeSystemImporter>.Instance,
            // Same partition for packages and terminology, so the shared-database guard short-circuits.
            packageTenantId: PartitionId);

    private static PackageResource CreatePackageResource(string resourceType, string json) => new()
    {
        PackageResourceId = PackageResourceId,
        PackageId = "test.package",
        PackageVersion = "1.0.0",
        ResourceType = resourceType,
        Canonical = $"http://example.org/fhir/{resourceType}/example",
        ResourceId = "example",
        ResourceJson = json,
        FhirVersion = "4.0.1",
        IsActive = true,
    };

    private static DataTable TableParameter(SqlCommand command, string name)
        => command.Parameters[name].Value.ShouldBeOfType<DataTable>();

    private sealed class RecordingSqlExecutionService : ISqlExecutionService
    {
        private readonly List<SqlCommand> _commands = [];

        public string? StoredContentHash { get; init; }
        public string? StoredStatus { get; init; }
        public bool FailPartiallyCompletedUpdate { get; init; }

        public IReadOnlyList<SqlCommand> Procedures =>
            [.. _commands.Where(c => c.CommandType == CommandType.StoredProcedure)];

        public IReadOnlyList<SqlCommand> NonQueries =>
            [.. _commands.Where(c => c.CommandText.StartsWith("UPDATE", StringComparison.Ordinal))];

        public SqlCommand ProcedureCall(string name)
        {
            var procedure = Procedures.ShouldHaveSingleItem();
            procedure.CommandText.ShouldBe(name);
            return procedure;
        }

        public void ShouldHaveRecordedPartiallyCompletedAfter(string procedureName, string expectedReason)
        {
            var update = NonQueries.ShouldHaveSingleItem();
            _commands.IndexOf(update).ShouldBeGreaterThan(_commands.IndexOf(ProcedureCall(procedureName)));
            update.CommandText.ShouldContain("= 'PartiallyCompleted'");
            update.CommandText.ShouldContain("ImportErrorMessage");
            update.Parameters["@reason"].Value.ShouldBe(expectedReason);
            update.Parameters["@packageResourceId"].Value.ShouldBe(PackageResourceId);
        }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            tenantId.ShouldBe(PartitionId);

            if (typeof(TResult) == typeof((string? ContentHash, string? Status)))
            {
                return Task.FromResult<IReadOnlyList<TResult>>(
                    [(TResult)(object)(StoredContentHash, StoredStatus)]);
            }

            if (typeof(TResult) == typeof(long) && command.CommandType == CommandType.StoredProcedure)
            {
                _commands.Add(command);
                return Task.FromResult<IReadOnlyList<TResult>>([(TResult)(object)1L]);
            }

            throw new NotSupportedException(
                $"No canned response for TResult={typeof(TResult)} (command: {command.CommandText}).");
        }

        public Task<int> ExecuteNonQueryAsync(
            int tenantId,
            SqlCommand command,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            tenantId.ShouldBe(PartitionId);
            _commands.Add(command);

            if (FailPartiallyCompletedUpdate && command.CommandText.Contains("'PartiallyCompleted'", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Simulated failure of the PartiallyCompleted status update.");
            }

            return Task.FromResult(1);
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("The shared-database guard should short-circuit in these tests.");

        public Task ExecuteInTransactionAsync(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("The shared-database guard should short-circuit in these tests.");
    }

    private sealed class FixedSystemRepository : ISystemRepository
    {
        public Task<int> GetOrCreateAsync(string systemUri, CancellationToken cancellationToken)
            => Task.FromResult(1);

        public Task<int?> GetSystemIdAsync(string systemUri, CancellationToken cancellationToken)
            => Task.FromResult<int?>(1);
    }
}
