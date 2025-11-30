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
    /// Only populated when MappingParser is constructed with preserveTrivia = true.
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
