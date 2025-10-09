// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Json.Schema;

namespace Sparky.Specification.Schema;

internal static class JsonSchemaExtensions
{
    public static string ToName(this Uri reference)
    {
        return reference.ToString().Split('/').Last();
    }

    public static (string Name, JsonSchema Schema)? Lookup(this IReadOnlyDictionary<string, JsonSchema> schema, JsonSchema reference)
    {
        RefKeyword singleOrDefault = reference?.Keywords?.OfType<RefKeyword>().SingleOrDefault();

        if (singleOrDefault == null)
            singleOrDefault = reference?.Keywords?.OfType<ItemsKeyword>().SingleOrDefault()?.SingleSchema?.Keywords
                ?.OfType<RefKeyword>().SingleOrDefault();

        if (singleOrDefault == null) return null;

        string name = singleOrDefault.Reference.ToName();
        return (name, schema[name]);
    }
}
