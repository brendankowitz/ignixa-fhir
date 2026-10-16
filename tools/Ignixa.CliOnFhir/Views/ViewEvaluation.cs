// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.FhirPath.Evaluation;
using Ignixa.SqlOnFhir.Evaluation;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Evaluates a view over catalogued resources with Ignixa's SQL-on-FHIR evaluator (<see cref="SqlOnFhirEvaluator"/>, as
/// <c>tools/Ignixa.SqlOnFhir.Cli</c>'s <c>run</c> does), reading each resource through <see cref="ResourceElements"/> so its
/// Bundle and conditional references read as the <c>Type/id</c> the search index resolved them to. A resource is evaluated on
/// its own, which keeps its rows' source known; a view the engine orders across resources is evaluated in one batch instead.
/// Not thread-safe.
/// </summary>
/// <param name="view">The validated view.</param>
/// <param name="variables">The variables applied.</param>
/// <param name="elements">The resources as FHIRPath input.</param>
/// <param name="empty">A resource of the view's type with no elements, to tell a failure of the view from a failure on data.</param>
internal sealed partial class ViewEvaluation(CompiledView view, IReadOnlyDictionary<string, string> variables, ResourceElements elements, IElement empty)
{
    private readonly SqlOnFhirEvaluator _evaluator = new();
    private readonly Dictionary<string, int> _columnIndexes = view.Columns
        .Select((column, index) => (column.Name, index))
        .ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.Ordinal);

    /// <summary>Resources in scope that could not be read, and so produced no rows (each is among the elements' problems).</summary>
    public int Unreadable { get; private set; }

    /// <summary>
    /// Checks the variables before anything is read: the engine rejects a name it manages itself (such as <c>rowIndex</c>), and
    /// does so before it looks at any resource.
    /// </summary>
    /// <exception cref="ToolRequestException">A variable name the engine manages itself.</exception>
    public void CheckVariables()
    {
        try
        {
            _ = _evaluator.EvaluateBatch(view.Navigator, [], variables);
        }
        catch (ArgumentException ex)
        {
            throw ToolRequestException.Error("invalid-parameter", ex.Message);
        }
    }

    /// <summary>
    /// Evaluates the view on a resource of its type with no elements, so that a view the engine cannot evaluate at all (an
    /// unknown function) fails the call even when no resource is in scope, rather than answering with no rows.
    /// </summary>
    /// <exception cref="ToolRequestException">The engine cannot evaluate the view.</exception>
    public void EvaluateOnEmpty(bool viewFromFolder) => Evaluate(() => _evaluator.Evaluate(view.Navigator, empty, variables), "an empty resource", viewFromFolder);

    /// <summary>The rows of <paramref name="entries"/>, in order.</summary>
    /// <exception cref="ToolRequestException">The engine cannot evaluate the view on a resource.</exception>
    public IEnumerable<ViewRow> Rows(IReadOnlyList<CatalogEntry> entries, bool viewFromFolder)
    {
        if (view.OrdersAcrossResources)
        {
            return BatchRows(entries, viewFromFolder);
        }

        return entries.SelectMany(entry => OneRows(entry, viewFromFolder));
    }

    private IEnumerable<ViewRow> OneRows(CatalogEntry entry, bool viewFromFolder)
    {
        if (elements.Get(entry) is not { } element)
        {
            Unreadable++;
            return [];
        }

        List<Dictionary<string, object?>> rows = Evaluate(() => _evaluator.Evaluate(view.Navigator, element, variables), FhirIds.Reference(entry), viewFromFolder);
        return rows.Select(row => new ViewRow(Cells(row), entry));
    }

    private IEnumerable<ViewRow> BatchRows(IReadOnlyList<CatalogEntry> entries, bool viewFromFolder)
    {
        List<IElement> resources = [];
        foreach (CatalogEntry entry in entries)
        {
            if (elements.Get(entry) is { } element)
            {
                resources.Add(element);
            }
            else
            {
                Unreadable++;
            }
        }

        List<Dictionary<string, object?>> rows = Evaluate(() => _evaluator.EvaluateBatch(view.Navigator, resources, variables), $"{resources.Count} resources", viewFromFolder);
        return rows.Select(row => new ViewRow(Cells(row), Source: null));
    }

    private List<Dictionary<string, object?>> Evaluate(Func<IEnumerable<Dictionary<string, object?>>> evaluate, string subject, bool viewFromFolder)
    {
        try
        {
            return [.. evaluate()];
        }
        catch (Exception ex) when (IsEvaluationProblem(ex))
        {
            throw ToolRequestException.Error("view-evaluation-failed", Failure(ex, subject, viewFromFolder));
        }
    }

    private static bool IsEvaluationProblem(Exception ex) =>
        ex is InvalidOperationException or FhirPathEvaluationException or ArgumentException or NotSupportedException or FormatException or InvalidCastException or OverflowException;

    private static Exception Innermost(Exception exception)
    {
        Exception innermost = exception;
        while (innermost.InnerException is { } inner)
        {
            innermost = inner;
        }

        return innermost;
    }

    // The engine's message can quote the resource it was evaluating ("Cannot convert value '...'"), so it is repeated only when it
    // cannot: when the view fails the same way on a resource with no elements, the message holds nothing from the data, and it is
    // quoted for the caller's own view (a folder's ViewDefinition is untrusted, so only its digest is given). A column with
    // several values, the common data-dependent failure, is recognized and put in the view's own terms.
    private string Failure(Exception exception, string subject, bool viewFromFolder)
    {
        Exception innermost = Innermost(exception);
        if (MultipleValues().Match(innermost.Message) is { Success: true } match && _columnIndexes.ContainsKey(match.Groups["name"].Value))
        {
            string column = ViewIssues.Word(match.Groups["name"].Value);
            return $"Column '{column}' has {match.Groups["count"].Value} values on {subject} but is not a collection; set collection to true on the column, or make its path return at most one value (first(), where(...)).";
        }

        if (FailureOnEmpty() is { } viewFailure)
        {
            return ViewIssues.Describe($"The ViewDefinition cannot be evaluated ({viewFailure.GetType().Name})", viewFailure, viewFromFolder);
        }

        string where = viewFromFolder ? "the folder's ViewDefinition" : "the ViewDefinition";
        return $"Evaluating {where} on {subject} failed with {innermost.GetType().Name}. The engine's message can quote the resource, so only its digest is given: {FhirIds.Digest(innermost.Message)}.";
    }

    private Exception? FailureOnEmpty()
    {
        try
        {
            _ = _evaluator.Evaluate(view.Navigator, empty, variables).ToList();
            return null;
        }
        catch (Exception ex) when (IsEvaluationProblem(ex))
        {
            return Innermost(ex);
        }
    }

    private object?[] Cells(Dictionary<string, object?> row)
    {
        object?[] cells = new object?[view.Columns.Count];
        foreach ((string name, object? value) in row)
        {
            if (!_columnIndexes.TryGetValue(name, out int index))
            {
                throw ToolRequestException.Error("view-evaluation-failed", $"The engine produced a column '{ViewIssues.Word(name)}' that the view's schema does not list; this is a defect in the SQL-on-FHIR engine, not in the view.");
            }

            cells[index] = Normalize(value);
        }

        return cells;
    }

    // Numbers, booleans and text are written as they are; a date or time the engine returns as one is written in ISO 8601, in full.
    private static object? Normalize(object? value) => value switch
    {
        null or string or bool or int or long or decimal or double => value,
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    [GeneratedRegex(@"^Column '(?<name>.*)' has collection=false but FHIRPath expression returned (?<count>\d+) values", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex MultipleValues();
}
