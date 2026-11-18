// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Evaluation;

namespace Ignixa.Application.Features.Experimental.Ips;

/// <summary>
/// Builds the evaluation context for StructureDefinition snapshot lookups by element path.
/// Slice names come from installed packages, so they are bound as <c>%path</c> instead of being
/// interpolated into the expression text, where a quote in the name would be parsed as syntax.
/// </summary>
internal static class StructureDefinitionPathContext
{
    public static EvaluationContext For(string path) =>
        new EvaluationContext().WithEnvironmentVariable("path", path);
}
