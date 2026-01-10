// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Defines a FhirPath function with its signature, return type rules, and validation logic.
/// Used by static validators to check function signatures at compile time.
/// </summary>
public sealed class FunctionDefinition
{
    private readonly List<FunctionContext> _supportedContexts = new();
    private readonly List<FunctionValidationDelegate> _validations = new();

    /// <summary>
    /// Creates a new function definition.
    /// </summary>
    /// <param name="name">The function name (e.g., "where", "select")</param>
    /// <param name="supportsCollections">Whether function accepts collection inputs</param>
    /// <param name="supportedAtRoot">Whether function can be called without focus</param>
    public FunctionDefinition(string name, bool supportsCollections = false, bool supportedAtRoot = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        SupportsCollections = supportsCollections;
        SupportedAtRoot = supportedAtRoot;
    }

    /// <summary>
    /// Gets the function name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets whether this function supports collection inputs.
    /// </summary>
    public bool SupportsCollections { get; }

    /// <summary>
    /// Gets whether this function can be called at expression root.
    /// </summary>
    public bool SupportedAtRoot { get; }

    /// <summary>
    /// Gets whether this function takes expression arguments evaluated with focus as $this.
    /// </summary>
    public bool TakesExpressionArguments { get; set; }

    /// <summary>
    /// Gets the supported context types (input type -> return type mappings).
    /// </summary>
    public IReadOnlyList<FunctionContext> SupportedContexts => _supportedContexts;

    /// <summary>
    /// Gets or sets the delegate for computing return types.
    /// </summary>
    public GetReturnTypeDelegate? GetReturnType { get; set; }

    /// <summary>
    /// Gets the validation delegates for this function.
    /// </summary>
    public IReadOnlyList<FunctionValidationDelegate> Validations => _validations;

    /// <summary>
    /// Adds supported context types to this function definition.
    /// </summary>
    /// <param name="contexts">Context specification in format "inputType-returnType" or comma-separated multiple</param>
    /// <returns>This instance for fluent chaining</returns>
    /// <example>
    /// <code>
    /// definition.AddContexts("string-integer"); // String in, integer out
    /// definition.AddContexts("integer-integer,decimal-decimal"); // Multiple contexts
    /// </code>
    /// </example>
    public FunctionDefinition AddContexts(string contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        foreach (var context in contexts.Split(','))
        {
            var trimmed = context.Trim();
            var dashIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
            if (dashIndex > 0 && dashIndex < trimmed.Length - 1)
            {
                var inputType = trimmed[..dashIndex].Trim();
                var returnType = trimmed[(dashIndex + 1)..].Trim();
                _supportedContexts.Add(new FunctionContext(inputType, returnType));
            }
        }

        return this;
    }

    /// <summary>
    /// Adds a validation delegate to this function.
    /// </summary>
    /// <param name="validation">The validation delegate</param>
    /// <returns>This instance for fluent chaining</returns>
    public FunctionDefinition AddValidation(FunctionValidationDelegate validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        _validations.Add(validation);
        return this;
    }

    /// <summary>
    /// Sets the return type delegate for this function.
    /// </summary>
    /// <param name="returnTypeDelegate">The delegate for computing return types</param>
    /// <returns>This instance for fluent chaining</returns>
    public FunctionDefinition WithReturnType(GetReturnTypeDelegate returnTypeDelegate)
    {
        GetReturnType = returnTypeDelegate;
        return this;
    }

    /// <summary>
    /// Marks this function as taking expression arguments evaluated with focus as $this.
    /// </summary>
    /// <returns>This instance for fluent chaining</returns>
    public FunctionDefinition WithTakesExpressionArguments()
    {
        TakesExpressionArguments = true;
        return this;
    }

    /// <summary>
    /// Checks if this function supports the given focus type.
    /// </summary>
    /// <param name="focusTypeName">The focus type name</param>
    /// <returns>True if supported</returns>
    public bool IsSupportedContext(string? focusTypeName)
    {
        if (_supportedContexts.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrEmpty(focusTypeName))
        {
            return _supportedContexts.Any(c =>
                c.ContextType.Equals("any", StringComparison.OrdinalIgnoreCase));
        }

        return _supportedContexts.Any(c =>
            c.ContextType.Equals("any", StringComparison.OrdinalIgnoreCase) ||
            c.ContextType.Equals(focusTypeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the return type for the given focus type.
    /// </summary>
    /// <param name="focusTypeName">The focus type name</param>
    /// <returns>The return type name, or null if not determinable</returns>
    public string? GetReturnTypeForContext(string? focusTypeName)
    {
        if (_supportedContexts.Count == 0)
        {
            return null;
        }

        var matchedContext = _supportedContexts.FirstOrDefault(c =>
            c.ContextType.Equals("any", StringComparison.OrdinalIgnoreCase) ||
            (focusTypeName != null && c.ContextType.Equals(focusTypeName, StringComparison.OrdinalIgnoreCase)));

        return matchedContext?.ReturnType;
    }
}

/// <summary>
/// Represents a context type to return type mapping for a function.
/// </summary>
/// <param name="ContextType">The expected input type (or "any" for any type)</param>
/// <param name="ReturnType">The return type for this context</param>
public sealed record FunctionContext(string ContextType, string ReturnType);

/// <summary>
/// Callback for validating function calls during static analysis.
/// </summary>
/// <param name="function">The function call expression being validated</param>
/// <param name="definition">The function definition</param>
/// <param name="arguments">The evaluated argument types</param>
/// <param name="issues">Collection to add validation issues to</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public delegate void FunctionValidationDelegate(
    FunctionCallExpression function,
    FunctionDefinition definition,
    IEnumerable<FhirPathTypeSet> arguments,
    ICollection<ValidationIssue> issues);

/// <summary>
/// Callback for computing return types during static type inference.
/// </summary>
/// <param name="definition">The function definition</param>
/// <param name="focus">The focus type information</param>
/// <param name="arguments">The evaluated argument types</param>
/// <param name="issues">Collection to add type inference issues to</param>
/// <returns>The inferred return types</returns>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
public delegate List<FhirPathType> GetReturnTypeDelegate(
    FunctionDefinition definition,
    FhirPathTypeSet focus,
    IEnumerable<FhirPathTypeSet> arguments,
    ICollection<ValidationIssue> issues);
