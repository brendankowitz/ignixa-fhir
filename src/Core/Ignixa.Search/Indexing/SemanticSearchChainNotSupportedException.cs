// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Indexing;

/// <summary>
/// Thrown when a chain (forward, or reverse via <c>_has</c>) terminates in a semantic search
/// parameter, for example <c>subject:Patient.semantic-text=x</c>: a vector embedding has no reference
/// identity to chain through.
/// </summary>
/// <remarks>
/// <para>
/// Derives from <see cref="SearchModifierNotSupportedException"/> purely to keep
/// <see cref="Parsing.SearchOptionsBuilder"/>'s existing catch-order routing into
/// <see cref="Models.SearchOptions.UnsupportedModifierParams"/>: a chain into a semantic parameter has
/// the same "silently widens instead of narrows" failure mode as an unsupported modifier, so it earns
/// the same SHALL-reject-by-default treatment.
/// </para>
/// <para>
/// It is a distinct subclass, not a plain <see cref="SearchModifierNotSupportedException"/>, so
/// <see cref="Parsing.SearchOptionsBuilder"/> can record this exception's message as the rejection
/// reason for the parameter without changing the diagnostics text produced for a genuine unsupported
/// modifier. The query <c>subject:Patient.semantic-text=x</c> did not use an unsupported modifier --
/// <c>:Patient</c> is a perfectly valid reference type qualifier -- so the generic "uses a modifier that
/// is not supported" wording misreports the actual reason: the chain terminates in a semantic parameter,
/// which cannot be chained through at all.
/// </para>
/// </remarks>
public sealed class SemanticSearchChainNotSupportedException : SearchModifierNotSupportedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticSearchChainNotSupportedException"/> class.
    /// </summary>
    /// <param name="message">The message describing why the chain was rejected.</param>
    public SemanticSearchChainNotSupportedException(string message)
        : base(message)
    {
    }
}
