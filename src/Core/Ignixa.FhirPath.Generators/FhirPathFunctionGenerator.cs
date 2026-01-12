// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ignixa.FhirPath.Generators;

/// <summary>
/// Source generator that discovers methods with [FhirPathFunction] attribute
/// and generates both SymbolTable registration code and FhirPathEvaluator dispatch code.
/// </summary>
[Generator]
public class FhirPathFunctionGenerator : IIncrementalGenerator
{
    private const string FhirPathFunctionAttributeName = "Ignixa.FhirPath.Attributes.FhirPathFunctionAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var functionMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateMethod(node),
                transform: static (context, _) => GetFunctionMetadata(context))
            .Where(static m => m is not null);

        var compilationAndFunctions = context.CompilationProvider.Combine(functionMethods.Collect());

        context.RegisterSourceOutput(compilationAndFunctions,
            static (context, source) => Execute(context, source.Left, source.Right!));
    }

    private static bool IsCandidateMethod(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax method &&
               method.AttributeLists.Count > 0 &&
               method.Modifiers.Any(m => m.ValueText == "public" || m.ValueText == "internal");
    }

    private static FunctionMetadata? GetFunctionMetadata(GeneratorSyntaxContext context)
    {
        var methodSyntax = (MethodDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(methodSyntax) is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        var attribute = methodSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FhirPathFunctionAttributeName);

        if (attribute == null)
        {
            return null;
        }

        var name = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value?.ToString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var supportedContexts = GetNamedArgument<string>(attribute, "SupportedContexts") ?? "any-any";
        var returnType = GetNamedArgument<string>(attribute, "ReturnType") ?? "any";
        var supportsCollections = GetNamedArgument<bool>(attribute, "SupportsCollections");
        var supportedAtRoot = GetNamedArgument<bool>(attribute, "SupportedAtRoot");
        var minArguments = GetNamedArgument<int>(attribute, "MinArguments");
        var maxArguments = GetNamedArgument<int>(attribute, "MaxArguments");
        var takesExpressionArguments = GetNamedArgument<bool>(attribute, "TakesExpressionArguments");
        var category = GetNamedArgument<string>(attribute, "Category");
        var description = GetNamedArgument<string>(attribute, "Description");

        // Capture method signature details for dispatch generation
        var containingClassName = methodSymbol.ContainingType?.Name ?? string.Empty;
        var methodName = methodSymbol.Name;
        var parameterSignature = AnalyzeParameters(methodSymbol);

        return new FunctionMetadata(
            Name: name,
            SupportedContexts: supportedContexts,
            ReturnType: returnType,
            SupportsCollections: supportsCollections,
            SupportedAtRoot: supportedAtRoot,
            MinArguments: minArguments == -1 ? null : minArguments,
            MaxArguments: maxArguments == -1 ? null : maxArguments,
            TakesExpressionArguments: takesExpressionArguments,
            Category: category,
            Description: description,
            ContainingClassName: containingClassName,
            MethodName: methodName,
            ParameterSignature: parameterSignature);
    }

    /// <summary>
    /// Analyzes method parameters to determine the dispatch pattern.
    /// Uses count-based heuristic: 4 parameters means focus + arguments + context + evaluator.
    /// </summary>
    private static ParameterSignature AnalyzeParameters(IMethodSymbol methodSymbol)
    {
        var paramCount = methodSymbol.Parameters.Length;

        // Pattern detection based on parameter count and types
        // Most functions follow these patterns:
        // - 0 params: no-arg functions (rare in our codebase)
        // - 1 param: focus only
        // - 2 params: focus + arguments OR focus + context
        // - 3 params: focus + arguments + context
        // - 4 params: focus + arguments + context + evaluator

        if (paramCount == 4)
        {
            // Full signature with evaluator: (focus, arguments, context, evaluator)
            return new ParameterSignature(
                HasFocus: true,
                HasArguments: true,
                HasContext: true,
                HasEvaluator: true);
        }

        if (paramCount == 3)
        {
            // (focus, arguments, context) - no evaluator
            return new ParameterSignature(
                HasFocus: true,
                HasArguments: true,
                HasContext: true,
                HasEvaluator: false);
        }

        // For 1-2 parameters, analyze types to determine the pattern
        var hasFocus = false;
        var hasArguments = false;
        var hasContext = false;

        foreach (var param in methodSymbol.Parameters)
        {
            var typeName = param.Type.ToDisplayString();

            if (typeName.Contains("IEnumerable") && typeName.Contains("IElement"))
            {
                hasFocus = true;
            }
            else if (typeName.Contains("IReadOnlyList") && typeName.Contains("Expression"))
            {
                hasArguments = true;
            }
            else if (typeName.Contains("EvaluationContext"))
            {
                hasContext = true;
            }
        }

        return new ParameterSignature(hasFocus, hasArguments, hasContext, HasEvaluator: false);
    }

    private static T? GetNamedArgument<T>(AttributeData attribute, string name)
    {
        var namedArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == name);

        if (namedArg.Key == null)
        {
            return default;
        }

        if (namedArg.Value.Value is T value)
        {
            return value;
        }

        return default;
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<FunctionMetadata?> functions)
    {
        var validFunctions = functions
            .Where(f => f is not null)
            .Cast<FunctionMetadata>()
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validFunctions.Count == 0)
        {
            return;
        }

        // Generate SymbolTable registration (existing functionality)
        var symbolTableSource = GenerateSymbolTable(validFunctions);
        context.AddSource("SymbolTable.g.cs", symbolTableSource);

        // Generate FhirPathEvaluator dispatch (new functionality)
        var evaluatorSource = GenerateEvaluatorDispatch(validFunctions);
        context.AddSource("FhirPathEvaluator.g.cs", evaluatorSource);
    }

    private static string GenerateSymbolTable(List<FunctionMetadata> functions)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// This file is generated by FhirPathFunctionGenerator.");
        sb.AppendLine("// Do not edit manually - changes will be overwritten on next build.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Ignixa.FhirPath.Visitors;");
        sb.AppendLine();
        sb.AppendLine("partial class SymbolTable");
        sb.AppendLine("{");
        sb.AppendLine("    partial void RegisterStandardFunctions()");
        sb.AppendLine("    {");

        foreach (var func in functions)
        {
            sb.AppendLine($"        // {func.Name}");
            sb.Append($"        Add(new FunctionDefinition(\"{func.Name}\"");

            if (func.SupportsCollections)
            {
                sb.Append(", supportsCollections: true");
            }

            if (func.SupportedAtRoot)
            {
                sb.Append(", supportedAtRoot: true");
            }

            sb.AppendLine(")");

            if (func.SupportedContexts != "any-any")
            {
                sb.AppendLine($"            .AddContexts(\"{func.SupportedContexts}\")");
            }

            if (func.MinArguments.HasValue || func.MaxArguments.HasValue)
            {
                var min = func.MinArguments.HasValue ? func.MinArguments.Value.ToString() : "null";
                var max = func.MaxArguments.HasValue ? func.MaxArguments.Value.ToString() : "null";
                sb.AppendLine($"            .AddValidation(ValidateArgumentCount({min}, {max}))");
            }

            if (func.TakesExpressionArguments)
            {
                sb.AppendLine("            .WithTakesExpressionArguments()");
            }

            GenerateReturnTypeProperty(sb, func);

            sb.AppendLine("        );");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateReturnTypeProperty(StringBuilder sb, FunctionMetadata func)
    {
        var returnType = func.ReturnType;

        if (string.Equals(returnType, "context", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("            .WithReturnType(ReturnsContext)");
        }
        else if (string.Equals(returnType, "fromargument", StringComparison.OrdinalIgnoreCase))
        {
            // Special case for iif(): union of then/else branches (arguments[1] and arguments[2])
            if (string.Equals(func.Name, "iif", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("            .WithReturnType((def, focus, args, issues) => {");
                sb.AppendLine("                var argList = args.ToList();");
                sb.AppendLine("                var result = new List<FhirPathType>();");
                sb.AppendLine("                if (argList.Count > 1) result.AddRange(argList[1].Types);");
                sb.AppendLine("                if (argList.Count > 2) result.AddRange(argList[2].Types);");
                sb.AppendLine("                return result.Count > 0 ? result : focus.Types.ToList();");
                sb.AppendLine("            })");
            }
            else
            {
                sb.AppendLine("            .WithReturnType(ReturnsFromArgument)");
            }
        }
        else if (!string.Equals(returnType, "any", StringComparison.OrdinalIgnoreCase))
        {
            // For complex types (non-primitive), use schema-aware type resolution
            // to ensure return types have proper IType definitions with children
            if (IsPrimitiveReturnType(returnType))
            {
                // Primitives don't need schema resolution
                sb.AppendLine($"            .WithReturnType((def, focus, args, issues) => new List<FhirPathType> {{ new FhirPathType(\"{func.ReturnType}\", focus.IsCollection()) }})");
            }
            else
            {
                // Complex types (Extension, Resource, etc.) use schema-aware resolution
                // This ensures the returned type has proper child definitions for property access
                sb.AppendLine($"            .WithReturnType(CreateSchemaAwareReturnType(\"{func.ReturnType}\"))");
            }
        }
    }

    /// <summary>
    /// Determines if a return type is a FhirPath primitive type.
    /// Primitive types don't require schema resolution.
    /// </summary>
    private static bool IsPrimitiveReturnType(string typeName)
    {
        // FhirPath primitive types as per specification
        // These don't have children that need schema resolution
        return typeName.ToUpperInvariant() switch
        {
            "BOOLEAN" or "INTEGER" or "STRING" or "DECIMAL" or "URI" or "URL" or
            "CANONICAL" or "BASE64BINARY" or "INSTANT" or "DATE" or "DATETIME" or
            "TIME" or "CODE" or "OID" or "ID" or "MARKDOWN" or "UNSIGNEDINT" or
            "POSITIVEINT" or "UUID" or "XHTML" or "ANY" => true,
            _ => false
        };
    }

    /// <summary>
    /// Generates the FhirPathEvaluator dispatch partial class with switch expression.
    /// </summary>
    private static string GenerateEvaluatorDispatch(List<FunctionMetadata> functions)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// This file is generated by FhirPathFunctionGenerator.");
        sb.AppendLine("// Do not edit manually - changes will be overwritten on next build.");
        sb.AppendLine("//");
        sb.AppendLine("// Generated dispatch for FhirPath functions based on [FhirPathFunction] attributes.");
        sb.AppendLine("// This eliminates the need for manual switch statement maintenance.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Ignixa.Abstractions;");
        sb.AppendLine("using Ignixa.FhirPath.Evaluation.Functions;");
        sb.AppendLine("using Ignixa.FhirPath.Expressions;");
        sb.AppendLine();
        sb.AppendLine("namespace Ignixa.FhirPath.Evaluation;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Auto-generated partial class providing function dispatch.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("partial class FhirPathEvaluator");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Dispatches a function call to the appropriate implementation.");
        sb.AppendLine("    /// This method is auto-generated from [FhirPathFunction] attributes.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <param name=\"functionName\">The name of the function to call</param>");
        sb.AppendLine("    /// <param name=\"focus\">The focus collection</param>");
        sb.AppendLine("    /// <param name=\"arguments\">The function arguments</param>");
        sb.AppendLine("    /// <param name=\"context\">The evaluation context</param>");
        sb.AppendLine("    /// <returns>The result of the function call</returns>");
        sb.AppendLine("    private IEnumerable<IElement> DispatchFunctionCall(");
        sb.AppendLine("        string functionName,");
        sb.AppendLine("        IEnumerable<IElement> focus,");
        sb.AppendLine("        IReadOnlyList<Expression> arguments,");
        sb.AppendLine("        EvaluationContext context)");
        sb.AppendLine("    {");
        sb.AppendLine("        // FhirPath function names are case-insensitive, ToLowerInvariant is intentional");
        sb.AppendLine("#pragma warning disable CA1308 // Normalize strings to uppercase");
        sb.AppendLine("        return functionName.ToLowerInvariant() switch");
        sb.AppendLine("#pragma warning restore CA1308 // Normalize strings to uppercase");
        sb.AppendLine("        {");

        // Group by category for readability
        var categories = functions
            .GroupBy(f => f.Category ?? "Other")
            .OrderBy(g => g.Key);

        foreach (var categoryGroup in categories)
        {
            sb.AppendLine($"            // {categoryGroup.Key} functions");

            foreach (var func in categoryGroup.OrderBy(f => f.Name))
            {
                var dispatchCall = GenerateDispatchCall(func);
                // FhirPath function names are case-insensitive per specification
                // The switch pattern matches against lowercased input, so we must lowercase here
#pragma warning disable CA1308 // Normalize strings to uppercase
                var lowerName = func.Name.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
                sb.AppendLine($"            \"{lowerName}\" => {dispatchCall},");
            }

            sb.AppendLine();
        }

        sb.AppendLine("            _ => throw new NotSupportedException($\"Function '{functionName}' is not yet implemented\")");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the appropriate dispatch call based on the method's parameter signature.
    /// </summary>
    private static string GenerateDispatchCall(FunctionMetadata func)
    {
        var className = func.ContainingClassName;
        var methodName = func.MethodName;
        var sig = func.ParameterSignature;

        // Pattern matching based on parameter combinations
        // Full signature: (focus, arguments, context, evaluator)
        if (sig.HasFocus && sig.HasArguments && sig.HasContext && sig.HasEvaluator)
        {
            return $"{className}.{methodName}(focus, arguments, context, EvaluateExpression)";
        }

        // (focus, arguments, context) - no evaluator
        if (sig.HasFocus && sig.HasArguments && sig.HasContext && !sig.HasEvaluator)
        {
            return $"{className}.{methodName}(focus, arguments, context)";
        }

        // (focus, context) - no arguments, no evaluator
        if (sig.HasFocus && !sig.HasArguments && sig.HasContext && !sig.HasEvaluator)
        {
            return $"{className}.{methodName}(focus, context)";
        }

        // (focus, arguments) - no context, no evaluator
        if (sig.HasFocus && sig.HasArguments && !sig.HasContext && !sig.HasEvaluator)
        {
            return $"{className}.{methodName}(focus, arguments)";
        }

        // (context only) - no focus, no arguments, no evaluator
        if (!sig.HasFocus && !sig.HasArguments && sig.HasContext && !sig.HasEvaluator)
        {
            return $"{className}.{methodName}(context)";
        }

        // (focus only) - most common simple pattern
        if (sig.HasFocus && !sig.HasArguments && !sig.HasContext && !sig.HasEvaluator)
        {
            return $"{className}.{methodName}(focus)";
        }

        // Fallback: assume focus-only pattern (for safety)
        return $"{className}.{methodName}(focus)";
    }

    /// <summary>
    /// Represents the parameter signature of a FhirPath function method.
    /// </summary>
    private sealed record ParameterSignature(
        bool HasFocus,
        bool HasArguments,
        bool HasContext,
        bool HasEvaluator);

    /// <summary>
    /// Metadata for a FhirPath function including both attribute data and method signature.
    /// </summary>
    private sealed record FunctionMetadata(
        string Name,
        string SupportedContexts,
        string ReturnType,
        bool SupportsCollections,
        bool SupportedAtRoot,
        int? MinArguments,
        int? MaxArguments,
        bool TakesExpressionArguments,
        string? Category,
        string? Description,
        string ContainingClassName,
        string MethodName,
        ParameterSignature ParameterSignature);
}
