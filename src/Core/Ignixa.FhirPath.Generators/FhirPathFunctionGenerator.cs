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
/// and generates SymbolTable.RegisterStandardFunctions() registration code.
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
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodSyntax);

        if (methodSymbol == null)
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
        var category = GetNamedArgument<string>(attribute, "Category");
        var description = GetNamedArgument<string>(attribute, "Description");

        return new FunctionMetadata(
            Name: name,
            SupportedContexts: supportedContexts,
            ReturnType: returnType,
            SupportsCollections: supportsCollections,
            SupportedAtRoot: supportedAtRoot,
            MinArguments: minArguments == -1 ? null : minArguments,
            MaxArguments: maxArguments == -1 ? null : maxArguments,
            Category: category,
            Description: description);
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

        var source = GenerateSymbolTable(validFunctions);
        context.AddSource("SymbolTable.g.cs", source);
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
            sb.AppendLine("            .WithReturnType(ReturnsFromArgument)");
        }
        else if (!string.Equals(returnType, "any", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"            .WithReturnType((def, focus, args, issues) => new List<FhirPathType> {{ new FhirPathType(\"{func.ReturnType}\") }})");
        }
    }

    private sealed record FunctionMetadata(
        string Name,
        string SupportedContexts,
        string ReturnType,
        bool SupportsCollections,
        bool SupportedAtRoot,
        int? MinArguments,
        int? MaxArguments,
        string? Category,
        string? Description);
}
