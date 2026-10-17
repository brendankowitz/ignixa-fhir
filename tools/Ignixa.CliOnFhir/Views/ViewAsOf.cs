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

    /// <summary>The view to compile, what <c>facts.asOf</c> says it read, and whether its expressions name <c>%asOf</c> or <c>%asOfDate</c>.</summary>
    public sealed record Applied(LoadedView View, AsOfFacts Facts, bool ReadsClock);

    /// <summary>
    /// <paramref name="view"/> with the constants added that it does not declare; the notes go to <paramref name="issues"/>.
    /// <paramref name="extraWhere"/>, the call's own filter, is an expression of the view too.
    /// </summary>
    /// <exception cref="ToolRequestException"><c>invalid-parameter</c>: a variable of either name that is not of its type.</exception>
    public static Applied With(LoadedView view, AsOf asOf, IReadOnlyDictionary<string, string> variables, string? extraWhere, List<IssueItem> issues)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(asOf);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(issues);

        CheckVariables(variables);
        JsonObject? declaredAsOf = Declared(view.Json, Name);
        JsonObject? declaredDate = Declared(view.Json, DateName);
        Note(Name, "instant", declaredAsOf is not null, variables, issues);
        Note(DateName, "day", declaredDate is not null, variables, issues);
        string day = asOf.Moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        AsOfFacts facts = InstantFacts(asOf, variables, declaredAsOf) with { Date = DateFacts(asOf, day, variables, declaredDate) };
        bool reads = Expressions(view.Json).Append(extraWhere).OfType<string>().Any(expression => ReadsClock().IsMatch(expression));

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

        if (declaredDate is null)
        {
            list.Add(new JsonObject { ["name"] = DateName, ["valueDate"] = day });
        }

        json["constant"] = list;
        return new Applied(view with { Json = json }, facts, reads);
    }

    private static AsOfFacts InstantFacts(AsOf asOf, IReadOnlyDictionary<string, string> variables, JsonObject? declared)
    {
        if (variables.TryGetValue(Name, out string? variable))
        {
            return new AsOfFacts(variable, AsOfSources.Variable);
        }

        if (declared is null)
        {
            return AsOfFacts.Of(asOf);
        }

        string? value = ConstantValue(declared);
        return FhirInstant.TryParse(value, out _)
            ? new AsOfFacts(value, AsOfSources.ViewConstant)
            : new AsOfFacts(null, AsOfSources.ViewConstant, value is null ? null : FhirIds.Digest(value));
    }

    private static AsOfDateFacts DateFacts(AsOf asOf, string day, IReadOnlyDictionary<string, string> variables, JsonObject? declared)
    {
        if (variables.TryGetValue(DateName, out string? variable))
        {
            return new AsOfDateFacts(variable, AsOfSources.Variable);
        }

        if (declared is null)
        {
            return new AsOfDateFacts(day, asOf.Source);
        }

        string? value = ConstantValue(declared);
        return value is not null && IsDate(value)
            ? new AsOfDateFacts(value, AsOfSources.ViewConstant)
            : new AsOfDateFacts(null, AsOfSources.ViewConstant, value is null ? null : FhirIds.Digest(value));
    }

    // The members of a ViewDefinition that hold FHIRPath: a column's or a where's path, forEach, forEachOrNull and repeat. Constants
    // are values, and names, titles and descriptions are text, so a mention of %asOf there reads nothing.
    private static IEnumerable<string> Expressions(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (string expression in array.SelectMany(Expressions))
            {
                yield return expression;
            }

            yield break;
        }

        if (node is not JsonObject json)
        {
            yield break;
        }

        foreach ((string key, JsonNode? value) in json)
        {
            if (key is "path" or "forEach" or "forEachOrNull" && value is JsonValue single && single.TryGetValue(out string? text))
            {
                yield return text;
            }
            else if (key == "repeat" && value is JsonArray repeats)
            {
                foreach (string path in repeats.OfType<JsonValue>().Select(item => item.TryGetValue(out string? repeat) ? repeat : null).OfType<string>())
                {
                    yield return path;
                }
            }
            else if (key != "constant")
            {
                foreach (string expression in Expressions(value))
                {
                    yield return expression;
                }
            }
        }
    }

    private static void CheckVariables(IReadOnlyDictionary<string, string> variables)
    {
        if (variables.TryGetValue(Name, out string? instant) && FhirInstant.Problem(instant, out _) is { } problem)
        {
            throw ToolRequestException.Error("invalid-parameter", $"Variable asOf must be an instant, as --as-of is (for a day, set asOfDate); the value given {Shown(instant)} is {problem}.");
        }

        if (variables.TryGetValue(DateName, out string? date) && !IsDate(date))
        {
            throw ToolRequestException.Error("invalid-parameter", $"Variable asOfDate must be a FHIR date, for example 2024-05-01. The value given {Shown(date)} is not one.");
        }
    }

    private static void Note(string name, string what, bool declared, IReadOnlyDictionary<string, string> variables, List<IssueItem> issues)
    {
        string fact = name == Name ? "facts.asOf" : "facts.asOf.date";
        if (variables.ContainsKey(name))
        {
            issues.Add(new IssueItem("warning", "as-of-overridden", $"The variable {name} replaces {(declared ? $"the ViewDefinition's own constant {name}" : $"the session's {what}")} as %{name} in this run (see {fact}).", Location: null));
        }
        else if (declared)
        {
            issues.Add(new IssueItem("warning", "as-of-shadowed", $"The ViewDefinition declares its own constant {name}, so %{name} is that constant's value, not the session's {what} (see {fact}); rename the constant to use %{name} as the {what} the data is read as of.", Location: null));
        }
    }

    private static JsonObject? Declared(JsonObject view, string name) => view["constant"] is JsonArray constants
        ? constants.OfType<JsonObject>().FirstOrDefault(constant => constant["name"] is JsonValue value && value.TryGetValue(out string? text) && text == name)
        : null;

    // The constant's value[x] as written; null when it has none.
    private static string? ConstantValue(JsonObject constant) => constant
        .Where(member => member.Key.StartsWith("value", StringComparison.Ordinal))
        .Select(member => member.Value)
        .OfType<JsonValue>()
        .Select(value => value.TryGetValue(out string? written) ? written : value.ToJsonString())
        .FirstOrDefault();

    private static bool IsDate(string value) => DateShape().IsMatch(value) && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static string Shown(string value) => DateTimeShape().IsMatch(value) ? $"'{value}'" : $"(digest {FhirIds.Digest(value)})";

    // %asOf or %asOfDate, plain or delimited.
    [GeneratedRegex(@"%(asOf|asOfDate|`asOf`|`asOfDate`)(?![A-Za-z0-9_])", RegexOptions.CultureInvariant)]
    private static partial Regex ReadsClock();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateShape();

    [GeneratedRegex(@"^[0-9TZ:.+\-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateTimeShape();
}
