namespace Ignixa.TestScript.Model;

public sealed record VariableDefinition
{
    public required string Name { get; init; }
    public string? DefaultValue { get; init; }
    public string? SourceId { get; init; }
    public string? Description { get; init; }
    public VariableExtraction? Extraction { get; init; }
}
