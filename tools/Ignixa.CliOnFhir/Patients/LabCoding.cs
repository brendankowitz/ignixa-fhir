// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The coding an Observation's lab series is keyed by (<see cref="ClinicalRules.LabKeyCoding"/>).</summary>
/// <param name="System">The coding's system.</param>
/// <param name="Code">The coding's code.</param>
internal sealed record LabCoding(string System, string Code)
{
    /// <summary>The key coding of <paramref name="observation"/>, or null when no coding has both a system and a code.</summary>
    public static LabCoding? Of(IElement observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        IElement? coding = observation.Select(ClinicalRules.LabKeyCoding).FirstOrDefault();
        return coding?.Scalar("system") is string system && coding.Scalar("code") is string code ? new LabCoding(system, code) : null;
    }

    /// <summary>As <c>system|code</c>.</summary>
    public override string ToString() => $"{System}|{Code}";
}
