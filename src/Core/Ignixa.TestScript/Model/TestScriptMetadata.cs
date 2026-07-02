namespace Ignixa.TestScript.Model;

public sealed record TestScriptMetadata
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? Status { get; init; }
    public string? Version { get; init; }

    /// <summary>
    /// FHIRPath expression (from the <c>http://ignixa.io/testscript/requiresCapability</c>
    /// extension on the TestScript resource) evaluated against the target's CapabilityStatement.
    /// When present and it evaluates to <c>false</c>, every test in this script is skipped —
    /// use this to gate an entire suite on a server capability (e.g. a whole $reindex suite on
    /// a server that doesn't advertise the operation at all). Individual tests can additionally
    /// carry their own <see cref="TestPhaseDefinition.RequiresCapability"/> for finer-grained gating.
    /// </summary>
    public string? RequiresCapability { get; init; }
}
