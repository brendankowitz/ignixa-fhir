// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.FhirFakes;

internal static class FhirFakeTags
{
    public const string TestIsolationSystem = "http://ignixa.dev/test-isolation";

    public static JsonObject CreateTestIsolationCoding(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return new JsonObject
        {
            ["system"] = TestIsolationSystem,
            ["code"] = code
        };
    }
}
