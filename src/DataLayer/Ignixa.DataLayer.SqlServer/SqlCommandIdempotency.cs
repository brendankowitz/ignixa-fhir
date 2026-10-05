namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Whether a command is safe for the transient-fault retry pipeline to execute more than once.
/// <para>
/// A transient failure -- most importantly a <c>-2</c> command timeout -- does not prove the server did not
/// already commit the statement. Retrying it therefore risks applying the write twice. Callers declare which
/// kind of command they are handing over; the execution service decides what to do about it.
/// </para>
/// </summary>
public enum SqlCommandIdempotency
{
    /// <summary>
    /// Executing the command a second time produces the same end state as executing it once (a SELECT, a
    /// keyed UPSERT, a DELETE by primary key, an INSERT guarded by an idempotency key). Transient faults are
    /// retried. This is the default because it is what every call site did before the option existed.
    /// </summary>
    Idempotent = 0,

    /// <summary>
    /// Executing the command a second time would apply its effect twice -- an unguarded
    /// <c>INSERT ... OUTPUT INSERTED</c> being the common case, since it needs the generated identity back
    /// and so cannot be made idempotent by a key the caller already holds. Transient faults propagate on the
    /// first attempt instead of being retried.
    /// </summary>
    NonIdempotent = 1,
}
