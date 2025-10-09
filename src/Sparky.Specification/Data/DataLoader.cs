// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Sparky.Extensions;

namespace Sparky.Specification.Data;

public static class DataLoader
{
    private static readonly string ThisNamespace = typeof(DataLoader).Namespace;
    private static readonly Assembly ThisAssembly = typeof(DataLoader).Assembly;

    public static Stream OpenVersionedFileStream(FhirSpecification fhirVersion, string filename, string @namespace = null, Assembly assembly = null)
    {
        string manifestName = $"{@namespace ?? ThisNamespace}.{fhirVersion}.{filename}";
        return (assembly ?? ThisAssembly).GetManifestResourceStream(manifestName);
    }
}
