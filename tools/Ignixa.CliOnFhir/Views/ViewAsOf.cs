// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Makes the session's <see cref="AsOf"/> instant the FHIRPath variable <c>%asOf</c> (a dateTime) of a view. The engine applies
/// a variable only where the ViewDefinition declares a constant of its name, and refuses a reference to one it does not
/// declare, so the run gives the view a constant <c>asOf</c> with the instant as its <c>valueDateTime</c>. A ViewDefinition
/// that declares its own <c>asOf</c> keeps it, with a warning that <c>%asOf</c> is then not the session's instant; a
/// <c>--var asOf=...</c> replaces the value either way, as a variable replaces any constant, with a warning.
/// </summary>
internal static class ViewAsOf
{
    /// <summary>The variable's name: <c>%asOf</c>.</summary>
    public const string Name = "asOf";

    /// <summary><paramref name="view"/> with the <c>asOf</c> constant added unless it declares one; the notes go to <paramref name="issues"/>.</summary>
    public static LoadedView With(LoadedView view, AsOf asOf, IReadOnlyDictionary<string, string> variables, List<IssueItem> issues)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(asOf);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(issues);

        bool declared = view.Json["constant"] is JsonArray constants
            && constants.OfType<JsonObject>().Any(constant => constant["name"] is JsonValue name && name.TryGetValue(out string? text) && text == Name);
        if (variables.ContainsKey(Name))
        {
            issues.Add(new IssueItem("warning", "as-of-overridden", $"The variable asOf replaces {(declared ? "the ViewDefinition's own constant asOf" : "the session's instant (facts.asOf)")} as %asOf in this run.", Location: null));
        }
        else if (declared)
        {
            issues.Add(new IssueItem("warning", "as-of-shadowed", "The ViewDefinition declares its own constant asOf, so %asOf is that constant's value, not the session's instant (facts.asOf); rename the constant to use %asOf as the instant the data is read as of.", Location: null));
        }

        // A constant member that is not an array is the ViewDefinition's own error, which compiling it reports as it is.
        if (declared || view.Json["constant"] is { } existing && existing is not JsonArray)
        {
            return view;
        }

        JsonObject json = (JsonObject)view.Json.DeepClone();
        JsonArray list = (JsonArray?)json["constant"] ?? [];
        list.Add(new JsonObject { ["name"] = Name, ["valueDateTime"] = asOf.Instant });
        json["constant"] = list;
        return view with { Json = json };
    }
}
