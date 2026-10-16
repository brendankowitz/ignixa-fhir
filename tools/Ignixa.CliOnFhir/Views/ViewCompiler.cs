// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Visitors;
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
internal static partial class ViewCompiler
{
    /// <summary>The failure code of a ViewDefinition that cannot be run.</summary>
    public const string InvalidCode = "invalid-view";

    private static readonly HashSet<string> ColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "base64Binary", "boolean", "canonical", "code", "date", "dateTime", "decimal", "id", "instant", "integer", "integer64",
        "markdown", "oid", "positiveInt", "string", "time", "unsignedInt", "uri", "url", "uuid", "xhtml",
    };

    private const string TypeUrlPrefix = "http://hl7.org/fhir/StructureDefinition/";

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

        if (ViewDefinitionShape.UnknownProperty(view.Json) is var (parent, property))
        {
            throw Invalid($"The ViewDefinition has a property '{ViewIssues.Word(property)}'{(parent.Length == 0 ? string.Empty : $" in {parent}")} that a ViewDefinition does not define; the evaluator would ignore it. Check its spelling.");
        }

        if (ViewExpressionGuards.Violation(view.Json) is var (where, reason))
        {
            throw Invalid($"The ViewDefinition's {where} {reason}.");
        }

        ViewDefinitionExpression expression = Parse(view, view.Json);
        if (!session.Schema.ResourceTypeNames.Contains(expression.Resource))
        {
            throw Invalid($"The ViewDefinition runs over '{ViewIssues.Word(expression.Resource)}', which is not a {session.Schema.Version} resource type.");
        }

        CheckColumns(expression);
        for (int i = 0; i < expression.Where.Length; i++)
        {
            if (NonBooleanType(session, expression.Where[i], expression.Resource) is { } found)
            {
                throw Invalid($"Clause {i} of the ViewDefinition's where yields {found}, not a boolean; a where path must be a condition (gender = 'female', code.exists()).");
            }
        }

        IReadOnlyList<ColumnSchema> columns = Columns(view, expression);
        if (columns.Count == 0)
        {
            throw Invalid("The ViewDefinition has no columns; add a select with at least one column (name and path).");
        }

        return extraWhere is null ? new CompiledView(Navigator(view.Json), expression, columns) : WithWhere(session, view, extraWhere, columns);
    }

    // The extra filter is a where clause of the view itself, so the engine evaluates, type-checks and combines it like the others.
    private static CompiledView WithWhere(FolderSession session, LoadedView view, string extraWhere, IReadOnlyList<ColumnSchema> columns)
    {
        if (ViewExpressionGuards.Violation(extraWhere) is { } reason)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'where' {reason}.");
        }

        FolderFhirPath.Compile("'where'", extraWhere);
        JsonObject filtered = (JsonObject)view.Json.DeepClone();
        JsonArray clauses = filtered["where"] as JsonArray ?? [];
        clauses.Add(new JsonObject { ["path"] = extraWhere });
        filtered["where"] = clauses;
        try
        {
            ViewDefinitionExpression expression = ViewDefinitionExpressionParser.Parse(Navigator(filtered));
            if (NonBooleanType(session, expression.Where[^1], expression.Resource) is { } found)
            {
                throw ToolRequestException.Error("invalid-parameter", $"'where' {QueryEcho.Of(extraWhere)} yields {found}, not a boolean; it must be a condition (gender = 'female', code.exists()).");
            }

            return new CompiledView(Navigator(filtered), expression, columns);
        }
        catch (Exception ex) when (IsDefinitionProblem(ex))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'where' {QueryEcho.Of(extraWhere)} cannot be added to the view: {ex.Message}");
        }
    }

    // The type names come from the FHIR definitions, not from the folder.
    private static string? NonBooleanType(FolderSession session, WhereExpression clause, string resource)
    {
        FhirPathTypeSet types = new FhirPathAnalyzer(session.Schema).InferTypes(clause.Filter, resource);
        return types.Types.Count > 0 && !types.HasUnknown && !types.CanBeOfType("boolean") ? types.TypeNames() : null;
    }

    // What the evaluator would otherwise do with these quietly: take the last of two columns of one name, or write a name the
    // specification does not allow (and that can be folder text) into the output.
    private static void CheckColumns(ViewDefinitionExpression expression)
    {
        foreach (ColumnExpression column in expression.Select.SelectMany(AllColumns))
        {
            if (!ColumnName().IsMatch(column.Name))
            {
                throw Invalid($"A column name must match ^[A-Za-z][A-Za-z0-9_]*$ (the ViewDefinition specification's rule) and be at most 64 characters; '{ViewIssues.Word(column.Name)}' does not.");
            }

            if (column.Type is { } type && !IsKnownType(type))
            {
                throw Invalid($"Column '{column.Name}' has the type '{ViewIssues.Word(type)}', which is not a FHIR primitive type name (string, integer, boolean, dateTime, ...) or {TypeUrlPrefix}<name>.");
            }
        }

        string? repeated = expression.Select.SelectMany(OutputNames).GroupBy(name => name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (repeated is not null)
        {
            throw Invalid($"The column '{repeated}' is produced more than once; every column of a view needs its own name (the branches of a unionAll repeat theirs, which is the one exception).");
        }
    }

    private static bool IsKnownType(string type) =>
        ColumnTypes.Contains(type.StartsWith(TypeUrlPrefix, StringComparison.Ordinal) ? type[TypeUrlPrefix.Length..] : type);

    private static IEnumerable<ColumnExpression> AllColumns(SelectExpression select) =>
        select.Columns.Concat(select.NestedSelect.SelectMany(AllColumns)).Concat(select.UnionAll.SelectMany(AllColumns));

    // The names one select puts in a row: its columns, its nested selects', and one branch's of its unionAll (every branch has the same).
    private static IEnumerable<string> OutputNames(SelectExpression select) =>
        select.Columns.Select(column => column.Name)
            .Concat(select.NestedSelect.SelectMany(OutputNames))
            .Concat(select.UnionAll.IsEmpty ? [] : OutputNames(select.UnionAll[0]));

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnName();

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
