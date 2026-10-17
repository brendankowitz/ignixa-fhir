// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Makes the session's <see cref="AsOf"/> instant the FHIRPath variables of a view: <c>%asOf</c>, a dateTime, and <c>%asOfDate</c>,
/// a date (the asOf calendar day in the asOf offset), which compares with dateTimes written without a zone where <c>%asOf</c> cannot.
/// The engine applies a variable only where the ViewDefinition declares a constant of its name, and refuses a reference to one it
/// does not declare, so the run gives the view a constant of each name it does not declare itself. A ViewDefinition's own constant
/// is kept, with a warning that the variable is then not the session's; a <c>--var</c> of either name replaces the value, as a
/// variable replaces any constant, with a warning, and must be of the variable's type: an instant for <c>asOf</c> (as
/// <c>--as-of</c> is), a date for <c>asOfDate</c>.
/// </summary>
internal static partial class ViewAsOf
{
    /// <summary>The instant: <c>%asOf</c>.</summary>
    public const string Name = "asOf";

    /// <summary>The day: <c>%asOfDate</c>.</summary>
    public const string DateName = "asOfDate";

    /// <summary>The view to compile, what <c>facts.asOf</c> says it read as <c>%asOf</c>, and whether its expressions name <c>%asOf</c>.</summary>
    public sealed record Applied(LoadedView View, AsOfFacts Facts, bool ReadsAsOf);

    /// <summary><paramref name="view"/> with the constants added that it does not declare; the notes go to <paramref name="issues"/>.</summary>
    /// <exception cref="ToolRequestException"><c>invalid-parameter</c>: a variable of either name that is not of its type.</exception>
    public static Applied With(LoadedView view, AsOf asOf, IReadOnlyDictionary<string, string> variables, List<IssueItem> issues)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(asOf);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(issues);

        CheckVariables(variables);
        JsonObject? declaredAsOf = Declared(view.Json, Name);
        Note(Name, declaredAsOf is not null, variables, issues);
        Note(DateName, Declared(view.Json, DateName) is not null, variables, issues);
        AsOfFacts facts = variables.TryGetValue(Name, out string? variable)
            ? new AsOfFacts(variable, AsOfSources.Variable)
            : declaredAsOf is not null ? new AsOfFacts(ConstantValue(declaredAsOf), AsOfSources.ViewConstant) : AsOfFacts.Of(asOf);
        bool reads = ReadsAsOf().IsMatch(view.Json.ToJsonString());

        // A constant member that is not an array is the ViewDefinition's own error, which compiling it reports as it is.
        if (view.Json["constant"] is { } existing && existing is not JsonArray)
        {
            return new Applied(view, facts, reads);
        }

        JsonObject json = (JsonObject)view.Json.DeepClone();
        JsonArray list = (JsonArray?)json["constant"] ?? [];
        if (declaredAsOf is null)
        {
            list.Add(new JsonObject { ["name"] = Name, ["valueDateTime"] = asOf.Instant });
        }

        if (Declared(view.Json, DateName) is null)
        {
            list.Add(new JsonObject { ["name"] = DateName, ["valueDate"] = asOf.Moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        }

        json["constant"] = list;
        return new Applied(view with { Json = json }, facts, reads);
    }

    private static void CheckVariables(IReadOnlyDictionary<string, string> variables)
    {
        if (variables.TryGetValue(Name, out string? instant) && FhirInstant.Problem(instant, out _) is { } problem)
        {
            throw ToolRequestException.Error("invalid-parameter", $"Variable asOf must be an instant, as --as-of is (for a day, set asOfDate); the value given {Shown(instant)} is {problem}.");
        }

        if (variables.TryGetValue(DateName, out string? date) && !(DateShape().IsMatch(date) && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
        {
            throw ToolRequestException.Error("invalid-parameter", $"Variable asOfDate must be a FHIR date, for example 2024-05-01. The value given {Shown(date)} is not one.");
        }
    }

    private static void Note(string name, bool declared, IReadOnlyDictionary<string, string> variables, List<IssueItem> issues)
    {
        if (variables.ContainsKey(name))
        {
            issues.Add(new IssueItem("warning", "as-of-overridden", $"The variable {name} replaces {(declared ? $"the ViewDefinition's own constant {name}" : "the session's instant (see facts.asOf)")} as %{name} in this run.", Location: null));
        }
        else if (declared)
        {
            issues.Add(new IssueItem("warning", "as-of-shadowed", $"The ViewDefinition declares its own constant {name}, so %{name} is that constant's value, not the session's instant; rename the constant to use %{name} as the instant the data is read as of.", Location: null));
        }
    }

    private static JsonObject? Declared(JsonObject view, string name) => view["constant"] is JsonArray constants
        ? constants.OfType<JsonObject>().FirstOrDefault(constant => constant["name"] is JsonValue value && value.TryGetValue(out string? text) && text == name)
        : null;

    // The constant's value[x] as written when it is shaped like a FHIR date or dateTime; the ViewDefinition can be the folder's, so
    // anything else is shown by its digest.
    private static string ConstantValue(JsonObject constant)
    {
        string? text = constant.Where(member => member.Key.StartsWith("value", StringComparison.Ordinal)).Select(member => member.Value).OfType<JsonValue>()
            .Select(value => value.TryGetValue(out string? written) ? written : value.ToJsonString()).FirstOrDefault();
        return text is null ? "(no value)" : DateTimeShape().IsMatch(text) ? text : FhirIds.Digest(text);
    }

    private static string Shown(string value) => DateTimeShape().IsMatch(value) ? $"'{value}'" : $"(digest {FhirIds.Digest(value)})";

    // %asOf, plain or delimited, but not %asOfDate.
    [GeneratedRegex(@"%(asOf|`asOf`)(?![A-Za-z0-9_])", RegexOptions.CultureInvariant)]
    private static partial Regex ReadsAsOf();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateShape();

    [GeneratedRegex(@"^[0-9TZ:.+\-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateTimeShape();
}
