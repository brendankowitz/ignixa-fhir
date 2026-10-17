// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>Whether a resource a draft refers to is in the folder and, for one other than the subject, is the patient's.</summary>
/// <param name="Reference">The reference, <c>Type/id</c>.</param>
/// <param name="Result"><c>found</c>, <c>not-found</c>, <c>other-patient</c> (its subject is another patient) or <c>subject-unknown</c> (it names no subject the tool can read).</param>
public sealed record ReferenceCheckDetails(string Reference, string Result);
