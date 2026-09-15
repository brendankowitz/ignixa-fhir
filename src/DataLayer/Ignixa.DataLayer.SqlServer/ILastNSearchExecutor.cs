using Ignixa.Search.Sql;
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

public interface ILastNSearchExecutor
{
    Task<IReadOnlyList<TResult>> ExecuteAsync<TResult>(
        int tenantId,
        CompiledSearch compiledSearch,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken);
}
