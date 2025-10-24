// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;
using Ignixa.Domain.Constants;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Search.Generated;

namespace Ignixa.Search.Definition;

/// <summary>
/// Manager to access compartment definitions.
/// </summary>
public class CompartmentDefinitionManager : ICompartmentDefinitionManager
{
    private readonly Dictionary<CompartmentType, HashSet<string>> _compartmentResourceTypesLookup;

    // This data structure stores the lookup of compartmentSearchParams (in the hash set) by ResourceType and CompartmentType.
    private readonly Dictionary<string, Dictionary<CompartmentType, HashSet<string>>> _compartmentSearchParamsLookup;

    public CompartmentDefinitionManager(FhirSpecification fhirSpecification)
    {
        // Load pre-generated compartment definitions to eliminate runtime JSON parsing overhead.
        // The compartment definitions are compiled from the official HL7 definitions available at
        // https://www.hl7.org/fhir/compartmentdefinition.html.
        Dictionary<CompartmentType, (CompartmentType Code, Uri Url, IList<(string Resource, IList<string> Params)> Resources)> compartments =
            fhirSpecification switch
            {
                FhirSpecification.R4 => R4CompartmentDefinitions.GetCompartments(),
                FhirSpecification.R4B => R4BCompartmentDefinitions.GetCompartments(),
                FhirSpecification.R5 => R5CompartmentDefinitions.GetCompartments(),
                FhirSpecification.R6 => R6CompartmentDefinitions.GetCompartments(),
                FhirSpecification.Stu3 => STU3CompartmentDefinitions.GetCompartments(),
                _ => throw new NotSupportedException($"FHIR version {fhirSpecification} is not supported")
            };

        (_compartmentSearchParamsLookup, _compartmentResourceTypesLookup) = BuildFromGenerated(compartments);
    }

    public static Dictionary<string, CompartmentType> ResourceTypeToCompartmentType { get; } = new()
    {
        { KnownResourceTypes.Device, CompartmentType.Device },
        { KnownResourceTypes.Encounter, CompartmentType.Encounter },
        { KnownResourceTypes.Patient, CompartmentType.Patient },
        { KnownResourceTypes.Practitioner, CompartmentType.Practitioner },
        { KnownResourceTypes.RelatedPerson, CompartmentType.RelatedPerson }
    };

    public bool TryGetSearchParams(string resourceType, CompartmentType compartmentType, out HashSet<string> searchParams)
    {
        if (_compartmentSearchParamsLookup.TryGetValue(resourceType, out Dictionary<CompartmentType, HashSet<string>> compartmentSearchParams)
            && compartmentSearchParams.TryGetValue(compartmentType, out searchParams))
            return true;

        searchParams = null;
        return false;
    }

    public bool TryGetResourceTypes(CompartmentType compartmentType, out HashSet<string> resourceTypes)
    {
        if (_compartmentResourceTypesLookup.TryGetValue(compartmentType, out resourceTypes)) return true;

        resourceTypes = null;
        return false;
    }

    public static string CompartmentTypeToResourceType(string compartmentType)
    {
        EnsureArg.IsTrue(Enum.IsDefined(typeof(CompartmentType), compartmentType), nameof(compartmentType));
        return compartmentType;
    }

    /// <summary>
    /// Builds lookup dictionaries from pre-generated compartment definitions.
    /// </summary>
    private (Dictionary<string, Dictionary<CompartmentType, HashSet<string>>> SearchParams, Dictionary<CompartmentType, HashSet<string>> ResourceTypes) BuildFromGenerated(
        Dictionary<CompartmentType, (CompartmentType Code, Uri Url, IList<(string Resource, IList<string> Params)> Resources)> compartmentLookup)
    {
        var searchParams = BuildResourceTypeLookup(compartmentLookup.Values);
        var resourceTypes = new Dictionary<CompartmentType, HashSet<string>>();

        foreach ((CompartmentType key, (CompartmentType Code, Uri Url, IList<(string Resource, IList<string> Params)> Resources) value) in compartmentLookup)
        {
            resourceTypes[key] = value.Resources.Where(x => x.Params.Any()).Select(x => x.Resource).ToHashSet();
        }

        return (searchParams, resourceTypes);
    }

    private static Dictionary<string, Dictionary<CompartmentType, HashSet<string>>> BuildResourceTypeLookup(ICollection<(CompartmentType Code, Uri Url, IList<(string Resource, IList<string> Params)> Resources)> compartmentDefinitions)
    {
        var resourceTypeParamsByCompartmentDictionary = new Dictionary<string, Dictionary<CompartmentType, HashSet<string>>>();

        foreach ((CompartmentType Code, Uri Url, IList<(string Resource, IList<string> Params)> Resources) compartment in compartmentDefinitions)
        foreach ((string Resource, IList<string> Params) resource in compartment.Resources)
        {
            if (!resourceTypeParamsByCompartmentDictionary.TryGetValue(resource.Resource, out Dictionary<CompartmentType, HashSet<string>> resourceTypeDict))
            {
                resourceTypeDict = new Dictionary<CompartmentType, HashSet<string>>();
                resourceTypeParamsByCompartmentDictionary.Add(resource.Resource, resourceTypeDict);
            }

            resourceTypeDict[compartment.Code] = resource.Params?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return resourceTypeParamsByCompartmentDictionary;
    }
}
