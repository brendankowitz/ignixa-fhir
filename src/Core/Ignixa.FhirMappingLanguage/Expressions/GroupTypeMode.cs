/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Group type mode annotation for FHIR Mapping Language groups.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Indicates how a group participates in type-directed rule selection,
/// declared in FML as a &lt;&lt;types&gt;&gt; or &lt;&lt;type+&gt;&gt; annotation.
/// </summary>
public enum GroupTypeMode
{
    /// <summary>No annotation present. The group is only invoked by explicit reference.</summary>
    None = 0,

    /// <summary>
    /// Declared &lt;&lt;types&gt;&gt;: the group is the default mapping group for the specified
    /// types <em>and</em> for the primary source type.
    /// </summary>
    Types,

    /// <summary>
    /// Declared &lt;&lt;type+&gt;&gt;: the group is the default mapping group for the specified types.
    /// </summary>
    TypeAndTypes
}
