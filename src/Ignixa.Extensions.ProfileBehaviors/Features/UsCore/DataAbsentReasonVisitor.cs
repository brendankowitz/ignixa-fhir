// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Extensions.ProfileBehaviors.Abstractions;

namespace Ignixa.Extensions.ProfileBehaviors.Features.UsCore;

/// <summary>
/// Visitor implementation that injects data-absent-reason extensions for missing mandatory elements.
/// Implements US Core requirement: "For mandatory elements with missing data and unknown reason,
/// include the element with a data-absent-reason extension using code 'unknown'."
/// </summary>
/// <remarks>
/// <para>
/// <strong>US Core Guidance</strong>:
/// - Optional elements (min=0): Omit entirely if missing
/// - Mandatory elements (min>0): Add element with data-absent-reason extension
/// - Required bindings without unknown code (e.g., status): Cannot use data-absent-reason
/// </para>
/// <para>
/// <strong>Implementation Strategy</strong>:
/// - Only injects at root level (depth = 0) to avoid complexity
/// - Driven by metadata (BindingStrength from IExtendedElementMetadata)
/// - Conservative for coded elements without binding metadata
/// - Uses "unknown" code by default
/// </para>
/// </remarks>
public sealed class DataAbsentReasonVisitor : IResourcePropertyVisitor
{
    private const string DataAbsentReasonUrl = "http://hl7.org/fhir/StructureDefinition/data-absent-reason";
    private const string DefaultCode = "unknown";

    // System/mandatory fields that should never have data-absent-reason
    private static readonly HashSet<string> SystemFields = new(StringComparer.Ordinal)
    {
        "resourceType",
        "id",
        "meta"
    };

    // FHIR types that indicate coded elements (require binding metadata)
    private static readonly HashSet<string> CodedTypes = new(StringComparer.Ordinal)
    {
        "code",
        "Coding",
        "CodeableConcept"
    };

    /// <summary>
    /// Visit existing property - pass through unchanged for US Core.
    /// </summary>
    public PropertyVisitResult VisitProperty(
        string propertyName,
        ElementMetadata? metadata,
        int depth,
        VisitorContext context)
    {
        // US Core visitor doesn't modify existing properties
        return PropertyVisitResult.Include();
    }

    /// <summary>
    /// Visit missing mandatory property - inject data-absent-reason if appropriate.
    /// </summary>
    public PropertyVisitResult VisitMissingProperty(
        string propertyName,
        ElementMetadata metadata,
        int depth,
        VisitorContext context)
    {
        // Only inject at root level
        if (depth > 0)
        {
            return PropertyVisitResult.Skip();
        }

        // Check if element is eligible for data-absent-reason injection
        if (!CanHaveDataAbsentReason(propertyName, metadata))
        {
            return PropertyVisitResult.Skip();
        }

        // Inject data-absent-reason
        return PropertyVisitResult.Inject(() => CreateDataAbsentReasonElement(metadata));
    }

    /// <summary>
    /// Determines if an element can have a data-absent-reason extension.
    /// Uses metadata-driven approach with binding strength from IExtendedElementMetadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Decision Logic</strong>:
    /// 1. Element must be mandatory (IsRequired = true)
    /// 2. Element must not be a system field (resourceType, id, meta)
    /// 3. For coded elements (code, Coding, CodeableConcept):
    ///    - WITH binding metadata: Check binding strength
    ///      - Required → Cannot have data-absent-reason
    ///      - Extensible/Preferred/Example → Can have data-absent-reason
    ///    - WITHOUT binding metadata: Conservative - skip (cannot safely determine)
    /// 4. For non-coded elements: Eligible (no binding concerns)
    /// </para>
    /// <para>
    /// <strong>No Hardcoded Patterns</strong>:
    /// This implementation is fully metadata-driven. It does NOT use hardcoded element
    /// name patterns like "status" or "intent". All decisions are based on schema metadata
    /// from FHIR StructureDefinitions.
    /// </para>
    /// </remarks>
    private static bool CanHaveDataAbsentReason(string propertyName, ElementMetadata metadata)
    {
        // Must be mandatory
        if (!metadata.IsRequired)
        {
            return false;
        }

        // Skip system fields (these are never eligible)
        if (SystemFields.Contains(propertyName))
        {
            return false;
        }

        // Check if element is a coded type
        bool isCodedElement = IsCodedElement(metadata);

        // For coded elements, we MUST have binding metadata to make a safe decision
        if (isCodedElement)
        {
            // If no binding strength metadata, we can't safely determine eligibility
            // Be conservative: skip injection (avoid potential required binding violation)
            if (string.IsNullOrEmpty(metadata.BindingStrength))
            {
                return false;
            }

            // Use binding strength to determine eligibility
            // Required bindings without "unknown" code cannot have data-absent-reason
            if (metadata.BindingStrength.Equals("Required", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Extensible, Preferred, Example bindings CAN have data-absent-reason
            // because they allow codes outside the ValueSet or text-only
            return true;
        }

        // Non-coded elements are eligible (no binding concerns)
        return true;
    }

    /// <summary>
    /// Checks if an element is a coded element (code, Coding, CodeableConcept).
    /// </summary>
    private static bool IsCodedElement(ElementMetadata metadata)
    {
        if (metadata.Types == null || metadata.Types.Count == 0)
        {
            return false;
        }

        // Check if any type is a coded type
        return metadata.Types.Any(t => CodedTypes.Contains(t));
    }

    /// <summary>
    /// Creates a FHIR element with data-absent-reason extension.
    /// </summary>
    private static JsonNode CreateDataAbsentReasonElement(ElementMetadata metadata)
    {
        if (metadata.IsCollection)
        {
            // Array element: name: [{ extension: [...] }]
            var arrayNode = new JsonArray();
            var elementWithExtension = CreateElementWithExtension();
            arrayNode.Add(elementWithExtension);
            return arrayNode;
        }
        else
        {
            // Single element: name: { extension: [...] }
            return CreateElementWithExtension();
        }
    }

    /// <summary>
    /// Creates a JSON object with data-absent-reason extension.
    /// </summary>
    private static JsonObject CreateElementWithExtension()
    {
        var element = new JsonObject();
        var extension = new JsonArray
        {
            new JsonObject
            {
                ["url"] = DataAbsentReasonUrl,
                ["valueCode"] = DefaultCode
            }
        };
        element["extension"] = extension;
        return element;
    }
}
