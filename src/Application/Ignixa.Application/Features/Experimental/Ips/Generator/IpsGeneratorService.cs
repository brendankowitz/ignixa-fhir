// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable CA1308 // Normalize strings to uppercase - we intentionally use lowercase for user-friendly display

using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.NarrativeGenerator;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Ips.Generator;

/// <summary>
/// Service for generating International Patient Summary (IPS) documents.
/// </summary>
public class IpsGeneratorService(
    IEnumerable<IIpsGenerationStrategy> strategies,
    IQueryExecutionStrategy executionStrategy,
    IFhirRepositoryFactory repositoryFactory,
    IPartitionStrategy partitionStrategy,
    IFhirRequestContextAccessor contextAccessor,
    INarrativeGenerator narrativeGenerator,
    ISchema schema,
    ILogger<IpsGeneratorService> logger) : IIpsGeneratorService
{
    /// <summary>
    /// Default maximum number of resources to include in an IPS document.
    /// </summary>
    private const int DefaultMaxIpsResources = 1000;

    private readonly FrozenDictionary<string, IIpsGenerationStrategy> _strategyByProfile = strategies.ToFrozenDictionary(s => s.BundleProfile, s => s);
    private readonly IIpsGenerationStrategy _defaultStrategy = strategies.FirstOrDefault(s => s.BundleProfile == IpsConstants.DefaultBundleProfile)
        ?? strategies.First();

    /// <inheritdoc />
    public async Task<BundleJsonNode> GenerateIpsAsync(
        string patientId,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var strategy = SelectStrategy(profile);

        var requestContext = contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        var partitionId = requestContext.TenantId;
        var repository = await repositoryFactory.GetRepositoryAsync(partitionId, cancellationToken);

        // 1. Fetch patient
        var patientKey = new ResourceKey("Patient", patientId);
        var patientResult = await repository.GetAsync(patientKey, cancellationToken);

        if (patientResult is null)
        {
            throw new ResourceNotFoundException($"Patient/{patientId} not found");
        }

        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(patientResult.ResourceBytes);

        var context = new IpsContext
        {
            PatientId = patientId,
            Patient = patient,
            Strategy = strategy,
            PartitionId = partitionId,
            GenerationTime = DateTimeOffset.UtcNow
        };

        // 2. Fetch all IPS resources using compartment search
        var sectionResources = await FetchSectionResourcesAsync(context, cancellationToken);

        // 3. Generate narratives for each section
        await GenerateNarrativesAsync(context, sectionResources, cancellationToken);

        // 4. Build Composition
        var composition = BuildComposition(context, sectionResources);

        // 5. Assemble Bundle
        var bundle = AssembleBundle(context, composition, sectionResources);

        // 6. Post-process
        strategy.PostProcessBundle(bundle, context);

        sw.Stop();
        logger.LogInformation(
            "Generated IPS for Patient/{PatientId} with {ResourceCount} resources in {Duration}ms",
            patientId,
            bundle.Entry.Count,
            sw.ElapsedMilliseconds);

        return bundle;
    }

    /// <inheritdoc />
    /// <remarks>
    /// TODO: Implement identifier-based patient lookup using token search parameter.
    /// This will require building a proper SearchParameterExpression for the identifier parameter.
    /// </remarks>
    public Task<BundleJsonNode> GenerateIpsByIdentifierAsync(
        string? identifierSystem,
        string identifierValue,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "Identifier-based IPS generation requested but not yet implemented. System: {System}, Value: {Value}",
            identifierSystem,
            identifierValue);

        throw new NotSupportedException(
            "Identifier-based IPS generation is not yet supported. Please use patient ID directly via GET /Patient/{id}/$summary");
    }

    private IIpsGenerationStrategy SelectStrategy(string? profile)
    {
        if (profile is null)
        {
            return _defaultStrategy;
        }

        return _strategyByProfile.TryGetValue(profile, out var strategy)
            ? strategy
            : _defaultStrategy;
    }

    private async Task<Dictionary<Section, List<ResourceJsonNode>>> FetchSectionResourcesAsync(
        IpsContext context,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var sections = context.Strategy.GetSections();

        // Get all resource types needed for IPS sections
        var resourceTypes = sections
            .SelectMany(s => s.ResourceTypes)
            .Distinct()
            .ToHashSet();

        // Build patient everything expression to get compartment resources
        var expression = new PatientEverythingExpression(
            patientId: context.PatientId,
            filteredResourceTypes: resourceTypes);

        var searchOptions = new SearchOptions
        {
            ResourceType = null, // Multi-resource type search
            Expression = expression,
            MaxItemCount = DefaultMaxIpsResources,
            Total = TotalType.None
        };

        var requestContext = contextAccessor.RequestContext!;
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = requestContext.TenantId,
            TenantConfiguration = requestContext.TenantConfiguration
        };

        var partition = partitionStrategy.DetermineReadPartition(
            partitionContext,
            "Patient",
            new Dictionary<string, string>());

        var sectionResources = sections.ToDictionary(s => s, _ => new List<ResourceJsonNode>());
        var resourceTracker = new HashSet<string>(); // Deduplication

        // Stream results and classify into sections
        await foreach (var result in executionStrategy.SearchStreamAsync(partition, searchOptions, cancellationToken))
        {
            var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(result.ResourceBytes);
            var resourceId = $"{resource.ResourceType}/{resource.Id}";

            if (!resourceTracker.Add(resourceId))
            {
                continue; // Already processed (deduplication)
            }

            var section = context.Strategy.ClassifyResource(resource);
            if (section is not null && context.Strategy.ShouldIncludeResource(section, resource, context))
            {
                sectionResources[section].Add(resource);
            }
        }

        sw.Stop();
        logger.LogDebug("Fetched IPS resources in {Duration}ms", sw.ElapsedMilliseconds);

        return sectionResources;
    }

    private async Task GenerateNarrativesAsync(
        IpsContext context,
        Dictionary<Section, List<ResourceJsonNode>> sectionResources,
        CancellationToken cancellationToken)
    {
        foreach (var (section, resources) in sectionResources)
        {
            foreach (var resource in resources)
            {
                try
                {
                    var element = resource.ToElement(schema);
                    var narrative = await narrativeGenerator.GenerateNarrativeAsync(
                        element,
                        resource.ResourceType,
                        CultureInfo.CurrentCulture,
                        TemplateFormat.Html,
                        cancellationToken);

                    SetNarrative(resource, narrative);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to generate narrative for {ResourceType}/{ResourceId}",
                        resource.ResourceType,
                        resource.Id);
                }
            }
        }
    }

    private static void SetNarrative(ResourceJsonNode resource, string narrativeXhtml)
    {
        var textNode = new JsonObject
        {
            ["status"] = "generated",
            ["div"] = narrativeXhtml
        };

        resource.MutableNode["text"] = textNode;
    }

    private CompositionJsonNode BuildComposition(
        IpsContext context,
        Dictionary<Section, List<ResourceJsonNode>> sectionResources)
    {
        var compositionId = Guid.NewGuid().ToString();

        var composition = new CompositionJsonNode
        {
            Id = compositionId,
            Status = CompositionJsonNode.CompositionStatus.Final,
            Date = context.GenerationTime,
            Title = context.Strategy.CreateTitle(context),
            Subject = ReferenceJsonNode.FromResourceTypeAndId("Patient", context.PatientId),
            Type = CreateCompositionType()
        };

        composition.Meta.Profiles.Add(IpsConstants.CompositionProfile);

        var author = context.Strategy.CreateAuthor(context);
        composition.Author.Add(ReferenceJsonNode.FromResourceTypeAndId(author.ResourceType, author.Id));

        foreach (var section in context.Strategy.GetSections())
        {
            var resources = sectionResources[section];

            if (resources.Count == 0 && section.Cardinality != SectionCardinality.Required)
            {
                continue;
            }

            var sectionComponent = CreateSectionComponent(section, resources);
            composition.Section.Add(sectionComponent);
        }

        return composition;
    }

    private static CodeableConceptJsonNode CreateCompositionType()
    {
        var type = new CodeableConceptJsonNode();
        type.Coding.Add(new CodingJsonNode
        {
            System = IpsConstants.LoincSystem,
            Code = IpsConstants.CompositionTypeCode,
            Display = IpsConstants.CompositionTypeDisplay
        });
        return type;
    }

    private CompositionJsonNode.SectionComponent CreateSectionComponent(
        Section section,
        List<ResourceJsonNode> resources)
    {
        var sectionComponent = new CompositionJsonNode.SectionComponent
        {
            Title = section.Title,
            Code = CreateSectionCode(section)
        };

        if (resources.Count > 0)
        {
            foreach (var resource in resources)
            {
                sectionComponent.Entry.Add(ReferenceJsonNode.FromResourceTypeAndId(resource.ResourceType, resource.Id));
            }

            sectionComponent.Text = new NarrativeJsonNode
            {
                Status = NarrativeJsonNode.NarrativeStatus.Generated,
                Div = GenerateSectionNarrative(section, resources)
            };
        }
        else
        {
            sectionComponent.EmptyReason = CreateEmptyReason();
            sectionComponent.Text = new NarrativeJsonNode
            {
                Status = NarrativeJsonNode.NarrativeStatus.Generated,
                Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>No {section.Title.ToLower(CultureInfo.InvariantCulture)} information available.</p></div>"
            };
        }

        return sectionComponent;
    }

    private static CodeableConceptJsonNode CreateSectionCode(Section section)
    {
        var code = new CodeableConceptJsonNode();
        code.Coding.Add(new CodingJsonNode
        {
            System = section.CodeSystem,
            Code = section.Code,
            Display = section.Display
        });
        return code;
    }

    private static CodeableConceptJsonNode CreateEmptyReason()
    {
        var emptyReason = new CodeableConceptJsonNode();
        emptyReason.Coding.Add(new CodingJsonNode
        {
            System = IpsConstants.EmptyReasonSystem,
            Code = "unavailable",
            Display = "Unavailable"
        });
        return emptyReason;
    }

    private string GenerateSectionNarrative(Section section, List<ResourceJsonNode> resources)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"<div xmlns=\"http://www.w3.org/1999/xhtml\"><h3>{section.Title}</h3>");

        if (resources.Count == 0)
        {
            sb.Append($"<p>No {section.Title.ToLower(CultureInfo.InvariantCulture)} information available.</p>");
        }
        else
        {
            sb.Append("<ul>");
            foreach (var resource in resources)
            {
                var display = GetResourceDisplay(resource);
                sb.Append($"<li>{display}</li>");
            }
            sb.Append("</ul>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private string GetResourceDisplay(ResourceJsonNode resource)
    {
        var resourceType = resource.ResourceType;

        // Use FHIRPath to extract display from common CodeableConcept paths
        // Checks: code.text, code.coding[0].display, medicationCodeableConcept.text, etc.
        var fhirPathExpression = "(code | medicationCodeableConcept | vaccineCode).text | (code | medicationCodeableConcept | vaccineCode).coding.first().display";

        var element = resource.ToElement(schema);
        var results = element.Select(fhirPathExpression);

        var display = results.FirstOrDefault()?.Value?.ToString()
            ?? $"{resourceType}/{resource.Id}";

        return System.Net.WebUtility.HtmlEncode(display);
    }

    private BundleJsonNode AssembleBundle(
        IpsContext context,
        ResourceJsonNode composition,
        Dictionary<Section, List<ResourceJsonNode>> sectionResources)
    {
        var bundleId = Guid.NewGuid().ToString();

        var bundle = new BundleJsonNode
        {
            Id = bundleId,
            Type = BundleJsonNode.BundleType.Document,
        };

        bundle.MutableNode["identifier"] = new JsonObject
        {
            ["system"] = "urn:ietf:rfc:3986",
            ["value"] = $"urn:uuid:{bundleId}"
        };

        bundle.MutableNode["timestamp"] = context.GenerationTime.ToString("o");

        bundle.MutableNode["meta"] = new JsonObject
        {
            ["profile"] = new JsonArray { IpsConstants.DefaultBundleProfile }
        };

        // First entry: Composition
        bundle.Entry.Add(new BundleComponentJsonNode
        {
            FullUrl = $"urn:uuid:{composition.Id}",
            Resource = composition
        });

        // Second entry: Patient
        bundle.Entry.Add(new BundleComponentJsonNode
        {
            FullUrl = $"Patient/{context.PatientId}",
            Resource = context.Patient
        });

        // Add author (Organization/Device)
        var author = context.Strategy.CreateAuthor(context);
        bundle.Entry.Add(new BundleComponentJsonNode
        {
            FullUrl = $"urn:uuid:{author.Id}",
            Resource = author
        });

        // Add all section resources
        var addedResources = new HashSet<string>();
        foreach (var (_, resources) in sectionResources)
        {
            foreach (var resource in resources)
            {
                var resourceKey = $"{resource.ResourceType}/{resource.Id}";
                if (addedResources.Add(resourceKey))
                {
                    bundle.Entry.Add(new BundleComponentJsonNode
                    {
                        FullUrl = resourceKey,
                        Resource = resource
                    });
                }
            }
        }

        return bundle;
    }
}
