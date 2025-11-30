// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Medino;
using ModelContextProtocol.Server;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Mcp.Tools;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Ignixa.Application.Features.Mcp.Tools.FhirOperations;

/// <summary>
/// MCP tool for querying the FHIR CapabilityStatement using FHIRPath expressions.
/// Allows arbitrary queries to discover supported resources, interactions, operations, and more.
/// </summary>
[McpServerToolType]
public class FhirPathQueryCapabilityStatementTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;
    private readonly FhirPathEvaluator _evaluator;
    private readonly FhirPathParser _parser;
    private readonly IFhirVersionContext _versionContext;
    private readonly ITenantConfigurationStore _tenantConfigStore;

    public FhirPathQueryCapabilityStatementTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator,
        FhirPathEvaluator evaluator,
        FhirPathParser parser,
        IFhirVersionContext versionContext)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _tenantConfigStore = tenantStore ?? throw new ArgumentNullException(nameof(tenantStore));
    }

    [McpServerTool(Name = "fhirpath_query_capability_statement")]
    [Description("Query the FHIR Server Capabilities, using FHIRPath expressions. " +
        "Allows flexible queries like 'rest.resource.type' (all resource types), " +
        "'rest.resource.where(type=\"Patient\").interaction.code' (Patient operations), " +
        "or 'rest.operation.name' (operations). " +
        "FHIRPath supports property access, filtering (where), string functions, and comparison operators. " +
        "Examples: 'rest.resource.type', 'rest.resource.where(type=\"Patient\").interaction.code', " +
        "'rest.resource.where(type=\"Observation\").searchParam.name', 'rest.operation.name', 'software.name'")]
    public async Task<FhirPathQueryResultDto> FhirPathQueryCapabilityStatementAsync(
        [Description("FHIRPath expression to evaluate against the CapabilityStatement")]
        string fhirPathExpression,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fhirPathExpression))
        {
            throw new ArgumentException("FHIRPath expression cannot be empty", nameof(fhirPathExpression));
        }

        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Get CapabilityStatement
        var query = new GetCapabilityStatementQuery(resolvedTenantId);
        var capabilityStatement = await _mediator.SendAsync(query, cancellationToken);

        // Execute FHIRPath query
        var results = new List<object>();
        var errors = new List<string>();

        try
        {
            // Compile FHIRPath expression
            var expression = _parser.Parse(fhirPathExpression);

            // Determine FHIR version from tenant configuration
            var fhirVersion = await ResolveFhirVersionAsync(resolvedTenantId, cancellationToken);

            // Get structure provider from version context
            var structureProvider = _versionContext.GetBaseSchemaProvider(fhirVersion);

            // Convert CapabilityStatement to IElement for evaluation
            var typedElement = capabilityStatement.ToElement(structureProvider);

            // Evaluate FHIRPath expression against the CapabilityStatement
            var evalResults = _evaluator.Evaluate((IElement)typedElement, expression);

            // Convert results to serializable objects
            foreach (var resultElement in evalResults)
            {
                results.Add(ConvertElementToSerializable((IElement)resultElement));
            }
        }
        catch (Exception ex)
        {
            errors.Add($"FHIRPath evaluation error: {ex.Message}");
        }

        return new FhirPathQueryResultDto
        {
            Expression = fhirPathExpression,
            ResultCount = results.Count,
            Results = results,
            Errors = errors
        };
    }

    /// <summary>
    /// Resolve the FHIR version from tenant configuration, with fallback to R4 default.
    /// </summary>
    private async Task<FhirSpecification> ResolveFhirVersionAsync(int tenantId, CancellationToken cancellationToken)
    {
        // Default to R4
        var fhirVersion = FhirSpecification.R4;

        try
        {
            var tenantConfig = await _tenantConfigStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
            if (tenantConfig != null && !string.IsNullOrEmpty(tenantConfig.FhirVersion))
            {
                fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
            }
        }
        catch
        {
            // If tenant resolution fails, use default R4
        }

        return fhirVersion;
    }

    /// <summary>
    /// Convert an IElement to a serializable object (for JSON response).
    /// </summary>
    private static object ConvertElementToSerializable(IElement element)
    {
        // Try to extract the JsonNode if available via Meta<T>
        var jsonNode = element.Meta<JsonNode>();
        if (jsonNode != null)
        {
            return JsonSerializer.Deserialize<object>(jsonNode.ToJsonString()) ?? "null";
        }

        // Fallback: extract value from the typed element
        // For primitive types, use the Value property
        if (element.Value != null)
        {
            return element.Value;
        }

        // For complex types, return a representation
        return new { type = element.InstanceType, value = element.ToString() };
    }
}
