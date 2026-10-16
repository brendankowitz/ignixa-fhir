// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Expressions;
using Ignixa.SqlOnFhir.Parsing;

#pragma warning disable CS0618 // ISourceNavigator is what the SQL-on-FHIR evaluator takes; its migration is pending upstream.

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Validates a ViewDefinition before anything is evaluated, as <c>tools/Ignixa.SqlOnFhir.Cli</c>'s <c>validate</c> does
/// (<c>Commands/ValidateCommand.cs</c>: a JSON object whose <c>resourceType</c> is ViewDefinition that
/// <see cref="ViewDefinitionExpressionParser"/> accepts), and adds the checks a run needs: a resource type of the folder's FHIR
/// version and at least one column. Every problem is an <c>invalid-view</c> error.
/// </summary>
internal static class ViewCompiler
{
    /// <summary>The failure code of a ViewDefinition that cannot be run.</summary>
    public const string InvalidCode = "invalid-view";

    /// <summary>Validates <paramref name="view"/>, with <paramref name="extraWhere"/> added to its <c>where</c> clauses.</summary>
    /// <exception cref="ToolRequestException">The view, or the extra filter, is not valid.</exception>
    public static CompiledView Compile(FolderSession session, LoadedView view, string? extraWhere)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);

        string? type = view.Json["resourceType"] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
        if (type != ViewDefinitions.ResourceType)
        {
            throw Invalid($"The ViewDefinition is not a ViewDefinition resource (resourceType {(type is null ? "missing" : ViewIssues.Word(type))}).");
        }

        ViewDefinitionExpression expression = Parse(view, view.Json);
        if (!session.Schema.ResourceTypeNames.Contains(expression.Resource))
        {
            throw Invalid($"The ViewDefinition runs over '{ViewIssues.Word(expression.Resource)}', which is not a {session.Schema.Version} resource type.");
        }

        IReadOnlyList<ColumnSchema> columns = Columns(view, expression);
        if (columns.Count == 0)
        {
            throw Invalid("The ViewDefinition has no columns; add a select with at least one column (name and path).");
        }

        return extraWhere is null ? new CompiledView(Navigator(view.Json), expression, columns) : WithWhere(view, extraWhere, columns);
    }

    // The extra filter is a where clause of the view itself, so the engine evaluates, type-checks and combines it like the others.
    private static CompiledView WithWhere(LoadedView view, string extraWhere, IReadOnlyList<ColumnSchema> columns)
    {
        FolderFhirPath.Compile("'where'", extraWhere);
        JsonObject filtered = (JsonObject)view.Json.DeepClone();
        JsonArray clauses = filtered["where"] as JsonArray ?? [];
        clauses.Add(new JsonObject { ["path"] = extraWhere });
        filtered["where"] = clauses;
        try
        {
            return new CompiledView(Navigator(filtered), ViewDefinitionExpressionParser.Parse(Navigator(filtered)), columns);
        }
        catch (Exception ex) when (IsDefinitionProblem(ex))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'where' {QueryEcho.Of(extraWhere)} cannot be added to the view: {ex.Message}");
        }
    }

    private static ViewDefinitionExpression Parse(LoadedView view, JsonObject json)
    {
        try
        {
            return ViewDefinitionExpressionParser.Parse(Navigator(json));
        }
        catch (Exception ex) when (IsDefinitionProblem(ex))
        {
            throw Invalid(ViewIssues.Describe("The ViewDefinition is not valid", ex, view.FromFolder));
        }
    }

    private static IReadOnlyList<ColumnSchema> Columns(LoadedView view, ViewDefinitionExpression expression)
    {
        try
        {
            return new SqlOnFhirSchemaEvaluator().GetSchema(expression);
        }
        catch (Exception ex) when (IsDefinitionProblem(ex))
        {
            throw Invalid(ViewIssues.Describe("The ViewDefinition's columns cannot be worked out", ex, view.FromFolder));
        }
    }

    private static ISourceNavigator Navigator(JsonObject json) => JsonSourceNodeFactory.Parse(json).ToSourceNavigator();

    private static bool IsDefinitionProblem(Exception ex) =>
        ex is InvalidOperationException or FormatException or ArgumentException or NotSupportedException;

    private static ToolRequestException Invalid(string diagnostics) => ToolRequestException.Error(InvalidCode, diagnostics);
}
