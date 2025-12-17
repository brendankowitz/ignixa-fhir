// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Ignixa.NarrativeGenerator.Localization;

/// <summary>
/// A string localizer that returns the key as-is without translation.
/// Used as a default when no localization is configured.
/// </summary>
internal sealed class NullStringLocalizer : IStringLocalizer
{
    /// <inheritdoc />
    public LocalizedString this[string name] =>
        new(name, name, resourceNotFound: true);

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(CultureInfo.CurrentCulture, name, arguments), resourceNotFound: true);

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        Enumerable.Empty<LocalizedString>();
}
