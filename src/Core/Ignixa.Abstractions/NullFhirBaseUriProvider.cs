// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// An <see cref="IFhirBaseUriProvider"/> that knows no base, so every absolute reference stays external.
/// </summary>
/// <remarks>
/// Exists so that "this caller has no server base" is written down rather than expressed as a null
/// argument. The provider used to be optional at four seams; a forgotten wiring then produced references
/// stored in a different form from the rest of the system, with no error to point at. Requiring the
/// dependency turns that into a compile error and leaves this as the one deliberate opt-out.
/// </remarks>
public sealed class NullFhirBaseUriProvider : IFhirBaseUriProvider
{
    public static NullFhirBaseUriProvider Instance { get; } = new();

    private NullFhirBaseUriProvider()
    {
    }

    /// <inheritdoc />
    public Uri? GetBaseUri() => null;

    /// <inheritdoc />
    public IReadOnlyList<Uri> GetServiceBaseUris() => [];

    /// <inheritdoc />
    public bool IsServiceBaseUri(Uri? candidate) => false;
}
