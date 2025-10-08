// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Extensions;
using Microsoft.Health.Fhir.Extensions.Exceptions;
using Microsoft.Health.Fhir.Extensions.Models;
using Microsoft.Health.Fhir.Extensions.Schema;
using Microsoft.Health.Fhir.SourceNodeSerialization;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;
using Microsoft.Health.Fhir.Extensions.ValueSets;
using Microsoft.Health.Fhir.Search.Extensions.Data;
using Microsoft.Health.Fhir.Search.Extensions.Indexing.Converters;

namespace Microsoft.Health.Fhir.Search.Extensions.Definition;

/// <summary>
/// Manager to access compartment definitions.
/// </summary>
public class CompartmentDefinitionManager : ICompartmentDefinitionManager
{
    private readonly FhirSpecification _fhirSpecification;
    private Dictionary<CompartmentType, HashSet<string>> _compartmentResourceTypesLookup;

    // This data structure stores the lookup of compartmentSearchParams (in the hash set) by ResourceType and CompartmentType.
    private Dictionary<string, Dictionary<CompartmentType, HashSet<string>>> _compartmentSearchParamsLookup;

    public CompartmentDefinitionManager(FhirSpecification fhirSpecification)
    {
        _fhirSpecification = fhirSpecification;
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The json file is a bundle compiled from the compartment definitions currently defined by HL7.
        // The definitions are available at https://www.hl7.org/fhir/compartmentdefinition.html.
        using Stream stream = DataLoader.OpenVersionedFileStream(_fhirSpecification, "compartment.json");
        BundleJsonNode bundle = await JsonSourceNodeFactory.Parse<BundleJsonNode>(stream);
        Build(bundle);
    }

    public static string CompartmentTypeToResourceType(string compartmentType)
    {
        EnsureArg.IsTrue(Enum.IsDefined(typeof(CompartmentType), compartmentType), nameof(compartmentType));
        return compartmentType;
    }

    internal void Build(BundleJsonNode bundle)
    {
        Dictionary<CompartmentType, (CompartmentType Code, Uri Url, IList<(string Resource, IList<string> Params)> Resources)> compartmentLookup = ValidateAndGetCompartmentDict(bundle);
        _compartmentSearchParamsLookup = BuildResourceTypeLookup(compartmentLookup.Values);
        _compartmentResourceTypesLookup = new Dictionary<CompartmentType, HashSet<string>>();
        foreach ((CompartmentType key, (CompartmentType Code, Uri Url, IList<(string Resource, IList<string> Params)> Resources) value) in compartmentLookup) _compartmentResourceTypesLookup[key] = value.Resources.Where(x => x.Params.Any()).Select(x => x.Resource).ToHashSet();
    }

    private static Dictionary<CompartmentType, (CompartmentType Code, Uri Url, IList<(string Resource, IList<string> Params)> Resources)> ValidateAndGetCompartmentDict(BundleJsonNode bundle)
    {
        EnsureArg.IsNotNull(bundle, nameof(bundle));

        var issues = new List<OperationOutcomeIssue>();
        var validatedCompartments = new Dictionary<CompartmentType, (CompartmentType, Uri, IList<(string, IList<string>)>)>();

        IList<BundleComponentJsonNode> entries = bundle.Entry;

        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            // Make sure resources are not null and they are Compartment.
            BundleComponentJsonNode entry = entries[entryIndex];

            ISourceNode sourceNode = JsonSourceNodeFactory.Create(entry.Resource);
            ITypedElement compartment = sourceNode.ToTypedElement(InstanceInferredStructureDefinitionSummaryProvider.CreateFrom(sourceNode));

            if (compartment == null || !string.Equals(KnownResourceTypes.CompartmentDefinition, compartment.InstanceType, StringComparison.Ordinal))
            {
                AddIssue(Resources.CompartmentDefinitionInvalidResource, entryIndex);
                continue;
            }

            string code = compartment.Scalar("code")?.ToString();
            string url = compartment.Scalar("url")?.ToString();

            if (code == null)
            {
                AddIssue(Resources.CompartmentDefinitionInvalidCompartmentType, entryIndex);
                continue;
            }

            CompartmentType typeCode = EnumUtility.ParseLiteral<CompartmentType>(code).GetValueOrDefault();

            if (validatedCompartments.ContainsKey(typeCode))
            {
                AddIssue(Resources.CompartmentDefinitionIsDupe, entryIndex);
                continue;
            }

            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                AddIssue(Resources.CompartmentDefinitionInvalidUrl, entryIndex);
                continue;
            }

            var resources = compartment.Select("resource")
                .Select(x => (x.Scalar("code")?.ToString(), (IList<string>)x.Select("param").AsStringValues().ToList()))
                .ToList();

            string[] resourceNames = resources.Select(x => x.Item1).ToArray();

            if (resourceNames.Length != resourceNames.Distinct().Count())
            {
                AddIssue(Resources.CompartmentDefinitionDupeResource, entryIndex);
                continue;
            }

            validatedCompartments.Add(
                typeCode,
                (typeCode, new Uri(url), new List<(string, IList<string>)>(resources)));
        }

        if (issues.Count != 0)
            throw new InvalidDefinitionException(
                Resources.CompartmentDefinitionContainsInvalidEntry,
                issues.ToArray());

        return validatedCompartments;

        void AddIssue(string format, params object[] args)
        {
            issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Fatal,
                OperationOutcomeConstants.IssueType.Invalid,
                string.Format(CultureInfo.InvariantCulture, format, args)));
        }
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
