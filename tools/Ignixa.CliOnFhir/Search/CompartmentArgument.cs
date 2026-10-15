// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// The <c>compartment</c> argument of a search (<c>Patient/123</c>): validated against the session's compartment definitions
/// and combined with the query as the server's compartment handler combines <c>Patient/123/Observation?code=...</c>
/// (<c>src/Application/Ignixa.Application/Features/Compartment/SearchCompartmentHandler.cs</c>).
/// </summary>
internal static partial class CompartmentArgument
{
    /// <summary>The searched type is never in compartments of the requested type, by the version's definition.</summary>
    public const string ExcludesTypeCode = "compartment-excludes-type";

    /// <summary>The compartment's own resource is not in the folder.</summary>
    public const string ResourceAbsentCode = "compartment-resource-absent";

    /// <summary>The compartment's own type is searched, and the version's definition does not make its own resource a member.</summary>
    public const string ExcludesOwnResourceCode = "compartment-excludes-own-resource";

    /// <summary>
    /// <paramref name="options"/> restricted to the compartment <paramref name="compartment"/> names, and the issues that
    /// qualify the answer: a <see cref="ExcludesTypeCode"/> warning when nothing of the searched type can match, an
    /// <see cref="ExcludesOwnResourceCode"/> information when the compartment's own type is searched and the definition leaves
    /// its own resource out (R4 Patient), and a <see cref="ResourceAbsentCode"/> information when the folder does not hold
    /// the compartment's resource.
    /// </summary>
    /// <exception cref="ToolRequestException">
    /// <paramref name="compartment"/> is not <c>Type/id</c> with a compartment type and a FHIR id, or the searched type's
    /// membership in such a compartment cannot be evaluated.
    /// </exception>
    public static (SearchOptions Options, IReadOnlyList<IssueItem> Issues) Apply(FolderSession session, SearchOptions options, string compartment)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(compartment);

        InMemoryCompartmentMembership membership = session.Compartments;
        (string type, string id) = Split(compartment, membership, session);
        string searched = options.ResourceType;
        try
        {
            membership.ThrowIfUnevaluable(type, searched);
        }
        catch (SearchOperationNotSupportedException ex)
        {
            throw SearchFailures.NotSupported($"'compartment={compartment}' cannot be evaluated: {ex.Issues.FirstOrDefault()?.Diagnostics ?? ex.Message}");
        }

        List<IssueItem> issues = [];
        if (!membership.ResourceTypes(type).Contains(searched, StringComparer.Ordinal))
        {
            issues.Add(new IssueItem("warning", ExcludesTypeCode, $"{searched} resources are never in a {type} compartment (the {session.Schema.Version} CompartmentDefinition lists no parameter for {searched}), so nothing matches.", Location: null));
        }

        if (searched == type && !membership.IncludesCompartmentResource(type))
        {
            IReadOnlyList<string> through = membership.Parameters(type, searched);
            string others = through.Count == 0 ? "no other" : $"only other {searched} resources referencing it through {string.Join(", ", through)}";
            issues.Add(new IssueItem("information", ExcludesOwnResourceCode, $"{type}/{id} itself is not in its own compartment under the {session.Schema.Version} CompartmentDefinition, which has no {InMemoryCompartmentMembership.FocalResourceCode} for {type}, so {others} can match{(type == "Patient" ? $" (R5 adds {InMemoryCompartmentMembership.FocalResourceCode}, which makes it a member)" : string.Empty)}. Read {type}/{id} with read_resources.", Location: null));
        }

        if (session.FindEntry(type, id) is null)
        {
            issues.Add(new IssueItem("information", ResourceAbsentCode, $"{type}/{id} is not in the folder; resources that reference it are still matched.", Location: null));
        }

        var expression = new CompartmentSearchExpression(type, id);
        Expression combined = options.Expression switch
        {
            null => expression,
            MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => Expression.And([expression, .. and.Expressions]),
            Expression other => Expression.And(expression, other),
        };

        return (new SearchOptions(options) { Expression = combined }, issues);
    }

    private static (string Type, string Id) Split(string compartment, InMemoryCompartmentMembership membership, FolderSession session)
    {
        string[] parts = compartment.Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'compartment' must be 'Type/id', for example 'Patient/123', not '{compartment}'.");
        }

        if (!membership.CompartmentTypes.Contains(parts[0], StringComparer.Ordinal))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'{parts[0]}' in 'compartment={compartment}' is not a {session.Schema.Version} compartment type; use one of {string.Join(", ", membership.CompartmentTypes)}.");
        }

        if (!FhirId().IsMatch(parts[1]))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'{parts[1]}' in 'compartment={compartment}' is not a FHIR id (1 to 64 letters, digits, '-' or '.').");
        }

        return (parts[0], parts[1]);
    }

    [GeneratedRegex("^[A-Za-z0-9\\-\\.]{1,64}$")]
    private static partial Regex FhirId();
}
