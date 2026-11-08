// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Generated;
using Ignixa.Specification.Extensions;

namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// Regressions: a census of every generated code-system mapping in the five versions, checked against that version's
/// own schema bindings and packaged ValueSet codes rather than against the generator. Every row fails at bdb927b, whose
/// mappings named guessed <c>/CodeSystem/</c> URLs, ValueSet URLs, and CodeableConcept and Coding elements, except the
/// STU3 row of <see cref="GivenAVersionsMappings_WhenCensused_ThenNoMappedSystemIsAValueSet"/>: STU3 named no ValueSet
/// URL there, so that row is a guard (mutation-checked by restoring the generator's ValueSet-URL fallback).
/// </summary>
public class CodeSystemMappingCensusTests
{
    // Systems no core package defines as an enumerable CodeSystem, so a binding to them has no packaged codes to compare.
    private static readonly HashSet<string> ExternalSystems = new(StringComparer.Ordinal)
    {
        "urn:ietf:bcp:13",
        "urn:ietf:bcp:47",
    };

    public static TheoryData<FhirVersion> Versions => [FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6];

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenAVersionsMappings_WhenCensused_ThenNoMappedSystemIsAValueSet(FhirVersion version)
    {
        Dictionary<string, string> mappings = Mappings(version);

        mappings.ShouldNotBeEmpty();
        mappings.Where(mapping => mapping.Value.Contains("/ValueSet/", StringComparison.Ordinal))
            .Select(mapping => $"{mapping.Key} -> {mapping.Value}")
            .ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenAVersionsMappings_WhenCensused_ThenEachMapsABoundCodeToTheOneSystemOfItsValueSet(FhirVersion version)
    {
        IFhirSchemaProvider schema = version.GetSchemaProvider();
        Dictionary<string, string> mappings = Mappings(version);

        mappings.ShouldNotBeEmpty();
        mappings.Select(mapping => Problem(schema, mapping.Key, mapping.Value) is { } problem ? $"{mapping.Key} -> {mapping.Value}: {problem}" : null)
            .OfType<string>()
            .ShouldBeEmpty();
    }

    private static Dictionary<string, string> Mappings(FhirVersion version) => version switch
    {
        FhirVersion.Stu3 => STU3CodeSystemMappings.GetMappings(),
        FhirVersion.R4 => R4CodeSystemMappings.GetMappings(),
        FhirVersion.R4B => R4BCodeSystemMappings.GetMappings(),
        FhirVersion.R5 => R5CodeSystemMappings.GetMappings(),
        FhirVersion.R6 => R6CodeSystemMappings.GetMappings(),
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
    };

    private static string? Problem(IFhirSchemaProvider schema, string path, string system)
    {
        int dot = path.LastIndexOf('.');
        string name = path[(dot + 1)..];
        IType? element = schema.GetTypeDefinition(path[..dot])?.Children.SingleOrDefault(child => child.Info.Name == name);
        if (element is not ITypeExtended { Binding.ValueSet: { } valueSet } || element.Info.Primitive != FhirPrimitive.Code)
        {
            return "not a bound code element";
        }

        string[] systems = [.. (schema.ValueSetProvider.GetCodes(valueSet) ?? []).Select(code => code.System).Distinct(StringComparer.Ordinal)];
        return systems switch
        {
            [] when ExternalSystems.Contains(system) => null,
            [] => $"{valueSet} has no packaged codes and the system is not an allow-listed external one",
            [string only] when only == system => null,
            _ => $"{valueSet} holds codes from {string.Join(", ", systems)}",
        };
    }
}
