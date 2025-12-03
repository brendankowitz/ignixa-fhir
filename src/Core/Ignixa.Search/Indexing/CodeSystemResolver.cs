// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Search.Generated;
using Ignixa.Serialization;

namespace Ignixa.Search.Indexing;

public sealed class CodeSystemResolver : ICodeSystemResolver
{
    private readonly Dictionary<string, string> _dictionary;

    public CodeSystemResolver(FhirVersion fhirVersion)
    {
        // Use pre-generated code system mappings to eliminate runtime JSON parsing overhead.
        // These mappings come from the FHIR specification and map resource paths like
        // "Account.status" to their canonical code system URLs like "http://hl7.org/fhir/account-status".
        _dictionary = fhirVersion switch
        {
            FhirVersion.R4 => R4CodeSystemMappings.GetMappings(),
            FhirVersion.R4B => R4BCodeSystemMappings.GetMappings(),
            FhirVersion.R5 => R5CodeSystemMappings.GetMappings(),
            FhirVersion.R6 => R6CodeSystemMappings.GetMappings(),
            FhirVersion.Stu3 => STU3CodeSystemMappings.GetMappings(),
            _ => throw new NotSupportedException($"FHIR version {fhirVersion} is not supported")
        };
    }

    public string ResolveSystem(string shortPath)
    {
        EnsureArg.IsNotNullOrWhiteSpace(shortPath, nameof(shortPath));

        if (_dictionary == null) throw new InvalidOperationException($"{nameof(CodeSystemResolver)} has not been initialized.");

        if (_dictionary.TryGetValue(NormalizePath(shortPath), out string system)) return system;

        return null;
    }

    private static string NormalizePath(string path)
    {
        return Regex.Replace(path, "\\[\\w+\\]", string.Empty);
    }
}
