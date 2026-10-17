// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.DeId;

/// <summary>The configuration's rules of one method: the method and how many paths it is applied to, never the paths themselves.</summary>
/// <param name="Method">The de-identification method, for example <c>redact</c> or <c>cryptoHash</c>.</param>
/// <param name="Paths">How many of the configuration's rules use it.</param>
public sealed record DeIdRuleFacts(string Method, int Paths);
