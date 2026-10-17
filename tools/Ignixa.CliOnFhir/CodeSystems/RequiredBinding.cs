// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>
/// Whether a search parameter reads only elements the FHIR version binds to a closed value set (binding strength
/// <c>required</c>: Condition.clinicalStatus, Condition.verificationStatus, Observation.status). Looking a code up in such a
/// parameter is a choice among that set's few values, not a choice of code system.
/// </summary>
internal static class RequiredBinding
{
    /// <summary>
    /// True when <paramref name="expression"/> is one or more plain <c>Type.element</c> paths (joined by <c>|</c>, optionally in
    /// parentheses) and every element has a required binding in <paramref name="schema"/>. Any other expression shape is not
    /// decided, and so is false.
    /// </summary>
    public static bool Covers(IFhirSchemaProvider schema, string? expression)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        string[] paths = expression.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return paths.Length > 0 && paths.All(path => IsRequired(schema, path.Trim('(', ')', ' ')));
    }

    private static bool IsRequired(IFhirSchemaProvider schema, string path)
    {
        string[] parts = path.Split('.');
        return parts.Length == 2
            && schema.GetTypeDefinition(parts[0])?.Children.FirstOrDefault(child => child.Info.Name == parts[1])
                is ITypeExtended { Binding: { ValueSet: { Length: > 0 } } binding }
            && string.Equals(binding.Strength, "required", StringComparison.OrdinalIgnoreCase);
    }
}
