// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Ignixa.DeId.Models;

/// <summary>
/// Predefined FHIR security labels for de-identification operations (REDACTED, ABSTRED, MASKED, etc.).
/// </summary>
public static class SecurityLabels
{
    public static readonly SecurityLabel REDACT = new(
        "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "REDACTED",
        "redacted");

    public static readonly SecurityLabel ABSTRED = new(
        "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "ABSTRED",
        "abstracted");

    public static readonly SecurityLabel CRYTOHASH = new(
        "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "CRYTOHASH",
        "cryptographic hash function");

    public static readonly SecurityLabel MASKED = new(
        "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "MASKED",
        "masked");

    public static readonly SecurityLabel PERTURBED = new(
        null,
        "PERTURBED",
        "exact value is replaced with another exact value");

    public static readonly SecurityLabel SUBSTITUTED = new(
        null,
        "SUBSTITUTED",
        "exact value is replaced with a predefined value");

    public static readonly SecurityLabel GENERALIZED = new(
        null,
        "GENERALIZED",
        "exact value is replaced with a general value");

    public static JsonObject ToJsonObject(this SecurityLabel label)
    {
        var obj = new JsonObject();
        if (label.System is not null)
        {
            obj["system"] = label.System;
        }
        obj["code"] = label.Code;
        obj["display"] = label.Display;
        return obj;
    }
}
