// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirPath.Attributes;

/// <summary>
/// Marks a method as a FhirPath function implementation.
/// Source generator uses this attribute to auto-generate SymbolTable registration,
/// eliminating manual maintenance of function metadata.
/// </summary>
/// <remarks>
/// <para>
/// This attribute provides metadata for FhirPath functions that enables:
/// - Static validation of function signatures
/// - Type inference during expression validation
/// - Compile-time discovery of all functions (no runtime reflection)
/// </para>
/// <para>
/// The source generator reads these attributes and generates the SymbolTable.RegisterStandardFunctions()
/// partial method implementation, which registers all functions with their signatures.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [FhirPathFunction("where",
///     SupportedContexts = "any-any",
///     ReturnType = "context",
///     SupportsCollections = true,
///     MinArguments = 1,
///     MaxArguments = 1)]
/// public static IEnumerable&lt;IElement&gt; Where(
///     IEnumerable&lt;IElement&gt; focus,
///     IReadOnlyList&lt;Expression&gt; arguments,
///     EvaluationContext context,
///     Func&lt;...&gt; evaluateExpression)
/// {
///     // Implementation
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class FhirPathFunctionAttribute : Attribute
{
    /// <summary>
    /// Creates a new FhirPathFunctionAttribute with the specified function name.
    /// </summary>
    /// <param name="name">
    /// The FhirPath function name (e.g., "where", "select", "first").
    /// This is the name used in FhirPath expressions.
    /// </param>
    public FhirPathFunctionAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>
    /// Gets the FhirPath function name (e.g., "where", "select", "length").
    /// This is the name used in FhirPath expressions to call this function.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the supported context types for this function.
    /// Format: "contextType-returnType" or "contextType-returnType,contextType-returnType" for multiple.
    /// </summary>
    /// <remarks>
    /// <para>Examples:</para>
    /// <list type="bullet">
    /// <item><description>"any-any" - Accepts any type, returns same type</description></item>
    /// <item><description>"string-integer" - Accepts string, returns integer (e.g., length())</description></item>
    /// <item><description>"any-boolean" - Accepts any type, returns boolean (e.g., empty())</description></item>
    /// <item><description>"integer-integer,decimal-decimal" - Accepts integer or decimal (e.g., abs())</description></item>
    /// </list>
    /// <para>Default is "any-any" which accepts any input type.</para>
    /// </remarks>
    public string SupportedContexts { get; set; } = "any-any";

    /// <summary>
    /// Gets or sets the return type for type inference.
    /// </summary>
    /// <remarks>
    /// <para>Special values:</para>
    /// <list type="bullet">
    /// <item><description>"context" - Returns the same type as focus (e.g., where(), first())</description></item>
    /// <item><description>"fromArgument" - Returns type from first argument evaluation (e.g., select())</description></item>
    /// <item><description>"any" - No specific return type inference</description></item>
    /// </list>
    /// <para>Concrete types: "boolean", "integer", "decimal", "string", "date", "dateTime", "time"</para>
    /// </remarks>
    public string ReturnType { get; set; } = "any";

    /// <summary>
    /// Gets or sets whether this function supports collection inputs.
    /// When true, the function can operate on collections (e.g., where(), select(), count()).
    /// When false, the function expects a singleton (e.g., length(), startsWith()).
    /// </summary>
    public bool SupportsCollections { get; set; }

    /// <summary>
    /// Gets or sets whether this function can be called at the expression root
    /// without an explicit focus (e.g., empty(), now(), today()).
    /// </summary>
    public bool SupportedAtRoot { get; set; }

    /// <summary>
    /// Gets or sets the minimum number of arguments required.
    /// Use -1 for no minimum validation (same as not setting).
    /// </summary>
    /// <remarks>
    /// For functions with optional arguments, set to the count of required arguments.
    /// Example: exists() has MinArguments = 0 (no args required), MaxArguments = 1 (optional criteria).
    /// </remarks>
    public int MinArguments { get; set; } = -1;

    /// <summary>
    /// Gets or sets the maximum number of arguments allowed.
    /// Use -1 for no maximum validation (same as not setting).
    /// </summary>
    /// <remarks>
    /// For variadic functions that accept unlimited arguments, leave as -1.
    /// For fixed argument counts, set MinArguments = MaxArguments.
    /// </remarks>
    public int MaxArguments { get; set; } = -1;

    /// <summary>
    /// Gets or sets whether this function takes expression arguments that should be evaluated
    /// with the focus element as $this context (e.g., where(), select(), exists(), all()).
    /// When true, arguments are evaluated with single-item context from focus collection.
    /// </summary>
    public bool TakesExpressionArguments { get; set; }

    /// <summary>
    /// Gets or sets the category of the function for documentation/grouping purposes.
    /// </summary>
    /// <remarks>
    /// Common categories: "Collection", "String", "Boolean", "Math", "DateTime", "Utility", "FHIR-specific"
    /// </remarks>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets a brief description of what the function does.
    /// Used for documentation and IDE support.
    /// </summary>
    public string? Description { get; set; }
}
