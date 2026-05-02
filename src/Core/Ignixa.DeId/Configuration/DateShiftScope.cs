// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Ignixa.DeId.Configuration;

/// <summary>
/// Defines the scope at which date shift offsets are applied.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DateShiftScope
{
    [EnumMember(Value = "resource")]
    Resource,
    [EnumMember(Value = "file")]
    File,
    [EnumMember(Value = "folder")]
    Folder
}
