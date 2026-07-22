namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>
/// Performance-mode knobs for a <see cref="RunRequest"/>. <see cref="Assertions"/> is a string
/// rather than an enum because the contract also accepts <c>"status-only"</c>, a Phase 3 value
/// this runner explicitly rejects (see <see cref="DefinitionRewriter"/>) rather than silently
/// treating it as something it isn't.
/// </summary>
internal sealed record RunRequestOptions(
    bool RunSetup = true,
    bool RunTeardown = true,
    string Assertions = "full");
