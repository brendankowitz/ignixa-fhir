/* Copyright (c) 2025, Ignixa Contributors */

using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.FhirMappingLanguage.Parser;

/// <summary>
/// Represents a compiled mapping ready for execution.
/// </summary>
public class CompiledMapping
{
    private readonly MapExpression _map;
    private readonly MappingEvaluator _evaluator;
    private readonly MappingContext _context;

    internal CompiledMapping(MapExpression map, MappingEvaluator evaluator, MappingContext context)
    {
        _map = map;
        _evaluator = evaluator;
        _context = context;
    }

    /// <summary>
    /// Gets the map URL.
    /// </summary>
    public string Url => _map.Url;

    /// <summary>
    /// Gets the map identifier.
    /// </summary>
    public string Identifier => _map.Identifier;

    /// <summary>
    /// Gets the groups defined in this mapping.
    /// </summary>
    public IReadOnlyList<string> Groups => _map.Groups.Select(g => g.Name).ToList();

    /// <summary>
    /// Executes the default group (first group) in the mapping.
    /// </summary>
    public void Execute()
    {
        _evaluator.Execute(_map, _context);
    }

    /// <summary>
    /// Executes a specific group by name.
    /// </summary>
    /// <param name="groupName">The name of the group to execute</param>
    public void ExecuteGroup(string groupName)
    {
        _evaluator.ExecuteGroup(_map, groupName, _context);
    }

    /// <summary>
    /// Gets the evaluation context (for setting sources/targets).
    /// </summary>
    public MappingContext Context => _context;
}
