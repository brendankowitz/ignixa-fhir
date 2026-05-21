namespace Ignixa.TestScript.Model;

public abstract record VariableExtraction;

public sealed record ExpressionExtraction(string Expression) : VariableExtraction;
public sealed record PathExtraction(string Path) : VariableExtraction;
public sealed record HeaderExtraction(string Field) : VariableExtraction;
