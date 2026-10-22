// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

/// <summary>
/// One terminology store (SQL, package content, later a remote server). A source reports facts about
/// the canonicals it holds; it never decides severity and never answers for content it does not hold.
/// </summary>
public interface ITerminologySource
{
    string Name { get; }

    ValueTask<SourceCoverage> GetCoverageAsync(CanonicalKind kind, CanonicalRef canonical, CancellationToken cancellationToken);
}
