using Ignixa.TestScript.Expressions;

namespace Ignixa.TestScript.Model;

public sealed record TestPhaseDefinition
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<ActionExpression> Actions { get; init; } = [];
    public ParametrizeDefinition? Parameters { get; init; }
    public IReadOnlyList<string> FhirVersions { get; init; } = [];

    /// <summary>
    /// FHIRPath expression (from the <c>http://ignixa.io/testscript/requiresCapability</c>
    /// extension on this test) evaluated against the target's CapabilityStatement. When
    /// present and it evaluates to <c>false</c>, this test is skipped rather than executed —
    /// e.g. <c>"rest.resource.where(type='Patient').operation.where(name='everything').exists()"</c>.
    /// </summary>
    public string? RequiresCapability { get; init; }
}
