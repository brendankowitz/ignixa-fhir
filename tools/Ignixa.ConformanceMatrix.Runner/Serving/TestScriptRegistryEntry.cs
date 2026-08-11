using Ignixa.TestScript.Model;

namespace Ignixa.ConformanceMatrix.Runner.Serving;

/// <summary>
/// One TestScript discovered under <c>--tests</c>. <see cref="Definition"/> is <see langword="null"/>
/// and <see cref="ParseError"/> is set when the file failed to parse — the entry stays in the
/// registry either way so <c>/testscripts</c> can list it and <c>/run</c> can report a 422 instead
/// of a 404 for a script that exists on disk but never parsed.
/// </summary>
internal sealed record TestScriptRegistryEntry(
    string Id,
    string Name,
    string RelativePath,
    TestScriptDefinition? Definition,
    string? ParseError);
