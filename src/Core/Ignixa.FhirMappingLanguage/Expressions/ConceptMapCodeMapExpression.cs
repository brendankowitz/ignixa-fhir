/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a single code mapping within a ConceptMap.
/// Example: s:12345 == t:67890
/// </summary>
public class ConceptMapCodeMapExpression : Expression
{
    public ConceptMapCodeMapExpression(
        string sourcePrefix,
        string sourceCode,
        ConceptMapEquivalence equivalence,
        string targetPrefix,
        string targetCode,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(sourcePrefix);
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(targetPrefix);
        ArgumentNullException.ThrowIfNull(targetCode);

        SourcePrefix = sourcePrefix;
        SourceCode = sourceCode;
        Equivalence = equivalence;
        TargetPrefix = targetPrefix;
        TargetCode = targetCode;
    }

    public string SourcePrefix { get; }
    public string SourceCode { get; }
    public ConceptMapEquivalence Equivalence { get; }
    public string TargetPrefix { get; }
    public string TargetCode { get; }

    public override string ToString()
    {
        var equiv = Equivalence switch
        {
            ConceptMapEquivalence.Equivalent => "==",
            ConceptMapEquivalence.RelatedTo => "~=",
            ConceptMapEquivalence.NotRelatedTo => "!=",
            ConceptMapEquivalence.Broader => "<-",
            ConceptMapEquivalence.Narrower => "->",
            _ => "=="
        };
        return $"{SourcePrefix}:{SourceCode} {equiv} {TargetPrefix}:{TargetCode}";
    }
}
