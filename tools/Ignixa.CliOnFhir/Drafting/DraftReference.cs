// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>A resource of the folder a draft refers to; it must be in the folder.</summary>
/// <param name="Element">Where the reference is in the draft, for example <c>MedicationRequest.encounter</c>.</param>
/// <param name="ResourceType">The type it names.</param>
/// <param name="Id">The id it names.</param>
/// <param name="IsSubject">Whether this is the draft's subject, the patient; any other referenced resource must be that patient's.</param>
internal sealed record DraftReference(string Element, string ResourceType, string Id, bool IsSubject)
{
    /// <summary><c>Type/id</c>.</summary>
    public string Reference => $"{ResourceType}/{Id}";
}
