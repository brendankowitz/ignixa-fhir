// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates <c>extension.url</c> / <c>modifierExtension.url</c> against the identity rules the HL7
/// reference validator enforces on the url itself — independent of whether the extension definition
/// can be resolved.
/// </summary>
/// <remarks>
/// <para>
/// Enforces one closed-world, terminology- and definition-independent rule: an extension url MUST NOT
/// use a reserved example domain (RFC 2606 <c>example.org</c>/<c>com</c>/<c>net</c>). An extension url
/// is a canonical identity that must resolve to a real definition; a reserved example host can never
/// do so, which is why the reference validator rejects it with "Example URLs are not allowed in this
/// context".
/// </para>
/// <para>
/// Scoped to primitive-element (shadow <c>_field</c>) extensions. The reference validator accepts the
/// same example.org host on a resource's root extensions when the resource is a matchetype template,
/// so unconditional enforcement is over-strict. The primitive-shadow position is where the diagnostic
/// is confirmed and cannot collide with those templates. Broader enforcement is deferred until
/// extension definitions resolve (below), which would let the check distinguish the two by definition
/// rather than by JSON position.
/// </para>
/// <para>
/// Deliberately silent on unresolvable extensions. In the definition-independent pipeline no
/// extension StructureDefinitions are loaded, so "cannot resolve" carries no signal — treating it as
/// an error would reject every resource carrying a core or vendor extension. Definition-dependent
/// checks (context applicability, value[x] typing) belong with a resolver that can distinguish
/// "genuinely unknown" from "simply not loaded"; until then this check stays silent rather than guess.
/// </para>
/// <para>
/// Closed-world raw-JSON walk (mirrors <see cref="ExtensionUrlVersionCheck"/>) since extensions can
/// appear at any depth, including primitive shadow properties (<c>_birthDate.extension</c>).
/// Registered in the profile (Full) tier so Compatibility depth is unaffected.
/// </para>
/// </remarks>
public sealed class ExtensionDefinitionCheck : IValidationCheck, ISingletonCheck
{
    private static readonly FrozenSet<string> ReservedExampleHosts =
        new[] { "example.org", "example.com", "example.net" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (settings.Depth < ValidationDepth.Full)
        {
            return ValidationResult.Success();
        }

        if (element.Meta<JsonNode>() is not JsonObject root)
        {
            return ValidationResult.Success();
        }

        var issues = new List<ValidationIssue>();
        WalkObject(root, element.InstanceType, onPrimitiveShadow: false, issues);

        return issues.Count > 0 ? ValidationResult.Failure(issues) : ValidationResult.Success();
    }

    private static void WalkObject(JsonObject obj, string path, bool onPrimitiveShadow, List<ValidationIssue> issues)
    {
        foreach (var (key, value) in obj)
        {
            // Contained resources are validated independently against their own schema, which applies
            // this same rule; walking into them here would duplicate the diagnostic.
            if (value is null || string.Equals(key, "contained", StringComparison.Ordinal))
            {
                continue;
            }

            var childPath = $"{path}.{key}";
            var isExtensionArray = key is "extension" or "modifierExtension";

            // A "_field" shadow object carries the extensions of a primitive element (e.g. _birthDate).
            // Enforcement is scoped to this position (see WalkArray) so we track the descent into it.
            var childOnShadow = onPrimitiveShadow || key.StartsWith('_');

            switch (value)
            {
                case JsonArray array:
                    WalkArray(array, childPath, isExtensionArray, childOnShadow, issues);
                    break;
                case JsonObject nested:
                    WalkObject(nested, childPath, childOnShadow, issues);
                    break;
            }
        }
    }

    private static void WalkArray(JsonArray array, string path, bool isExtensionArray, bool onPrimitiveShadow, List<ValidationIssue> issues)
    {
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject item)
            {
                continue;
            }

            // Scoped deliberately to primitive-element (shadow "_field") extensions, the one position
            // where the reference validator's example-url diagnostic is confirmed and cannot collide
            // with matchetype template resources (whose root example.org extensions it accepts).
            // Broader enforcement is deferred until extension definitions resolve — see class remarks.
            if (isExtensionArray
                && onPrimitiveShadow
                && item["url"] is JsonValue urlValue
                && urlValue.ToString() is { Length: > 0 } url
                && IsReservedExampleUrl(url))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "ext-example-url",
                    $"Example URLs are not allowed in this context ({url})",
                    $"{path}[{i}].url"));
            }

            WalkObject(item, $"{path}[{i}]", onPrimitiveShadow, issues);
        }
    }

    private static bool IsReservedExampleUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        if (ReservedExampleHosts.Contains(host))
        {
            return true;
        }

        // Subdomains of a reserved host (e.g. fhir.example.org) are equally reserved.
        foreach (var reserved in ReservedExampleHosts)
        {
            if (host.EndsWith("." + reserved, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
