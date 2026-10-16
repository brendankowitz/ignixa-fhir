// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.SqlOnFhir.Expressions;

namespace Ignixa.CliOnFhir.Views;

/// <summary>The <c>name=value</c> FHIRPath variables of a call, which override ViewDefinition constants.</summary>
internal static class ViewVariables
{
    /// <summary>The variables by name.</summary>
    /// <exception cref="ToolRequestException">An entry that is not <c>name=value</c>, or a name given twice.</exception>
    public static IReadOnlyDictionary<string, string> Parse(IReadOnlyList<string>? entries)
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);
        foreach (string entry in entries ?? [])
        {
            int equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                throw ToolRequestException.Error("invalid-parameter", $"Variable '{QueryEcho.Of(entry)}' is not name=value, for example cutoff=1990-01-01.");
            }

            if (!variables.TryAdd(entry[..equals], entry[(equals + 1)..]))
            {
                throw ToolRequestException.Error("invalid-parameter", $"Variable '{entry[..equals]}' is given more than once.");
            }
        }

        return variables;
    }

    /// <summary>
    /// A warning for each variable that names no constant of the view: the engine applies a variable only where the
    /// ViewDefinition declares a constant of that name, so one that names none changes nothing, and that must not pass unseen.
    /// </summary>
    public static IEnumerable<IssueItem> Undeclared(IReadOnlyDictionary<string, string> variables, ViewDefinitionExpression expression)
    {
        HashSet<string> declared = [.. expression.Constants.Select(constant => constant.Name)];
        foreach (string name in variables.Keys.Where(name => !declared.Contains(name)).Order(StringComparer.Ordinal))
        {
            yield return new IssueItem(
                "warning",
                "variable-not-declared",
                $"Variable '{name}' names no constant of the ViewDefinition, so it changed nothing; a variable overrides the value of a declared constant (add a constant '{name}' with a value[x] of the type it should have).",
                Location: null);
        }
    }
}
