// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.DeId;

/// <summary>What a run did with the resources of one type.</summary>
/// <param name="ResourceType">The type.</param>
/// <param name="Processed">Resources of the type in scope.</param>
/// <param name="Written">Resources de-identified and written.</param>
/// <param name="Failed">Resources not written: unreadable, or not completely de-identified (each is an issue, capped).</param>
/// <param name="File">The file they were written to; absent when none was written.</param>
/// <param name="Bytes">Size of <paramref name="File"/>.</param>
public sealed record DeIdTypeFacts(
    string ResourceType,
    int Processed,
    int Written,
    int Failed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? File,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Bytes);
