// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>A "not found" result with the scope that was checked, so absence is never asserted beyond what was looked at.</summary>
/// <param name="What">What was looked for, for example <c>Patient/p9</c>.</param>
/// <param name="Checked">The domain that was checked, for example the catalog and how many resources of that type it holds.</param>
/// <param name="IndexBuiltAt">When the catalog or index that was checked finished building.</param>
public sealed record AbsentItem(string What, string Checked, DateTimeOffset IndexBuiltAt);
