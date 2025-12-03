// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Features.Validate;

/// <summary>
/// Handler for FHIR $validate operation.
/// Validates a resource against FHIR specification and optionally a specific profile.
/// Returns an OperationOutcome resource with validation results.
/// </summary>
public class ValidateResourceHandler : IRequestHandler<ValidateResourceCommand, ValidateResourceResult>
{
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly Func<FhirVersion, int, IValidationSchemaResolver> _schemaResolverFactory;
    private readonly ITerminologyService _terminologyService;
    private readonly ILogger<ValidateResourceHandler> _logger;

    public ValidateResourceHandler(
        IFhirRequestContextAccessor contextAccessor,
        Func<FhirVersion, int, IValidationSchemaResolver> schemaResolverFactory,
        ITerminologyService terminologyService,
        ILogger<ValidateResourceHandler> logger)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _schemaResolverFactory = schemaResolverFactory ?? throw new ArgumentNullException(nameof(schemaResolverFactory));
        _terminologyService = terminologyService ?? throw new ArgumentNullException(nameof(terminologyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<ValidateResourceResult> HandleAsync(
        ValidateResourceCommand request,
        CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        // Get tenant configuration from context to determine validation depth
        var currentTenantConfig = context.TenantConfiguration;

        // Use FHIR version from context (defaults to R4)
        var fhirVersionEnum = context.FhirVersion;

        // Determine validation depth: Default to Spec depth for $validate operation
        var validationDepth = ParseValidationDepth(currentTenantConfig?.ValidationDepth ?? "Spec");

        _logger.LogDebug(
            "Validating resource with $validate operation (FHIR {Version}, Depth: {Depth})",
            fhirVersionEnum,
            validationDepth);

        var sourceNode = request.JsonNode.ToSourceNavigator();
        var issues = new List<object>();

        // If no resource type is specified in the request, extract from the resource itself
        var resourceType = request.ResourceType;
        if (string.IsNullOrEmpty(resourceType))
        {
            // Try to extract from JsonNode directly
            var resourceTypeValue = request.JsonNode.ResourceType;
            if (!string.IsNullOrEmpty(resourceTypeValue))
            {
                resourceType = resourceTypeValue;
            }
        }

        if (string.IsNullOrEmpty(resourceType))
        {
            // No resource type found - return error
            issues.Add(new
            {
                severity = "error",
                code = "required",
                diagnostics = "Resource must contain a 'resourceType' field"
            });
        }
        else if (sourceNode is not null)
        {
            // Handle mode-specific validation logic (FHIR spec requirement)
            var normalizedMode = request.Mode?.ToUpperInvariant();

            // Delete mode: Skip resource content validation, only check referential integrity
            if (normalizedMode == "DELETE")
            {
                _logger.LogDebug(
                    "Delete mode validation for {ResourceType}/{InstanceId} - checking referential integrity only",
                    resourceType,
                    request.InstanceId);

                // TODO: Implement referential integrity check
                // Pseudo code for DELETE mode validation:
                //
                // 1. Use _revinclude=*:* to find all resources that reference this resource:
                //    var searchOptions = new SearchOptions
                //    {
                //        TenantId = request.TenantId,
                //        ResourceType = resourceType,
                //        ResourceId = request.InstanceId, // Single resource search
                //        RevIncludes = new[] { "*:*" }    // Find ALL resources that reference this one
                //    };
                //
                //    var referencingResources = new List<string>();
                //    await foreach (var entry in _searchService.SearchStreamAsync(searchOptions, cancellationToken))
                //    {
                //        if (entry.Mode == SearchEntryMode.Include) // RevInclude results are marked as Include mode
                //        {
                //            referencingResources.Add($"{entry.ResourceType}/{entry.ResourceId}");
                //        }
                //    }
                //
                // 2. If any references exist, add error issue:
                //    if (referencingResources.Any())
                //    {
                //        issues.Add(new {
                //            severity = "error",
                //            code = "conflict",
                //            diagnostics = $"Cannot delete {resourceType}/{request.InstanceId}: {referencingResources.Count} resource(s) reference this resource: {string.Join(", ", referencingResources.Take(5))}{(referencingResources.Count > 5 ? "..." : "")}",
                //            expression = new[] { resourceType }
                //        });
                //    }
                //
                // 3. Check for cascade delete policy in tenant configuration:
                //    if (currentTenantConfig?.AllowCascadeDelete == true && referencingResources.Any())
                //    {
                //        issues.Add(new {
                //            severity = "warning",
                //            code = "informational",
                //            diagnostics = $"Cascade delete enabled: {referencingResources.Count} referencing resource(s) will also be deleted"
                //        });
                //    }
                //
                // For now, return success as we don't have cross-resource reference tracking yet
                issues.Add(new
                {
                    severity = "information",
                    code = "informational",
                    diagnostics = "Delete mode validation: referential integrity checks not yet implemented"
                });

                // Skip normal schema validation for delete mode
                var deleteOutcome = new
                {
                    resourceType = "OperationOutcome",
                    issue = issues.ToArray()
                };
                var deleteJson = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(deleteOutcome));
                return Task.FromResult(new ValidateResourceResult(deleteJson ?? throw new InvalidOperationException("Failed to serialize OperationOutcome")));
            }

            // Determine which schema to use
            string canonicalUrl;
            if (!string.IsNullOrEmpty(request.Profile))
            {
                // Validate against specific profile if provided
                canonicalUrl = request.Profile;
            }
            else
            {
                // Validate against base resource definition
                canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
            }

            // Get tenantId from tenant configuration (defaults to 1 if not found)
            var tenantId = currentTenantConfig?.TenantId ?? 1;

            var schemaResolver = _schemaResolverFactory(fhirVersionEnum, tenantId);
            var schema = schemaResolver.GetSchema(canonicalUrl);

            if (schema is null)
            {
                issues.Add(new
                {
                    severity = "error",
                    code = "not-found",
                    diagnostics = $"Schema not found for {canonicalUrl}"
                });
            }
            else
                {
                    var settings = new ValidationSettings
                    {
                        Depth = validationDepth,
                        TerminologyService = _terminologyService
                    };
                    var state = new ValidationState();
                    var validationResult = schema.Validate((IElement)sourceNode, settings, state);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning(
                        "Validation issues found for {ResourceType} (mode: {Mode}): {ErrorCount} error(s), {WarningCount} warning(s)",
                        resourceType,
                        normalizedMode ?? "default",
                        validationResult.Issues.Count(i => i.Severity == IssueSeverity.Error),
                        validationResult.Issues.Count(i => i.Severity == IssueSeverity.Warning));

                    // Convert validation issues to OperationOutcome issues
                    foreach (var issue in validationResult.Issues)
                    {
                        issues.Add(new
                        {
                            severity = MapSeverity(issue.Severity),
                            code = NormalizeCode(issue.Code),
                            diagnostics = issue.Message,
                            expression = string.IsNullOrEmpty(issue.Path) ? null : issue.Path  // Note: 'location' is deprecated, only use 'expression'
                        });
                    }
                }

                // Mode-specific post-validation checks
                if (normalizedMode == "CREATE")
                {
                    // TODO: Add uniqueness constraint checks for create mode
                    // Pseudo code for CREATE mode validation:
                    //
                    // 1. Extract business identifiers from the resource:
                    //    var identifiers = ExtractIdentifiers(jsonNode);
                    //    // e.g., Patient.identifier, Organization.identifier, etc.
                    //
                    // 2. For each identifier with uniqueness constraints:
                    //    foreach (var identifier in identifiers.Where(i => HasUniquenessConstraint(i.System)))
                    //    {
                    //        // Query for existing resources with same identifier
                    //        var existingResources = await _searchService.SearchAsync(
                    //            tenantId: request.TenantId,
                    //            resourceType: resourceType,
                    //            searchParams: new[] { $"identifier={identifier.System}|{identifier.Value}" },
                    //            cancellationToken);
                    //
                    //        if (existingResources.Any())
                    //        {
                    //            issues.Add(new {
                    //                severity = "error",
                    //                code = "duplicate",
                    //                diagnostics = $"Resource with identifier {identifier.System}|{identifier.Value} already exists: {existingResources.First().Id}",
                    //                expression = "identifier"
                    //            });
                    //        }
                    //    }
                    //
                    // 3. Check resource-specific uniqueness rules from StructureDefinition:
                    //    // e.g., Practitioner.identifier where use='official' must be unique
                    //    // e.g., Organization.name + Organization.partOf must be unique
                    //    var uniquenessRules = schema.GetUniquenessConstraints();
                    //    foreach (var rule in uniquenessRules)
                    //    {
                    //        var conflictingResources = await CheckUniquenessRule(rule, jsonNode, cancellationToken);
                    //        if (conflictingResources.Any())
                    //        {
                    //            issues.Add(new {
                    //                severity = "error",
                    //                code = "duplicate",
                    //                diagnostics = rule.GetViolationMessage(conflictingResources),
                    //                expression = rule.Path
                    //            });
                    //        }
                    //    }
                    //
                    _logger.LogDebug("Create mode validation - uniqueness checks not yet implemented");
                }
                else if (normalizedMode == "UPDATE")
                {
                    // TODO: Add immutable field checks and version validation for update mode
                    // Pseudo code for UPDATE mode validation:
                    //
                    // 1. Fetch the existing resource from storage:
                    //    var existingResource = await _resourceRepository.GetAsync(
                    //        tenantId: request.TenantId,
                    //        resourceType: resourceType,
                    //        resourceId: request.InstanceId,
                    //        cancellationToken);
                    //
                    //    if (existingResource is null)
                    //    {
                    //        issues.Add(new {
                    //            severity = "error",
                    //            code = "not-found",
                    //            diagnostics = $"Cannot validate update: {resourceType}/{request.InstanceId} does not exist. Use create mode instead."
                    //        });
                    //        return; // Cannot proceed with update validation
                    //    }
                    //
                    // 2. Check version matching (optimistic concurrency):
                    //    var submittedVersion = jsonNode.MutableNode?["meta"]?["versionId"]?.GetValue<string>();
                    //    if (!string.IsNullOrEmpty(submittedVersion) && submittedVersion != existingResource.Meta.VersionId)
                    //    {
                    //        issues.Add(new {
                    //            severity = "error",
                    //            code = "conflict",
                    //            diagnostics = $"Version conflict: submitted version {submittedVersion} does not match current version {existingResource.Meta.VersionId}",
                    //            expression = "meta.versionId"
                    //        });
                    //    }
                    //
                    // 3. Validate immutable fields (from StructureDefinition extensions):
                    //    var immutableFields = schema.GetImmutableFields(); // Fields marked with isModifier=true or custom immutability extensions
                    //    foreach (var fieldPath in immutableFields)
                    //    {
                    //        var existingValue = FhirPathEvaluator.Evaluate(existingResource, fieldPath);
                    //        var newValue = FhirPathEvaluator.Evaluate(jsonNode, fieldPath);
                    //
                    //        if (!ValuesAreEqual(existingValue, newValue))
                    //        {
                    //            issues.Add(new {
                    //                severity = "error",
                    //                code = "business-rule",
                    //                diagnostics = $"Cannot modify immutable field: {fieldPath}",
                    //                expression = fieldPath
                    //            });
                    //        }
                    //    }
                    //
                    // 4. Validate ID consistency:
                    //    var submittedId = jsonNode.MutableNode?["id"]?.GetValue<string>();
                    //    if (!string.IsNullOrEmpty(submittedId) && submittedId != request.InstanceId)
                    //    {
                    //        issues.Add(new {
                    //            severity = "error",
                    //            code = "invalid",
                    //            diagnostics = $"Resource ID '{submittedId}' does not match URL parameter '{request.InstanceId}'",
                    //            expression = "id"
                    //        });
                    //    }
                    //
                    // 5. Check for breaking reference changes (using _revinclude=*:*):
                    //    // If this resource is referenced by others, validate that key fields haven't changed
                    //    // (e.g., Patient.identifier changes could break references from other resources)
                    //    var searchOptions = new SearchOptions
                    //    {
                    //        TenantId = request.TenantId,
                    //        ResourceType = resourceType,
                    //        ResourceId = request.InstanceId, // Single resource search
                    //        RevIncludes = new[] { "*:*" }    // Find ALL resources that reference this one
                    //    };
                    //
                    //    var hasInboundReferences = false;
                    //    await foreach (var entry in _searchService.SearchStreamAsync(searchOptions, cancellationToken))
                    //    {
                    //        if (entry.Mode == SearchEntryMode.Include) // RevInclude results are marked as Include mode
                    //        {
                    //            hasInboundReferences = true;
                    //            break; // We only need to know if ANY references exist
                    //        }
                    //    }
                    //
                    //    if (hasInboundReferences)
                    //    {
                    //        var criticalFieldsChanged = CheckCriticalFieldChanges(existingResource, jsonNode);
                    //        // Critical fields: identifier, status (for definitional resources), etc.
                    //        if (criticalFieldsChanged.Any())
                    //        {
                    //            issues.Add(new {
                    //                severity = "warning",
                    //                code = "business-rule",
                    //                diagnostics = $"This resource is referenced by other resources. Changing {string.Join(", ", criticalFieldsChanged)} may affect referential integrity.",
                    //                expression = criticalFieldsChanged
                    //            });
                    //        }
                    //    }
                    //
                    _logger.LogDebug("Update mode validation for {InstanceId} - immutability checks not yet implemented", request.InstanceId);
                }
            }
        }

        // Build OperationOutcome response as JsonNode
        // FHIR spec requirement: OperationOutcome.issue has cardinality 1..* (at least one issue required)
        // See: https://hl7.org/fhir/R4/operationoutcome.html
        if (issues.Count == 0)
        {
            // Successful validation with no issues - add informational issue per spec "All OK" example
            var successMessage = !string.IsNullOrEmpty(request.Profile)
                ? $"Validation successful: Resource conforms to {request.Profile}"
                : "Validation successful: No issues found";

            if (!string.IsNullOrEmpty(request.Mode))
            {
                successMessage += $" (mode: {request.Mode})";
            }

            issues.Add(new
            {
                severity = "information",
                code = "informational",
                diagnostics = successMessage
            });
        }

        var operationOutcome = new
        {
            resourceType = "OperationOutcome",
            issue = issues.ToArray()
        };

        var operationOutcomeJson = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(operationOutcome));
        if (operationOutcomeJson is null)
        {
            throw new InvalidOperationException("Failed to serialize OperationOutcome");
        }

        return Task.FromResult(new ValidateResourceResult(operationOutcomeJson));
    }

    private static ValidationDepth ParseValidationDepth(string? depth)
    {
        return depth?.ToUpperInvariant() switch
        {
            "MINIMAL" => ValidationDepth.Minimal,
            "SPEC" => ValidationDepth.Spec,
            "FULL" => ValidationDepth.Full,
            // Backward compatibility
            "NONE" => ValidationDepth.Minimal,
            "FAST" => ValidationDepth.Minimal,
            "PROFILE" => ValidationDepth.Full,
            _ => ValidationDepth.Spec // Default
        };
    }

    private static string MapSeverity(IssueSeverity severity) =>
        severity switch
        {
            IssueSeverity.Fatal => "fatal",
            IssueSeverity.Error => "error",
            IssueSeverity.Warning => "warning",
            IssueSeverity.Information => "information",
            _ => "information"
        };

    /// <summary>
    /// Normalizes validation issue codes to FHIR OperationOutcome issue codes.
    /// Maps constraint keys (bdl-7, ele-1) and custom codes to standard FHIR codes.
    /// </summary>
    private static string NormalizeCode(string code)
    {
        // Map common constraint keys and validation codes to FHIR issue codes
        var upperCode = code?.ToUpperInvariant();
        return upperCode switch
        {
            // FHIR standard codes
            "INVALID" or "VALUE" => "invalid",
            "STRUCTURE" => "structure",
            "REQUIRED" => "required",
            "INVARIANT" => "invariant",
            "NOT-FOUND" => "not-found",
            "CONFLICT" => "conflict",
            "PROCESSING" => "processing",
            "EXCEPTION" => "exception",

            // Map constraint keys to appropriate codes
            _ when code?.StartsWith("bdl-", StringComparison.OrdinalIgnoreCase) == true => "structure",  // Bundle constraints
            _ when code?.StartsWith("ele-", StringComparison.OrdinalIgnoreCase) == true => "structure",  // Element constraints
            _ when code?.StartsWith("ext-", StringComparison.OrdinalIgnoreCase) == true => "structure",  // Extension constraints

            // Default to processing for unknown codes
            _ => "processing"
        };
    }

    /// <summary>
    /// Validates terminology bindings for common coded elements based on ValidationDepth.
    /// NOTE: This method is currently unused as terminology validation is integrated into the main schema validation.
    /// Kept for reference in case separate terminology validation is needed in the future.
    /// </summary>
    private async Task<List<object>> ValidateTerminologyBindingsAsync(
        string resourceType,
        ResourceJsonNode resource,
        ValidationDepth validationDepth,
        CancellationToken cancellationToken)
    {
        var issues = new List<object>();

        // Skip terminology validation for Minimal mode
        if (validationDepth == ValidationDepth.Minimal)
        {
            return issues;
        }

        // Define bindings for common coded elements (hard-coded for MVP)
        var bindings = GetKnownBindings(resourceType);

        foreach (var (elementPath, valueSetUrl, strength) in bindings)
        {
            // Skip extensible bindings in Spec mode (only validate in Full mode)
            if (validationDepth == ValidationDepth.Spec && strength == Ignixa.Validation.Abstractions.BindingStrength.Extensible)
            {
                continue;
            }

            // Extract coded value from resource
            var codedValue = ExtractCodedValue(resource, elementPath);
            if (codedValue == null)
            {
                continue; // Element not present in resource
            }

            // Validate binding
            var result = await _terminologyService.ValidateBindingAsync(
                valueSetUrl,
                strength,
                codedValue.Value.System,
                codedValue.Value.Code,
                codedValue.Value.Display,
                version: null,
                cancellationToken);

            // Add issue if validation failed or has warnings
            if (!result.IsValid || result.Severity != IssueSeverity.Information)
            {
                issues.Add(new
                {
                    severity = MapSeverity(result.Severity),
                    code = result.Severity == IssueSeverity.Error ? "code-invalid" : "business-rule",
                    diagnostics = result.Message,
                    expression = elementPath
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// Returns known bindings for common coded elements by resource type.
    /// </summary>
    private static List<(string ElementPath, string ValueSetUrl, Ignixa.Validation.Abstractions.BindingStrength Strength)> GetKnownBindings(string resourceType)
    {
        return resourceType switch
        {
            "Patient" => new List<(string, string, Ignixa.Validation.Abstractions.BindingStrength)>
            {
                ("Patient.gender", "http://hl7.org/fhir/ValueSet/administrative-gender", Ignixa.Validation.Abstractions.BindingStrength.Required),
                ("Patient.maritalStatus.coding", "http://hl7.org/fhir/ValueSet/marital-status", Ignixa.Validation.Abstractions.BindingStrength.Extensible),
            },
            "Observation" => new List<(string, string, Ignixa.Validation.Abstractions.BindingStrength)>
            {
                ("Observation.status", "http://hl7.org/fhir/ValueSet/observation-status", Ignixa.Validation.Abstractions.BindingStrength.Required),
            },
            "Condition" => new List<(string, string, Ignixa.Validation.Abstractions.BindingStrength)>
            {
                ("Condition.clinicalStatus.coding", "http://hl7.org/fhir/ValueSet/condition-clinical", Ignixa.Validation.Abstractions.BindingStrength.Required),
                ("Condition.verificationStatus.coding", "http://hl7.org/fhir/ValueSet/condition-ver-status", Ignixa.Validation.Abstractions.BindingStrength.Required),
            },
            _ => new List<(string, string, Ignixa.Validation.Abstractions.BindingStrength)>()
        };
    }

    /// <summary>
    /// Extracts coded value from resource at given element path.
    /// </summary>
    private static (string? System, string? Code, string? Display)? ExtractCodedValue(
        ResourceJsonNode resource,
        string elementPath)
    {
        try
        {
            var parts = elementPath.Split('.');
            JsonNode? current = resource.MutableNode;

            // Navigate to element (e.g., "Patient.gender" or "Patient.maritalStatus.coding")
            for (int i = 1; i < parts.Length; i++) // Skip resource type (parts[0])
            {
                var part = parts[i];
                current = current?[part];
                if (current == null) return null;
            }

            // Handle different coded element types
            if (elementPath.EndsWith(".coding", StringComparison.Ordinal))
            {
                // CodeableConcept.coding (array)
                var codingArray = current?.AsArray();
                if (codingArray == null || codingArray.Count == 0) return null;

                var firstCoding = codingArray[0];
                return (
                    System: firstCoding?["system"]?.GetValue<string>(),
                    Code: firstCoding?["code"]?.GetValue<string>(),
                    Display: firstCoding?["display"]?.GetValue<string>()
                );
            }
            else
            {
                // Simple code element (e.g., Patient.gender)
                var code = current?.GetValue<string>();
                if (code == null) return null;

                // For simple code elements, system will be inferred by terminology service
                return (
                    System: null,
                    Code: code,
                    Display: null
                );
            }
        }
        catch
        {
            return null; // Ignore extraction errors
        }
    }
}
