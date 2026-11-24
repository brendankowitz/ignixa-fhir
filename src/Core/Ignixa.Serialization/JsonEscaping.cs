// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Serialization;

/// <summary>
/// How strings and property names are escaped when serializing FHIR JSON.
/// </summary>
public enum JsonEscaping
{
    /// <summary>
    /// Escape only what Newtonsoft.Json escapes by default; see <see cref="MinimalJsonEncoder"/>.
    /// </summary>
    Minimal = 0,

    /// <summary>
    /// Also escape HTML-sensitive and all non-ASCII characters, using
    /// <see cref="System.Text.Encodings.Web.JavaScriptEncoder.Default"/>; for JSON embedded in HTML.
    /// </summary>
    HtmlSafe = 1,
}
