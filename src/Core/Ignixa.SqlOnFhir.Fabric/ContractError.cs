// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Creates the rejection raised for a creation log that is malformed or not the supported contract. Reasons name
/// the offending structure by position and by fixed contract terms only, never by values read from the log, so
/// that a failure cannot leak schema contents.
/// </summary>
internal static class ContractError
{
    public static InvalidDataException Because(string reason) =>
        new($"The Silver creation log is not a supported contract: {reason}");
}
