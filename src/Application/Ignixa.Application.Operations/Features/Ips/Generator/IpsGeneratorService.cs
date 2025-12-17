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
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Operations.Features.Ips.Api;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.NarrativeGenerator;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Features.Ips.Generator;

/// <summary>
/// Service for generating International Patient Summary (IPS) documents.
/// </summary>
public class IpsGeneratorService : IIpsGeneratorService
{
    private readonly FrozenDictionary<string, IIpsGenerationStrategy> _strategyByProfile;
    private readonly IIpsGenerationStrategy _defaultStrategy;
    private readonly IQueryExecutionStrategy _executionStrategy;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly INarrativeGenerator _narrativeGenerator;
    private readonly ISchema _schema;
    private readonly ILogger<IpsGeneratorService> _logger;

    public IpsGeneratorService(
        IEnumerable<IIpsGenerationStrategy> strategies,
        IQueryExecutionStrategy executionStrategy,
        IFhirRepositoryFactory repositoryFactory,
        IPartitionStrategy partitionStrategy,
        IFhirRequestContextAccessor contextAccessor,
        INarrativeGenerator narrativeGenerator,
        ISchema schema,
        ILogger<IpsGeneratorService> logger)
    {
        var strategyList = strategies.ToList();
        _strategyByProfile = strategyList.ToFrozenDictionary(s => s.BundleProfile, s => s);
        _defaultStrategy = strategyList.FirstOrDefault(s => s.BundleProfile == IpsConstants.DefaultBundleProfile)
            ?? strategyList.First();
        _executionStrategy = executionStrategy;
        _repositoryFactory = repositoryFactory;
        _partitionStrategy = partitionStrategy;
        _contextAccessor = contextAccessor;
        _narrativeGenerator = narrativeGenerator;
        _schema = schema;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BundleJsonNode> GenerateIpsAsync(
        string patientId,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var strategy = SelectStrategy(profile);

        var requestContext = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        var partitionId = requestContext.TenantId;
        var repository = await _repositoryFactory.GetRepositoryAsync(partitionId, cancellationToken);

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
        _logger.LogInformation(
            "Generated IPS for Patient/{PatientId} with {ResourceCount} resources in {Duration}ms",
            patientId,
            bundle.Entry.Count,
            sw.ElapsedMilliseconds);

        return bundle;
    }

    /// <inheritdoc />
    public Task<BundleJsonNode> GenerateIpsByIdentifierAsync(
        string? identifierSystem,
        string identifierValue,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        // Identifier-based IPS generation not yet implemented
        // This requires a search query with identifier token search parameter
        throw new NotSupportedException(
            "Identifier-based IPS generation is not yet supported. Please use patient ID directly.");
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
            MaxItemCount = 1000, // Reasonable limit for IPS
            Total = TotalType.None
        };

        var requestContext = _contextAccessor.RequestContext!;
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = requestContext.TenantId,
            TenantConfiguration = requestContext.TenantConfiguration
        };

        var partition = _partitionStrategy.DetermineReadPartition(
            partitionContext,
            "Patient",
            new Dictionary<string, string>());

        var sectionResources = sections.ToDictionary(s => s, _ => new List<ResourceJsonNode>());
        var resourceTracker = new HashSet<string>(); // Deduplication

        // Stream results and classify into sections
        await foreach (var result in _executionStrategy.SearchStreamAsync(partition, searchOptions, cancellationToken))
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
        _logger.LogDebug("Fetched IPS resources in {Duration}ms", sw.ElapsedMilliseconds);

        return sectionResources;
    }

    private async Task GenerateNarrativesAsync(
        IpsContext context,
        Dictionary<Section, List<ResourceJsonNode>> sectionResources,
        CancellationToken cancellationToken)
    {
        // Generate narratives for each section's resources
        foreach (var (section, resources) in sectionResources)
        {
            foreach (var resource in resources)
            {
                try
                {
                    var element = resource.ToElement(_schema);
                    var narrative = await _narrativeGenerator.GenerateNarrativeAsync(
                        element,
                        resource.ResourceType,
                        CultureInfo.CurrentCulture,
                        TemplateFormat.Html,
                        cancellationToken);

                    // Set the narrative on the resource
                    SetNarrative(resource, narrative);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
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

    private ResourceJsonNode BuildComposition(
        IpsContext context,
        Dictionary<Section, List<ResourceJsonNode>> sectionResources)
    {
        var compositionId = Guid.NewGuid().ToString();

        var composition = new JsonObject
        {
            ["resourceType"] = "Composition",
            ["id"] = compositionId,
            ["meta"] = new JsonObject
            {
                ["profile"] = new JsonArray { IpsConstants.CompositionProfile }
            },
            ["status"] = "final",
            ["type"] = new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = IpsConstants.LoincSystem,
                        ["code"] = IpsConstants.CompositionTypeCode,
                        ["display"] = IpsConstants.CompositionTypeDisplay
                    }
                }
            },
            ["subject"] = new JsonObject
            {
                ["reference"] = $"Patient/{context.PatientId}"
            },
            ["date"] = context.GenerationTime.ToString("o"),
            ["title"] = context.Strategy.CreateTitle(context)
        };

        // Add author
        var author = context.Strategy.CreateAuthor(context);
        composition["author"] = new JsonArray
        {
            new JsonObject
            {
                ["reference"] = $"{author.ResourceType}/{author.Id}"
            }
        };

        // Build sections
        var sectionsArray = new JsonArray();

        foreach (var section in context.Strategy.GetSections())
        {
            var resources = sectionResources[section];

            // Skip empty optional/recommended sections
            if (resources.Count == 0 && section.Cardinality != SectionCardinality.Required)
            {
                continue;
            }

            var sectionNode = new JsonObject
            {
                ["title"] = section.Title,
                ["code"] = new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = section.CodeSystem,
                            ["code"] = section.Code,
                            ["display"] = section.Display
                        }
                    }
                }
            };

            if (resources.Count > 0)
            {
                var entryArray = new JsonArray();
                foreach (var resource in resources)
                {
                    entryArray.Add(new JsonObject
                    {
                        ["reference"] = $"{resource.ResourceType}/{resource.Id}"
                    });
                }
                sectionNode["entry"] = entryArray;

                // Generate section narrative
                sectionNode["text"] = new JsonObject
                {
                    ["status"] = "generated",
                    ["div"] = GenerateSectionNarrative(section, resources)
                };
            }
            else
            {
                // Empty required section - add emptyReason
                sectionNode["emptyReason"] = new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = IpsConstants.EmptyReasonSystem,
                            ["code"] = "unavailable",
                            ["display"] = "Unavailable"
                        }
                    }
                };

                sectionNode["text"] = new JsonObject
                {
                    ["status"] = "generated",
                    ["div"] = $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>No {section.Title.ToLower(CultureInfo.InvariantCulture)} information available.</p></div>"
                };
            }

            sectionsArray.Add(sectionNode);
        }

        composition["section"] = sectionsArray;

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(composition.ToJsonString()!);
    }

    private static string GenerateSectionNarrative(Section section, List<ResourceJsonNode> resources)
    {
        // Simple narrative generation for the section
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

    private static string GetResourceDisplay(ResourceJsonNode resource)
    {
        var resourceType = resource.ResourceType;

        // Try common display fields
        var display = resource.MutableNode["code"]?["text"]?.GetValue<string>()
            ?? resource.MutableNode["code"]?["coding"]?[0]?["display"]?.GetValue<string>()
            ?? resource.MutableNode["medicationCodeableConcept"]?["text"]?.GetValue<string>()
            ?? resource.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["display"]?.GetValue<string>()
            ?? resource.MutableNode["vaccineCode"]?["text"]?.GetValue<string>()
            ?? resource.MutableNode["vaccineCode"]?["coding"]?[0]?["display"]?.GetValue<string>()
            ?? $"{resourceType}/{resource.Id}";

        return System.Web.HttpUtility.HtmlEncode(display);
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

        // Set identifier
        bundle.MutableNode["identifier"] = new JsonObject
        {
            ["system"] = "urn:ietf:rfc:3986",
            ["value"] = $"urn:uuid:{bundleId}"
        };

        // Set timestamp
        bundle.MutableNode["timestamp"] = context.GenerationTime.ToString("o");

        // Set meta with profile
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
