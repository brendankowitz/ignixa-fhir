// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>A CodeableConcept as the patient tools use it, read with FHIRPath.</summary>
/// <param name="Key">The first coding with both a system and a code, as <c>system|code</c>; null when there is none.</param>
/// <param name="Display">The concept's <c>text</c>, else its first coding <c>display</c>; null when it has neither.</param>
/// <param name="Text">The concept's <c>text</c>.</param>
/// <param name="Codings">Every coding's system, code and display, in order.</param>
internal sealed record CodedConcept(string? Key, string? Display, string? Text, IReadOnlyList<(string? System, string? Code, string? Display)> Codings)
{
    /// <summary>Reads <paramref name="concept"/>, a CodeableConcept element.</summary>
    public static CodedConcept Read(IElement concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        List<(string? System, string? Code, string? Display)> codings =
        [
            .. concept.Select("coding").Select(coding => (coding.Scalar("system") as string, coding.Scalar("code") as string, coding.Scalar("display") as string)),
        ];
        string? text = concept.Scalar("text") as string;
        (string? System, string? Code, string? Display) keyed = codings.FirstOrDefault(coding => coding.System is not null && coding.Code is not null);
        string? key = keyed.Code is null ? null : $"{keyed.System}|{keyed.Code}";
        return new CodedConcept(key, text ?? codings.Select(coding => coding.Display).FirstOrDefault(display => display is not null), text, codings);
    }

    /// <summary>The main codes of <paramref name="resource"/> by <see cref="ClinicalRules.MainCode"/>; empty for a type without one.</summary>
    public static IReadOnlyList<CodedConcept> MainCodes(string resourceType, IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return ClinicalRules.MainCode.TryGetValue(resourceType, out string? expression)
            ? [.. resource.Select(expression).Select(Read)]
            : [];
    }

    /// <summary>Whether any coding has <paramref name="code"/>, and <paramref name="system"/> when it is not null (an empty one means "no system").</summary>
    public bool HasCode(string? system, string code) =>
        Codings.Any(coding => coding.Code == code && (system is null || (system.Length == 0 ? coding.System is null : coding.System == system)));

    /// <summary>Whether the text or a coding display contains <paramref name="text"/>, ignoring case.</summary>
    public bool Mentions(string text) =>
        (Text?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
        || Codings.Any(coding => coding.Display?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
}
