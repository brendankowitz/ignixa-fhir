namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// A transaction's <c>COMMIT</c> failed, so whether the work was applied is unknown.
/// <para>
/// This is a distinct type, and deliberately not a <c>SqlException</c>, because that is what keeps the
/// transient-fault retry pipeline away from it. Everything before the commit is safely retryable -- the
/// rollback undoes it and the unit of work restarts from the top. A commit is not: the server may have
/// committed and lost the acknowledgement on the way back, so re-running the unit would apply it twice. The
/// only correct handling is to surface it and let a human or a higher-level reconciliation decide.
/// </para>
/// </summary>
public sealed class SqlTransactionCommitException : Exception
{
    public SqlTransactionCommitException()
    {
    }

    public SqlTransactionCommitException(string message)
        : base(message)
    {
    }

    public SqlTransactionCommitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SqlTransactionCommitException(int tenantId, Exception innerException)
        : base(
            $"Committing the transaction for tenant {tenantId} failed. Whether the work was applied is unknown: " +
            "the server may have committed it before the failure. This is not retried automatically -- re-running " +
            "the unit of work could apply it twice.",
            innerException)
        => TenantId = tenantId;

    /// <summary>Gets the tenant whose transaction failed to commit, or <c>null</c> when unknown.</summary>
    public int? TenantId { get; }
}
