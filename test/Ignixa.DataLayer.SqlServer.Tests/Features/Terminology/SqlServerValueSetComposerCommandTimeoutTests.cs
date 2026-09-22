using System.Text.Json.Nodes;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests.Features.Terminology;

/// <summary>
/// Pins that <see cref="SqlServerValueSetComposer"/> runs its own reads with the same configurable
/// <see cref="SqlCommand.CommandTimeout"/> as the CodeSystem/ValueSet/ConceptMap import procedures, rather
/// than the ADO.NET default of 30 seconds. <c>ReadConceptsAsync</c> in particular runs BEFORE
/// <c>dbo.ImportTermValueSet</c> for any ValueSet carrying a <c>compose</c> element, and for a plain
/// <c>compose.include.system</c> with no <c>concept</c> or <c>filter</c> array it reads every concept in that
/// system with no row limit -- for a SNOMED-scale include, as many rows as the import itself writes. Left
/// unconfigured, that command alone could overrun 30 seconds and be retried into a spurious <c>Failed</c>
/// status independently of whether the importer's own timeout was ever raised.
/// <see cref="SqlServerValueSetComposer.ReadExpansionAsync"/> carries the same risk for a
/// <c>compose.include.valueSet</c> reference to a previously expanded, SNOMED-scale ValueSet. No real
/// database is involved: a fake <see cref="ISqlExecutionService"/> captures the <see cref="SqlCommand"/> each
/// method builds instead of executing it.
/// </summary>
public class SqlServerValueSetComposerCommandTimeoutTests
{
    private const int TestSystemPartitionId = 0;
    private const string SystemUrl = "http://example.org/fhir/CodeSystem/timeout-test";
    private const string ValueSetCanonical = "http://example.org/fhir/ValueSet/timeout-test";

    [Fact]
    public async Task GivenAWholeSystemInclude_WhenComposed_ThenReadConceptsUsesTheConfiguredTimeout()
    {
        var sqlExecutionService = new CommandCapturingSqlExecutionService();
        var compose = ComposeWithWholeSystemInclude();

        await SqlServerValueSetComposer.ComposeAsync(
            compose,
            sqlExecutionService,
            TestSystemPartitionId,
            new FixedSystemRepository(systemId: 1),
            NullLogger.Instance,
            commandTimeoutSeconds: 7,
            CancellationToken.None);

        sqlExecutionService.ReadConceptsCommand.ShouldNotBeNull();
        sqlExecutionService.ReadConceptsCommand.CommandTimeout.ShouldBe(7);
    }

    [Fact]
    public async Task GivenAValueSetInclude_WhenComposed_ThenReadExpansionUsesTheConfiguredTimeout()
    {
        var sqlExecutionService = new CommandCapturingSqlExecutionService();
        var compose = ComposeWithValueSetInclude();

        await SqlServerValueSetComposer.ComposeAsync(
            compose,
            sqlExecutionService,
            TestSystemPartitionId,
            new FixedSystemRepository(systemId: 1),
            NullLogger.Instance,
            commandTimeoutSeconds: 11,
            CancellationToken.None);

        sqlExecutionService.ReadExpansionCommand.ShouldNotBeNull();
        sqlExecutionService.ReadExpansionCommand.CommandTimeout.ShouldBe(11);
    }

    [Fact]
    public async Task GivenManyExplicitCodes_WhenComposed_ThenComparisonPolicyIsReadOnceWithoutReadingConcepts()
    {
        var sql = new CommandCapturingSqlExecutionService();
        var codes = Enumerable.Range(0, 500).SelectMany(index => new[] { $"CODE{index}", $"code{index}" });
        var compose = new JsonObject
        {
            ["include"] = new JsonArray(new JsonObject
            {
                ["system"] = SystemUrl, ["version"] = "V1",
                ["concept"] = new JsonArray(codes.Select(code => (JsonNode)new JsonObject { ["code"] = code }).ToArray()),
            }),
        };

        var result = await SqlServerValueSetComposer.ComposeAsync(
            compose, sql, TestSystemPartitionId, new FixedSystemRepository(1),
            NullLogger.Instance, 13, CancellationToken.None);

        result.Entries.Count.ShouldBe(500);
        result.IsPartial.ShouldBeFalse();
        sql.PolicyReadCount.ShouldBe(1);
        sql.PolicyCommand.ShouldNotBeNull();
        sql.PolicyCommand.CommandTimeout.ShouldBe(13);
        sql.ReadConceptsCommand.ShouldBeNull();
    }

    private static JsonObject ComposeWithWholeSystemInclude()
    {
        var compose = new JsonObject
        {
            ["include"] = new JsonArray(new JsonObject { ["system"] = SystemUrl }),
        };

        return compose;
    }

    private static JsonObject ComposeWithValueSetInclude()
    {
        var compose = new JsonObject
        {
            ["include"] = new JsonArray(new JsonObject
            {
                ["valueSet"] = new JsonArray(ValueSetCanonical),
            }),
        };

        return compose;
    }

    /// <summary>
    /// Answers the calls <see cref="SqlServerValueSetComposer"/> makes through
    /// <see cref="ISqlExecutionService.ExecuteReaderAsync{TResult}"/>, keyed off recognizable fragments of
    /// <see cref="SqlCommand.CommandText"/> rather than <c>TResult</c> -- the row types
    /// <c>ConceptCandidate</c> and <c>ValueSetExpansionRow</c> are not both reachable from this project, and
    /// the row-mapping delegate is never invoked because every canned response is empty.
    /// </summary>
    private sealed class CommandCapturingSqlExecutionService : ISqlExecutionService
    {
        public SqlCommand? ReadConceptsCommand { get; private set; }

        public SqlCommand? ReadExpansionCommand { get; private set; }
        public SqlCommand? PolicyCommand { get; private set; }
        public int PolicyReadCount { get; private set; }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            if (command.CommandText.Contains("FROM dbo.TermCodeSystem ", StringComparison.Ordinal))
            {
                PolicyCommand = command;
                PolicyReadCount++;
                return Task.FromResult<IReadOnlyList<TResult>>([(TResult)(object)(1, (string?)"V1", false)]);
            }
            if (command.CommandText.Contains("JOIN dbo.TermCodeSystem", StringComparison.Ordinal))
            {
                ReadConceptsCommand = command;
                return Task.FromResult<IReadOnlyList<TResult>>([]);
            }

            if (command.CommandText.Contains("FROM dbo.TermValueSetExpansion", StringComparison.Ordinal))
            {
                ReadExpansionCommand = command;
                return Task.FromResult<IReadOnlyList<TResult>>([]);
            }

            if (command.CommandText.Contains("FROM dbo.TermValueSet ", StringComparison.Ordinal))
            {
                // The resolved expansion metadata must be present for ReadExpansionAsync to run.
                return Task.FromResult<IReadOnlyList<TResult>>([(TResult)(object)(1L, false, (string?)null)]);
            }

            throw new NotSupportedException(
                $"This fixture has no canned response for command: {command.CommandText}");
        }

        public Task<int> ExecuteNonQueryAsync(
            int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
            => throw new NotSupportedException("SqlServerValueSetComposer does not use ExecuteNonQueryAsync.");

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("SqlServerValueSetComposer does not use ExecuteInTransactionAsync.");

        public Task ExecuteInTransactionAsync(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("SqlServerValueSetComposer does not use ExecuteInTransactionAsync.");
    }

    private sealed class FixedSystemRepository(int systemId) : ISystemRepository
    {
        public Task<int> GetOrCreateAsync(string systemUri, CancellationToken cancellationToken)
            => Task.FromResult(systemId);

        public Task<int?> GetSystemIdAsync(string systemUri, CancellationToken cancellationToken)
            => Task.FromResult<int?>(systemId);
    }
}
