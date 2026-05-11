// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Cli;

internal static class VarParser
{
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string>? vars)
    {
        if (vars is null)
            return new Dictionary<string, string>();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in vars)
        {
            var idx = v.IndexOf('=', StringComparison.Ordinal);
            if (idx <= 0)
                throw new ArgumentException($"Invalid --var format '{v}'. Expected name=value.");
            result[v[..idx]] = v[(idx + 1)..];
        }
        return result;
    }
}
