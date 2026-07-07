// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// examples (non-spec) mode: rejects URL-valued elements that point at a reserved example domain
/// (<c>example.org</c> / <c>acme.com</c>). Off unless <see cref="ValidationSettings.CheckExampleUrls"/>
/// is set — the reference validator permits example URLs in spec mode and flags them otherwise.
/// </summary>
public sealed class ExampleUrlCheck(string elementName) : IValidationCheck
{
    private static readonly FrozenSet<string> ExampleHosts =
        new[] { "example.org", "acme.com" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly string _elementName = elementName ?? throw new ArgumentNullException(nameof(elementName));

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (!settings.CheckExampleUrls)
        {
            return ValidationResult.Success();
        }

        List<ValidationIssue>? issues = null;
        foreach (var child in element.Children(_elementName))
        {
            if (!child.HasPrimitiveValue)
            {
                continue;
            }

            var text = child.Value?.ToString();
            if (!string.IsNullOrEmpty(text) && IsExampleUrl(text))
            {
                (issues ??= new List<ValidationIssue>()).Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "invalid",
                    child.Location,
                    $"Example URLs are not allowed in this context ({text})"));
            }
        }

        return issues is null ? ValidationResult.Success() : ValidationResult.Failure(issues);
    }

    private static bool IsExampleUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        foreach (var reserved in ExampleHosts)
        {
            if (string.Equals(host, reserved, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + reserved, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
