namespace Ignixa.ConformanceMatrix.Cli.Commands;

/// <summary>Serialization shape for the <c>run</c> command's <c>--out</c> file.</summary>
internal enum ReportFormat
{
    /// <summary>A FHIR <c>Bundle</c> of <c>TestReport</c> resources, one per executed TestScript.</summary>
    Fhir,

    /// <summary>
    /// This tool's native per-impl report (<see cref="Reporting.ImplReport"/>) — the shape the
    /// <c>merge</c> command consumes to build the conformance matrix.
    /// </summary>
    Json
}
