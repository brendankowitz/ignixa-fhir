// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Caching;

/// <summary>Why a stored manifest no longer matches the current run.</summary>
/// <param name="Reason">Every difference found, in words.</param>
/// <param name="ChangedFiles">Files added, removed or modified since the cache was written.</param>
internal sealed record ManifestMismatch(string Reason, int ChangedFiles);
