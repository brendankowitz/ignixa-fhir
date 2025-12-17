// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace Ignixa.NarrativeGenerator.Localization;

/// <summary>
/// A string localizer that uses ResourceManager to load translations from embedded .resx files.
/// </summary>
internal sealed class ResourceManagerStringLocalizer : IStringLocalizer
{
    private readonly ResourceManager _resourceManager;
    private readonly CultureInfo? _culture;

    /// <summary>
    /// Creates a new ResourceManagerStringLocalizer.
    /// </summary>
    /// <param name="resourceManager">The ResourceManager to use for loading translations.</param>
    /// <param name="culture">Optional culture to use. If null, uses CurrentCulture.</param>
    public ResourceManagerStringLocalizer(ResourceManager resourceManager, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);
        _resourceManager = resourceManager;
        _culture = culture;
    }

    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            var culture = _culture ?? CultureInfo.CurrentCulture;
            var value = _resourceManager.GetString(name, culture);

            return new LocalizedString(name, value ?? name, resourceNotFound: value is null);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            var localizedString = this[name];

            if (localizedString.ResourceNotFound)
            {
                return localizedString;
            }

            var formatted = string.Format(
                _culture ?? CultureInfo.CurrentCulture,
                localizedString.Value,
                arguments);

            return new LocalizedString(name, formatted, resourceNotFound: false);
        }
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var culture = _culture ?? CultureInfo.CurrentCulture;
        var resourceSet = _resourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: includeParentCultures);

        if (resourceSet is null)
        {
            yield break;
        }

        foreach (System.Collections.DictionaryEntry entry in resourceSet)
        {
            var key = entry.Key?.ToString();
            var value = entry.Value?.ToString();

            if (key is not null && value is not null)
            {
                yield return new LocalizedString(key, value, resourceNotFound: false);
            }
        }
    }
}
