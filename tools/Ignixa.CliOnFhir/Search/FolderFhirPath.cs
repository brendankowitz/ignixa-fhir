// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// FHIRPath over the folder's resources for one call, with <c>resolve()</c> reaching any resource the folder holds. Expressions
/// are parsed and evaluated by the engine a SQL-on-FHIR ViewDefinition's <c>column.path</c> is evaluated with
/// (<see cref="FhirPathParser"/>, <see cref="FhirPathEvaluator"/>), with the input as <c>%resource</c> and <c>%rootResource</c>.
/// Resources are read through <see cref="ResourceElements"/>, so a Bundle (<c>urn:uuid:</c>) or conditional
/// (<c>Type?identifier=...</c>) reference reads as the <c>Type/id</c> the search index resolved it to, and <c>resolve()</c>
/// follows it as a search or <c>_include</c> would. Records which resources <c>resolve()</c> reached, for evidence, and which
/// references it could not find. Binds <see cref="PatientSearch"/> as <c>patientSearch(type, query)</c>. Not thread-safe.
/// </summary>
internal sealed partial class FolderFhirPath
{
    private readonly FolderSession _session;
    private readonly ResourceElements _elements;
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly PatientSearch _patientSearch;
    private readonly IReadOnlyDictionary<string, HostFunction> _functions;
    private readonly List<CatalogEntry> _resolved = [];
    private readonly List<string> _unresolved = [];

    public FolderFhirPath(FolderSession session)
    {
        _session = session;
        _elements = new ResourceElements(session);
        _patientSearch = new PatientSearch(session, _elements.Get);
        _functions = new Dictionary<string, HostFunction>(StringComparer.OrdinalIgnoreCase) { [PatientSearch.FunctionName] = _patientSearch.Invoke };
    }

    /// <summary>
    /// Resources that could not be read (changed or removed since they were catalogued), one issue each, then the notes of
    /// the <c>patientSearch</c> subqueries.
    /// </summary>
    public IReadOnlyList<IssueItem> Problems => [.. _elements.Problems, .. _patientSearch.Notes];

    /// <summary>Diagnostics of the <c>patientSearch</c> subqueries beyond those in <see cref="Problems"/>, counted.</summary>
    public IReadOnlyList<OmittedItem> Omitted => _patientSearch.Omitted;

    /// <summary>Parses <paramref name="expression"/>, failing the request with what is wrong with it.</summary>
    /// <param name="label">How the request names the expression, for example <c>column 'date'</c>.</param>
    /// <exception cref="ToolRequestException">The expression is blank or is not valid FHIRPath.</exception>
    public static Expression Compile(string label, string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw ToolRequestException.Error("invalid-parameter", $"{label} has no FHIRPath expression.");
        }

        try
        {
            return new FhirPathParser().Parse(expression);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw ToolRequestException.Error("invalid-parameter", $"{label} '{expression}' is not valid FHIRPath: {ex.Message}");
        }
    }

    /// <summary>
    /// A resource of <paramref name="resourceType"/> with no elements but its type, to evaluate an expression on when there is
    /// nothing else to evaluate it on, so a function the engine does not implement fails the call even when nothing matched.
    /// </summary>
    public IElement Empty(string resourceType) =>
        JsonSourceNodeFactory.Parse(new JsonObject { ["resourceType"] = resourceType }).ToElement(_session.Schema);

    /// <summary>The resource as FHIRPath input, or null when it cannot be read now (the reason is in <see cref="Problems"/>).</summary>
    public IElement? Read(CatalogEntry entry) => _elements.Get(entry);

    /// <summary>Evaluates <paramref name="expression"/> with <paramref name="resource"/> as its input, failing the request on an evaluation error.</summary>
    /// <param name="label">How the request names the expression.</param>
    /// <param name="text">The expression as the caller wrote it, quoted in a failure.</param>
    /// <param name="subject">The resource or set being evaluated, as the response names it, for a failure message.</param>
    /// <exception cref="ToolRequestException">The engine could not evaluate the expression on this input.</exception>
    public IReadOnlyList<IElement> Evaluate(IElement resource, Expression expression, string label, string text, string subject)
    {
        var context = new FhirEvaluationContext { ElementResolver = Resolve, Resource = resource, RootResource = resource, HostFunctions = _functions };
        try
        {
            return [.. _evaluator.Evaluate(resource, expression, context)];
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException or FormatException or InvalidCastException)
        {
            throw ToolRequestException.Error("invalid-parameter", $"{label} '{text}' could not be evaluated on {subject}: {ex.Message}");
        }
    }

    /// <summary>The resources <c>resolve()</c> reached since the last call, in the order reached, and forgets them.</summary>
    public IReadOnlyList<CatalogEntry> TakeResolved()
    {
        List<CatalogEntry> resolved = [.. _resolved];
        _resolved.Clear();
        return resolved;
    }

    /// <summary>
    /// The references <c>resolve()</c> found no resource in the folder for since the last call, as written, and forgets them.
    /// </summary>
    public IReadOnlyList<string> TakeUnresolved()
    {
        List<string> unresolved = [.. _unresolved];
        _unresolved.Clear();
        return unresolved;
    }

    /// <summary>
    /// The host side of <c>resolve()</c>, reached when the reference names nothing inside the input itself (a contained resource,
    /// a sibling Bundle entry): a local <c>Type/id</c> (with or without <c>_history</c>) the folder holds. Anything else (another
    /// server's URL, a <c>urn:</c> or conditional reference the index left unresolved) is recorded for <see cref="TakeUnresolved"/>.
    /// </summary>
    private IElement? Resolve(string reference)
    {
        Match local = LocalReference().Match(reference);
        if (local.Success
            && _session.FindEntry(local.Groups["type"].Value, local.Groups["id"].Value) is { } entry
            && _elements.Get(entry) is { } element)
        {
            _resolved.Add(entry);
            return element;
        }

        _unresolved.Add(reference);
        return null;
    }

    [GeneratedRegex(@"^(?<type>[A-Z][A-Za-z]{1,63})/(?<id>[A-Za-z0-9\-\.]{1,64})(/_history/[A-Za-z0-9\-\.]{1,64})?$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalReference();
}
