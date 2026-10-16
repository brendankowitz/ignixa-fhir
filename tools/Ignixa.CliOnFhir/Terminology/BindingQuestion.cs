// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>A binding question for the terminology server: is <paramref name="Code"/> in <paramref name="ValueSet"/>.</summary>
internal readonly record struct BindingQuestion(string ValueSet, string? System, string Code, string? Version);
