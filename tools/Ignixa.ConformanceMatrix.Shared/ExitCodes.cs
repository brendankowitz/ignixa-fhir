namespace Ignixa.ConformanceMatrix;

/// <summary>
/// Process exit codes shared by the ignixa-matrix and ignixa-matrix-runner tools. Compile-linked
/// into both projects so CI consumers see one contract: a usage error ("nothing ran because the
/// invocation was wrong") is distinguishable from an unexpected internal failure.
/// </summary>
internal static class ExitCodes
{
    internal const int UsageError = 2;
    internal const int InternalError = 3;
}
