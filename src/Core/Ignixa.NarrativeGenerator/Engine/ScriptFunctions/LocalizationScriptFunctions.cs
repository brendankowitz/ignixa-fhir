// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Localization;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Ignixa.NarrativeGenerator.Engine.ScriptFunctions;

/// <summary>
/// Provides localization functions for Scriban templates in narrative generation.
/// </summary>
/// <remarks>
/// <para>
/// This class exposes localization capabilities to Scriban templates. Usage:
/// </para>
/// <code>
/// {{ l10n.t "Patient.Name" }}
/// {{ l10n.format "Patient.AgeFormat" age }}
/// {{ l10n.lang }}
/// </code>
/// </remarks>
public class LocalizationScriptFunctions : ScriptObject
{
    private readonly IStringLocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationScriptFunctions"/> class.
    /// </summary>
    /// <param name="localizer">The string localizer for accessing localized strings.</param>
    public LocalizationScriptFunctions(IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;

        // Register methods as callable members
        // Scriban requires functions to be registered as lambda expressions
        this["t"] = (Func<string, string>)(key => T(key));
        this["format"] = (Func<string, object[], string>)((key, args) => Format(key, args));
        this["get_or_default"] = (Func<string, string, string>)((key, defaultValue) => GetOrDefault(key, defaultValue));
        this["lang"] = (Func<string>)(() => Lang());
        this["culture"] = (Func<string>)(() => Culture());
    }

    /// <summary>
    /// Translates a localization key to the current culture.
    /// </summary>
    /// <param name="key">The localization key (e.g., "Patient.Name").</param>
    /// <returns>The localized string value.</returns>
    /// <example>
    /// {{ l10n.t "Patient.Name" }}
    /// </example>
    public string T(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return _localizer[key].Value;
    }

    /// <summary>
    /// Translates a localization key with format arguments.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted localized string.</returns>
    /// <example>
    /// {{ l10n.format "Patient.AgeFormat" age }}
    /// </example>
    public string Format(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return _localizer[key, args].Value;
    }

    /// <summary>
    /// Gets a localized string by key, returning a default value if not found.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <param name="defaultValue">The default value if the key is not found.</param>
    /// <returns>The localized string or the default value.</returns>
    /// <example>
    /// {{ l10n.get_or_default "Custom.Label" "Default Label" }}
    /// </example>
    public string GetOrDefault(string key, string defaultValue)
    {
        if (string.IsNullOrEmpty(key))
        {
            return defaultValue;
        }

        var localized = _localizer[key];
        return localized.ResourceNotFound ? defaultValue : localized.Value;
    }

    /// <summary>
    /// Returns the current culture's two-letter ISO language code.
    /// </summary>
    /// <returns>The two-letter ISO language code (e.g., "en", "es").</returns>
    /// <example>
    /// {{ l10n.lang }}
    /// </example>
    public static string Lang()
    {
        return CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
    }

    /// <summary>
    /// Returns the current culture's full name.
    /// </summary>
    /// <returns>The full culture name (e.g., "en-US", "es-ES").</returns>
    /// <example>
    /// {{ l10n.culture }}
    /// </example>
    public static string Culture()
    {
        return CultureInfo.CurrentCulture.Name;
    }
}
