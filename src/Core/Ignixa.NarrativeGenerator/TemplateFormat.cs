// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.NarrativeGenerator;

/// <summary>
/// Supported template output formats for narrative generation.
/// </summary>
public enum TemplateFormat
{
    /// <summary>
    /// XHTML format for FHIR Narrative.div (requires xmlns, limited HTML elements).
    /// This is the default format for standard FHIR narrative generation.
    /// </summary>
    Html,

    /// <summary>
    /// Markdown format for human-readable documentation and display.
    /// Useful for generating documentation, clinical summaries, or display in Markdown-aware systems.
    /// </summary>
    Markdown,

    /// <summary>
    /// Single-line compact format for creating patient record vector embeddings.
    /// Produces dense, token-efficient text suitable for AI/ML embedding models.
    /// Example: "45yo Male with [Conditions]. Medications: [list]. Labs: [values]."
    /// </summary>
    Compact
}
