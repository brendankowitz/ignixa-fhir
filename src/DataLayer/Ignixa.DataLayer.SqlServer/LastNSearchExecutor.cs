using System.Data;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

public sealed class LastNSearchExecutor : ILastNSearchExecutor
{
    private readonly ISqlExecutionService _executionService;

    public LastNSearchExecutor(ISqlExecutionService executionService)
    {
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
    }

    public async Task<IReadOnlyList<TResult>> ExecuteAsync<TResult>(
        int tenantId,
        CompiledSearch compiledSearch,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compiledSearch);
        if (compiledSearch.Query.EffectiveShape is not ResultShape.LastN)
        {
            throw new ArgumentException("The compiled search must have a LastN result shape.", nameof(compiledSearch));
        }

        // SQL text comes from SearchPlan compilation; user values remain emitted parameters.
#pragma warning disable CA2100
        using var command = new SqlCommand(compiledSearch.Sql);
#pragma warning restore CA2100
        foreach (EmittedSqlParameter emittedParameter in compiledSearch.Parameters)
        {
            command.Parameters.Add(CreateParameter(emittedParameter));
        }

        try
        {
            return await _executionService.ExecuteReaderAsync(
                tenantId,
                command,
                readRow,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 50403)
        {
            throw new LastNUnavailableException(
                "$lastn is unavailable while Observation code groups are not ready.",
                exception);
        }
    }

    private static SqlParameter CreateParameter(EmittedSqlParameter emittedParameter)
    {
        SqlParameter parameter = emittedParameter.Value switch
        {
            short value => new SqlParameter(emittedParameter.Name, SqlDbType.SmallInt) { Value = value },
            int value => new SqlParameter(emittedParameter.Name, SqlDbType.Int) { Value = value },
            long value => new SqlParameter(emittedParameter.Name, SqlDbType.BigInt) { Value = value },
            string value => new SqlParameter(emittedParameter.Name, SqlDbType.NVarChar, value.Length) { Value = value },
            DateTime value => new SqlParameter(emittedParameter.Name, SqlDbType.DateTime2) { Value = value },
            DateTimeOffset value => new SqlParameter(emittedParameter.Name, SqlDbType.DateTimeOffset) { Value = value },
            _ => throw new NotSupportedException(
                $"SQL parameter '{emittedParameter.Name}' has unsupported type '{emittedParameter.Value.GetType().Name}'."),
        };
        return parameter;
    }
}
