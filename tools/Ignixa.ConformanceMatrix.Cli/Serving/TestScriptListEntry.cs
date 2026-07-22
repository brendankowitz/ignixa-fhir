namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>Wire shape of one entry in <c>GET /testscripts</c>.</summary>
internal sealed record TestScriptListEntry(string Id, string Name, string File, bool Valid, string? Error);
