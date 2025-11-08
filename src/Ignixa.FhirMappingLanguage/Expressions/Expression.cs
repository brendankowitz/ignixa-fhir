/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Expression tree for FHIR Mapping Language abstract syntax tree (AST).
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Base class for all FHIR Mapping Language expression nodes in the abstract syntax tree (AST).
/// </summary>
public abstract class Expression
{
    protected Expression()
    {
    }

    protected Expression(ISourcePositionInfo? location)
    {
        Location = location;
    }

    /// <summary>
    /// Location information for this expression component in the parsed mapping expression.
    /// </summary>
    public ISourcePositionInfo? Location { get; set; }

    /// <summary>
    /// Original source text for this expression (preserves whitespace and comments for round-tripping).
    /// Only populated when MappingCompiler is constructed with preserveTrivia = true.
    /// </summary>
    public string? SourceText { get; set; }

    /// <summary>
    /// Sets position information and returns this expression for fluent chaining.
    /// </summary>
    public T SetPosition<T>(ISourcePositionInfo location) where T : Expression
    {
        Location = location;
        return (T)this;
    }
}

/// <summary>
/// Source position information for expressions.
/// </summary>
public interface ISourcePositionInfo
{
    int LineNumber { get; }
    int LinePosition { get; }
    int RawPosition { get; }
    int Length { get; }
}

/// <summary>
/// Implementation of source position information.
/// </summary>
public class MappingExpressionLocationInfo : ISourcePositionInfo
{
    public int LineNumber { get; set; }
    public int LinePosition { get; set; }
    public int RawPosition { get; set; }
    public int Length { get; set; }
}

/// <summary>
/// Represents a literal value in a mapping expression.
/// Examples: 'hello', 42, 3.14, true
/// </summary>
public class LiteralExpression : Expression
{
    public LiteralExpression(object value, ISourcePositionInfo? location = null) : base(location)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public object Value { get; }

    public override string ToString() => $"Literal({Value})";
}

/// <summary>
/// Represents an identifier in a mapping expression.
/// Examples: patient, name, Bundle
/// </summary>
public class IdentifierExpression : Expression
{
    public IdentifierExpression(string name, ISourcePositionInfo? location = null) : base(location)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public override string ToString() => $"Identifier({Name})";
}

/// <summary>
/// Represents an embedded FHIRPath expression (surrounded by parentheses).
/// Example: (name.given.first())
/// </summary>
public class FhirPathExpression : Expression
{
    public FhirPathExpression(string expression, ISourcePositionInfo? location = null) : base(location)
    {
        PathExpression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    public string PathExpression { get; }

    public override string ToString() => $"FhirPath({PathExpression})";
}

/// <summary>
/// Represents the top-level map structure.
/// Example: map "http://example.org/fhir/StructureMap/Example" = "ExampleMap"
/// </summary>
public class MapExpression : Expression
{
    public MapExpression(
        string url,
        string identifier,
        IEnumerable<UsesExpression> uses,
        IEnumerable<ImportsExpression> imports,
        IEnumerable<GroupExpression> groups,
        ISourcePositionInfo? location = null) : base(location)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        Uses = uses?.ToList() ?? new List<UsesExpression>();
        Imports = imports?.ToList() ?? new List<ImportsExpression>();
        Groups = groups?.ToList() ?? new List<GroupExpression>();
    }

    public string Url { get; }
    public string Identifier { get; }
    public IReadOnlyList<UsesExpression> Uses { get; }
    public IReadOnlyList<ImportsExpression> Imports { get; }
    public IReadOnlyList<GroupExpression> Groups { get; }

    public override string ToString() => $"Map({Url} = {Identifier})";
}

/// <summary>
/// Represents a uses declaration.
/// Example: uses "http://hl7.org/fhir/StructureDefinition/Patient" alias Patient as source
/// </summary>
public class UsesExpression : Expression
{
    public UsesExpression(
        string url,
        string? alias,
        ModelMode mode,
        ISourcePositionInfo? location = null) : base(location)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        Alias = alias;
        Mode = mode;
    }

    public string Url { get; }
    public string? Alias { get; }
    public ModelMode Mode { get; }

    public override string ToString() => $"Uses({Url} as {Mode})";
}

/// <summary>
/// Model mode for uses declarations.
/// </summary>
public enum ModelMode
{
    Source,
    Queried,
    Target,
    Produced
}

/// <summary>
/// Represents an imports declaration.
/// Example: imports "http://example.org/fhir/StructureMap/Helpers"
/// </summary>
public class ImportsExpression : Expression
{
    public ImportsExpression(string url, ISourcePositionInfo? location = null) : base(location)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
    }

    public string Url { get; }

    public override string ToString() => $"Imports({Url})";
}

/// <summary>
/// Represents a transformation group.
/// Example: group PatientToBundle(source src : Patient, target bundle : Bundle)
/// </summary>
public class GroupExpression : Expression
{
    public GroupExpression(
        string name,
        IEnumerable<ParameterExpression> parameters,
        string? extends,
        IEnumerable<RuleExpression> rules,
        ISourcePositionInfo? location = null) : base(location)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Parameters = parameters?.ToList() ?? new List<ParameterExpression>();
        Extends = extends;
        Rules = rules?.ToList() ?? new List<RuleExpression>();
    }

    public string Name { get; }
    public IReadOnlyList<ParameterExpression> Parameters { get; }
    public string? Extends { get; }
    public IReadOnlyList<RuleExpression> Rules { get; }

    public override string ToString() => $"Group({Name})";
}

/// <summary>
/// Represents a parameter in a group definition.
/// Example: source src : Patient
/// </summary>
public class ParameterExpression : Expression
{
    public ParameterExpression(
        ParameterMode mode,
        string name,
        string? type,
        ISourcePositionInfo? location = null) : base(location)
    {
        Mode = mode;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
    }

    public ParameterMode Mode { get; }
    public string Name { get; }
    public string? Type { get; }

    public override string ToString() => $"Parameter({Mode} {Name}: {Type})";
}

/// <summary>
/// Parameter mode.
/// </summary>
public enum ParameterMode
{
    Source,
    Target
}

/// <summary>
/// Represents a transformation rule.
/// Example: src.name -> tgt.name
/// </summary>
public class RuleExpression : Expression
{
    public RuleExpression(
        string? name,
        IEnumerable<SourceExpression> sources,
        IEnumerable<TargetExpression> targets,
        Expression? dependent,
        ISourcePositionInfo? location = null) : base(location)
    {
        Name = name;
        Sources = sources?.ToList() ?? new List<SourceExpression>();
        Targets = targets?.ToList() ?? new List<TargetExpression>();
        Dependent = dependent;
    }

    public string? Name { get; }
    public IReadOnlyList<SourceExpression> Sources { get; }
    public IReadOnlyList<TargetExpression> Targets { get; }
    public Expression? Dependent { get; }

    public override string ToString() => $"Rule({Name ?? "anonymous"})";
}

/// <summary>
/// Represents cardinality constraints for source elements.
/// Example: 0..1, 1..*, 0..*
/// </summary>
public class Cardinality
{
    public Cardinality(int min, int? max)
    {
        if (min < 0)
        {
            throw new ArgumentException("Minimum cardinality cannot be negative", nameof(min));
        }

        if (max.HasValue && max.Value < min)
        {
            throw new ArgumentException("Maximum cardinality cannot be less than minimum", nameof(max));
        }

        Min = min;
        Max = max;
    }

    /// <summary>
    /// Minimum number of elements (inclusive).
    /// </summary>
    public int Min { get; }

    /// <summary>
    /// Maximum number of elements (inclusive). Null means unbounded (*).
    /// </summary>
    public int? Max { get; }

    /// <summary>
    /// Returns true if the given count satisfies this cardinality constraint.
    /// </summary>
    public bool IsSatisfiedBy(int count)
    {
        if (count < Min)
        {
            return false;
        }

        if (Max.HasValue && count > Max.Value)
        {
            return false;
        }

        return true;
    }

    public override string ToString() => Max.HasValue ? $"{Min}..{Max}" : $"{Min}..*";
}

/// <summary>
/// Represents a source element in a transformation rule.
/// Example: src.name as vn where name.exists()
/// </summary>
public class SourceExpression : Expression
{
    public SourceExpression(
        Expression context,
        string? variable,
        string? type,
        Expression? condition,
        Expression? check,
        Expression? log,
        Expression? defaultValue = null,
        Cardinality? cardinality = null,
        ISourcePositionInfo? location = null) : base(location)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Variable = variable;
        Type = type;
        Condition = condition;
        Check = check;
        Log = log;
        Default = defaultValue;
        Cardinality = cardinality;
    }

    public Expression Context { get; }
    public string? Variable { get; }
    public string? Type { get; }
    public Expression? Condition { get; }
    public Expression? Check { get; }
    public Expression? Log { get; }
    public Expression? Default { get; }
    public Cardinality? Cardinality { get; }

    public override string ToString() => $"Source({Context})";
}

/// <summary>
/// Represents a target element in a transformation rule.
/// Example: tgt.name = create('HumanName') or tgt.type = 'collection'
/// </summary>
public class TargetExpression : Expression
{
    public TargetExpression(
        Expression? context,
        string? variable,
        Expression? transform,
        ListMode? listMode,
        ISourcePositionInfo? location = null) : base(location)
    {
        Context = context;
        Variable = variable;
        Transform = transform;
        ListMode = listMode;
    }

    public Expression? Context { get; }
    public string? Variable { get; }
    public Expression? Transform { get; }
    public ListMode? ListMode { get; }

    public override string ToString() => $"Target({Context})";
}

/// <summary>
/// List mode for target elements.
/// </summary>
public enum ListMode
{
    First,
    NotFirst,
    Last,
    NotLast,
    OnlyOne,
    Share,
#pragma warning disable CA1720 // Identifier contains type name - 'Single' is a FHIR spec keyword for list modes
    Single
#pragma warning restore CA1720
}

/// <summary>
/// Represents a transform function call.
/// Example: create('HumanName'), translate(src, '#conceptMap', 'code')
/// </summary>
public class TransformExpression : Expression
{
    public TransformExpression(
        string functionName,
        IEnumerable<Expression> arguments,
        ISourcePositionInfo? location = null) : base(location)
    {
        FunctionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
        Arguments = arguments?.ToList() ?? new List<Expression>();
    }

    public string FunctionName { get; }
    public IReadOnlyList<Expression> Arguments { get; }

    public override string ToString() => $"Transform({FunctionName})";
}

/// <summary>
/// Represents a qualified identifier (e.g., context.property).
/// Example: src.name, bundle.entry
/// </summary>
public class QualifiedIdentifierExpression : Expression
{
    public QualifiedIdentifierExpression(
        Expression context,
        string property,
        ISourcePositionInfo? location = null) : base(location)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Property = property ?? throw new ArgumentNullException(nameof(property));
    }

    public Expression Context { get; }
    public string Property { get; }

    public override string ToString() => $"{Context}.{Property}";
}

/// <summary>
/// Represents an indexed access expression.
/// Example: src.name[0], src.identifier[1]
/// </summary>
public class IndexExpression : Expression
{
    public IndexExpression(
        Expression context,
        int index,
        ISourcePositionInfo? location = null) : base(location)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Index = index;
    }

    public Expression Context { get; }
    public int Index { get; }

    public override string ToString() => $"{Context}[{Index}]";
}

/// <summary>
/// Represents a group invocation in a dependent clause.
/// Example: then GroupName(src, tgt)
/// </summary>
public class GroupInvocationExpression : Expression
{
    public GroupInvocationExpression(
        string groupName,
        IEnumerable<Expression> arguments,
        ISourcePositionInfo? location = null) : base(location)
    {
        GroupName = groupName ?? throw new ArgumentNullException(nameof(groupName));
        Arguments = arguments?.ToList() ?? new List<Expression>();
    }

    public string GroupName { get; }
    public IReadOnlyList<Expression> Arguments { get; }

    public override string ToString() => $"{GroupName}({string.Join(", ", Arguments)})";
}

/// <summary>
/// Represents a set of nested rules in a dependent clause.
/// Example: then { rule1; rule2; }
/// </summary>
public class RuleSetExpression : Expression
{
    public RuleSetExpression(
        IEnumerable<RuleExpression> rules,
        ISourcePositionInfo? location = null) : base(location)
    {
        Rules = rules?.ToList() ?? new List<RuleExpression>();
    }

    public IReadOnlyList<RuleExpression> Rules { get; }

    public override string ToString() => $"{{ {Rules.Count} rules }}";
}
