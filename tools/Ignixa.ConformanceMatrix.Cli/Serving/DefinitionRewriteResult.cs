using Ignixa.TestScript.Model;

namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>Outcome of <see cref="DefinitionRewriter.Apply"/>: either a rewritten definition, or a validation error to surface as a 400.</summary>
internal sealed record DefinitionRewriteResult
{
    public TestScriptDefinition? Definition { get; private init; }
    public string? Error { get; private init; }

    public bool IsValid => Error is null;

    public static DefinitionRewriteResult Valid(TestScriptDefinition definition) => new() { Definition = definition };

    public static DefinitionRewriteResult Invalid(string error) => new() { Error = error };
}
