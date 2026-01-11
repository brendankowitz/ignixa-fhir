// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Expressions;
using Ignixa.Specification;

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Registry of FhirPath function signatures for static validation and type inference.
/// </summary>
/// <remarks>
/// <para>
/// This is a partial class. The RegisterStandardFunctions() method is implemented by a source generator
/// that reads [FhirPathFunction] attributes from function implementations.
/// </para>
/// <para>
/// The SymbolTable provides:
/// - Function lookup by name
/// - Argument count validation
/// - Return type inference
/// - Context type checking
/// </para>
/// </remarks>
internal sealed partial class SymbolTable
{
    private readonly Dictionary<string, FunctionDefinition> _functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IFhirSchemaProvider? _schema;

    /// <summary>
    /// Creates a new SymbolTable with the standard FhirPath functions registered.
    /// </summary>
    public SymbolTable()
    {
        RegisterStandardFunctions();
    }

    /// <summary>
    /// Creates a new SymbolTable with schema-aware type resolution.
    /// </summary>
    /// <param name="schema">The FHIR schema provider for type resolution</param>
    public SymbolTable(IFhirSchemaProvider schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schema = schema;
        RegisterStandardFunctions();
    }

    /// <summary>
    /// Gets a function definition by name.
    /// </summary>
    /// <param name="name">The function name</param>
    /// <returns>The function definition, or null if not found</returns>
    public FunctionDefinition? Get(string name)
    {
        return _functions.TryGetValue(name, out var definition) ? definition : null;
    }

    /// <summary>
    /// Adds a function definition to the symbol table.
    /// </summary>
    /// <param name="definition">The function definition to add</param>
    /// <returns>The added definition for fluent chaining</returns>
    public FunctionDefinition Add(FunctionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _functions[definition.Name] = definition;
        return definition;
    }

    /// <summary>
    /// Gets all registered function names.
    /// </summary>
    public IEnumerable<string> FunctionNames => _functions.Keys;

    /// <summary>
    /// Gets the count of registered functions.
    /// </summary>
    public int FunctionCount => _functions.Count;

    /// <summary>
    /// Registers all standard FhirPath functions.
    /// This method is implemented by the source generator.
    /// </summary>
    partial void RegisterStandardFunctions();

    /// <summary>
    /// Creates a validation delegate that checks argument count.
    /// </summary>
    /// <param name="min">Minimum argument count (null for no minimum)</param>
    /// <param name="max">Maximum argument count (null for no maximum)</param>
    /// <returns>A validation delegate</returns>
    public static FunctionValidationDelegate ValidateArgumentCount(int? min, int? max)
    {
        return (function, definition, arguments, issues) =>
        {
            var argCount = arguments.Count();

            if (min.HasValue && argCount < min.Value)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationIssueSeverity.Error,
                    Message = $"Function '{definition.Name}' requires at least {min.Value} argument(s), got {argCount}",
                    Location = function.Location?.ToString(),
                    Expression = function.ToString()
                });
            }

            if (max.HasValue && argCount > max.Value)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationIssueSeverity.Error,
                    Message = $"Function '{definition.Name}' accepts at most {max.Value} argument(s), got {argCount}",
                    Location = function.Location?.ToString(),
                    Expression = function.ToString()
                });
            }
        };
    }

    /// <summary>
    /// Return type delegate that returns the focus type (used by where(), first(), etc.).
    /// </summary>
#pragma warning disable CA1002 // Do not expose generic lists
    public static List<FhirPathType> ReturnsContext(
        FunctionDefinition definition,
        FhirPathTypeSet focus,
        IEnumerable<FhirPathTypeSet> arguments,
        ICollection<ValidationIssue> issues)
    {
        return focus.Types.ToList();
    }
#pragma warning restore CA1002

    /// <summary>
    /// Return type delegate that returns types from the first argument (used by select()).
    /// </summary>
#pragma warning disable CA1002 // Do not expose generic lists
    public static List<FhirPathType> ReturnsFromArgument(
        FunctionDefinition definition,
        FhirPathTypeSet focus,
        IEnumerable<FhirPathTypeSet> arguments,
        ICollection<ValidationIssue> issues)
    {
        var argList = arguments.ToList();
        if (argList.Count == 0)
        {
            return focus.Types.ToList();
        }

        var resultTypes = argList[0].Types.ToList();

        if (focus.IsCollection())
        {
            return resultTypes.Select(t => t.AsCollection()).ToList();
        }

        return resultTypes;
    }
#pragma warning restore CA1002
}
