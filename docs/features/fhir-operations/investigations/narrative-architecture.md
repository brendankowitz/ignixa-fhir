# Investigation: Narrative Generator Architecture

**Feature**: fhir-operations
**Status**: Proposed
**Created**: 2025-01-16

**Status**: Proposed
**Date**: 2025-01-16
**Deciders**: Engineering Team

## Context

The NarrativeGenerator project generates WCAG 2.1 AA compliant XHTML narratives for FHIR resources using Scriban templates. Two architectural questions have been raised:

1. Should templates render from `IElement` instead of `ResourceJsonNode`?
2. What's the optimal template generation strategy: pre-generated files, runtime generation, or metadata-driven?

## Decision 1: IElement-Based Rendering ✅ IMPLEMENTED

### Status: ✅ **COMPLETED (2025-01-16)**

### Problem

Templates use `ResourceJsonNode` as the root context object, but only access data via FHIRPath. Every FHIRPath call converts `ResourceJsonNode → IElement`:

```csharp
public string? Path(object resource, object expression)
{
    var resourceNode = (ResourceJsonNode)resource;
    var element = resourceNode.ToElement(_schema);  // ❌ Repeated conversion
    var results = _evaluator.Evaluate(element, parsedExpression);
    // ...
}
```

### Decision

**Switch to `IElement` as the rendering model.**

### Implementation

```csharp
// Entry point: Convert ONCE
public class FhirNarrativeGenerator(ISchema schema, ...)
{
    public async Task<string> GenerateNarrativeAsync(
        ResourceJsonNode resource,
        CultureInfo? culture = null,
        CancellationToken cancellationToken = default)
    {
        var resourceType = resource.ResourceType;
        var fhirVersion = resource.FhirVersion ?? FhirVersion.R4;

        // Convert to IElement ONCE
        var element = resource.ToElement(schema);

        // Template engine works with IElement
        var rendered = await templateEngine.RenderAsync(
            template.Content,
            element,
            resourceType,
            fhirVersion,
            actualCulture,
            cancellationToken);

        return sanitizer.Sanitize(rendered);
    }
}

// FHIRPath functions: No conversion needed
public string? Path(object resource, object expression)
{
    var element = (IElement)resource;  // ✅ Already an IElement
    var results = _evaluator.Evaluate(element, parsedExpression);
    // ...
}
```

### Benefits

1. **Performance**: Conversion happens ONCE at entry point, not on every FHIRPath call
2. **Semantic Correctness**: `IElement` is the proper FHIRPath evaluation model
3. **Type Safety**: Templates can't accidentally access `MutableNode` directly
4. **Clean Abstraction**: FHIRPath works on element tree, not JSON

### Test Results

- ✅ All 41 tests passing
- ✅ 0 warnings, 0 errors
- ✅ Non-breaking change (interface unchanged)

---

## Decision 2: Template Generation Strategy

### Status: **Under Review**

### Current State

- **719 pre-generated `.scriban` templates**
  - 29 normative templates (shared across versions)
  - 690 version-specific templates (R4: 117, R4B: 112, R5: 127, R6: 130, STU3: 88)
- **Deployment size**: ~180KB for normative templates alone
- **Maintenance**: Regenerate when FHIR spec changes

### Options Analysis

#### Option A: Pre-Generated Templates (Status Quo)

**Current Implementation:**
- 719 `.scriban` files embedded as resources
- Generated via `CSharpNarrativeTemplateLanguage.cs`
- Template resolution: Normative → Version-specific → Generic fallback

**Pros:**
- ✅ Fast startup (no runtime codegen)
- ✅ Easy to customize individual resources
- ✅ Can hand-tune for better UX (Patient demographics, Observation values)
- ✅ Deployable as embedded resources
- ✅ Easy to debug (see exact template source)
- ✅ Template compilation cached by Scriban

**Cons:**
- ❌ 719 files to maintain (though auto-generated)
- ❌ Duplication across FHIR versions
- ❌ Regeneration required on spec changes
- ❌ ~180KB+ embedded resource overhead

**Quality Assessment:**
```
Generated Template Quality:
- Average size: ~5.8KB per normative template
- Inline formatting (no hidden helpers)
- Proper ARIA labels and semantic HTML
- FHIRPath-based data extraction
- Localized labels via l10n.t "Key"

Example: Patient.scriban (138 lines)
- Demographics section (name, gender, birthDate)
- Contact information (telecom, address)
- Managing organization
- Active/deceased status
- Identifiers
- Links to related records
```

#### Option B: Runtime Generation from StructureDefinition

**Concept:**
```csharp
public class RuntimeTemplateGenerator
{
    public string GenerateTemplate(IStructureDefinitionSummary sd)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"fhir-{{ resourceType }}\">");

        foreach (var element in sd.Elements.Where(e => e.IsTopLevel))
        {
            sb.Append($"{{{{~ if (fhir.exists resource \"{element.Path}\") ~}}}}");
            sb.Append($"<dt>{{{{ l10n.t \"{sd.TypeName}.{element.Name}\" }}}}</dt>");

            if (element.Type.IsPrimitive)
                sb.Append($"<dd>{{{{ fhir.path resource \"{element.Path}\" }}}}</dd>");
            else
                sb.Append($"<dd>{{{{ fhir.display (fhir.path resource \"{element.Path}\") }}}}</dd>");

            sb.Append("{{{{~ end ~}}}}");
        }

        sb.Append("</div>");
        return sb.ToString();
    }
}
```

**Pros:**
- ✅ No template files to maintain
- ✅ Automatically adapts to spec changes
- ✅ Smaller deployment (~20KB code vs 180KB templates)
- ✅ Single implementation for all FHIR versions

**Cons:**
- ❌ Startup cost (generate + compile on first use per resource type)
- ❌ Generic output (can't hand-tune Patient demographics)
- ❌ Harder to customize per-resource
- ❌ Need caching strategy (memory vs disk)
- ❌ Debugging harder (no source file to inspect)
- ❌ Still need ~719 localization key sets

#### Option C: Enhanced Generic Template with Metadata

**Concept:** Make `Generic.scriban` smart enough to handle ANY resource using StructureDefinition metadata:

```scriban
{{~ # Generic template that adapts to resource structure ~}}
{{~ elements = get_structure_elements resourceType fhirVersion ~}}

<div class="fhir-resource fhir-{{ resourceType | string.downcase }}">
  <h3>{{ l10n.t (resourceType + ".Title") }}</h3>

  <dl class="fhir-details">
    {{~ for element in elements ~}}
      {{~ if (fhir.exists resource element.path) ~}}
        <dt>{{ l10n.t (resourceType + "." + element.name) }}</dt>

        {{~ if element.is_primitive ~}}
          <dd>{{ format_by_type (fhir.path resource element.path) element.type }}</dd>
        {{~ else if element.is_codeable_concept ~}}
          <dd>{{ fhir.display (fhir.path resource element.path) }}</dd>
        {{~ else if element.is_reference ~}}
          <dd>{{ fhir.display_reference (fhir.path resource element.path) }}</dd>
        {{~ else ~}}
          <dd>{{ fhir.path resource element.path }}</dd>
        {{~ end ~}}
      {{~ end ~}}
    {{~ end ~}}
  </dl>
</div>
```

**Required Helpers:**
```csharp
// New Scriban function
public IEnumerable<ElementMetadata> GetStructureElements(string resourceType, FhirVersion version)
{
    var sd = _schema.GetStructureDefinition(resourceType);
    return sd.Elements
        .Where(e => e.Depth == 1)  // Top-level only
        .Select(e => new ElementMetadata
        {
            Name = e.ElementName,
            Path = e.Path,
            Type = e.Type.FirstOrDefault()?.Code,
            IsPrimitive = e.Type.FirstOrDefault()?.IsPrimitive ?? false,
            IsCodeableConcept = e.Type.Any(t => t.Code == "CodeableConcept"),
            IsReference = e.Type.Any(t => t.Code == "Reference")
        });
}
```

**Pros:**
- ✅ ONE template file to maintain
- ✅ Automatically adapts to spec changes
- ✅ Still customizable (edit one file)
- ✅ Easy to debug (see template source)
- ✅ Smaller deployment (~10KB template + helpers)

**Cons:**
- ❌ Can't optimize per-resource layout (Patient demographics vs Observation values)
- ❌ More complex Scriban helpers needed
- ❌ Still need ~719 localization key sets
- ❌ Performance: Metadata lookup on every render (mitigated by caching)

#### Option D: Hybrid Approach (Recommended)

**Proposal:**
1. Keep **Generic.scriban** as the base (with metadata helpers)
2. Allow **hand-coded Normative templates** for 10-15 important resources:
   - Patient (demographics section)
   - Observation (values + reference ranges)
   - Bundle (entry summary)
   - DiagnosticReport (results + conclusions)
   - Condition (clinical status, severity)
   - MedicationRequest (dosage instructions)
   - AllergyIntolerance (reactions, criticality)
   - Composition (sections)
   - DocumentReference (content attachments)
   - OperationOutcome (issues)
3. **Remove 690 generated version-specific templates**
4. **Enhanced Generic fallback** for Trial-Use/rarely-viewed resources

**Architecture:**
```
Template Resolution Priority:
1. Hand-coded Normative/{ResourceType}.scriban (10-15 templates)
2. Generic.scriban with metadata (1 template)

Total templates: ~16 files (vs 719 currently)
Deployment size: ~30KB (vs 180KB+)
```

**Benefits:**
- ✅ 90% of resources use Generic (especially Trial-Use)
- ✅ 10% of important resources get hand-tuned UX
- ✅ Deployment size: 180KB → 30KB (83% reduction)
- ✅ Maintainability: 719 files → 16 files (98% reduction)
- ✅ Still performant (embedded resources, cached compilation)
- ✅ Customizable for important resources
- ✅ Automatic adaptation for new/updated resources

**Migration Path:**
1. Implement metadata helpers for Generic.scriban
2. Test Generic.scriban against all resource types
3. Identify top 10-15 resources needing custom templates (usage metrics)
4. Hand-code those templates for optimal UX
5. Remove generated templates
6. Update template generator to only generate localization keys

### Recommendation Matrix

| Criteria | Generated (Current) | Runtime Gen | Enhanced Generic | **Hybrid** |
|----------|-------------------|-------------|------------------|------------|
| **Startup Performance** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| **Runtime Performance** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| **Customizability** | ⭐⭐⭐⭐⭐ | ⭐ | ⭐⭐⭐ | **⭐⭐⭐⭐⭐** |
| **Maintainability** | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | **⭐⭐⭐⭐⭐** |
| **Deployment Size** | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | **⭐⭐⭐⭐⭐** |
| **Debuggability** | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ | **⭐⭐⭐⭐⭐** |
| **Spec Adaptability** | ⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | **⭐⭐⭐⭐⭐** |
| **UX Quality** | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | **⭐⭐⭐⭐⭐** |

---

## Proposed Implementation Plan (Hybrid Approach)

### Phase 1: Enhance Generic Template (1-2 days)

1. Add metadata helper functions:
   ```csharp
   scriptObject.Import("get_structure_elements",
       (Func<string, FhirVersion, IEnumerable<ElementMetadata>>)GetStructureElements);
   scriptObject.Import("format_by_type",
       (Func<string?, string, string>)FormatByType);
   ```

2. Update `Generic.scriban` to use metadata:
   - Iterate over top-level elements from StructureDefinition
   - Render based on element type (primitive, CodeableConcept, Reference, etc.)
   - Use conditional formatting

3. Test Generic template against all 719 resource types

### Phase 2: Identify Core Templates (1 day)

1. Analyze resource usage patterns (if available)
2. Select top 10-15 resources for hand-coding:
   - **Clinical**: Patient, Observation, Condition, Procedure, DiagnosticReport
   - **Medications**: MedicationRequest, MedicationStatement, AllergyIntolerance
   - **Documents**: Bundle, Composition, DocumentReference
   - **Admin**: OperationOutcome, CapabilityStatement
   - **Infrastructure**: Organization, Practitioner

3. Design custom layouts for each

### Phase 3: Hand-Code Priority Templates (2-3 days)

1. Create hand-coded templates for selected resources
2. Focus on optimal UX (e.g., Patient demographics section)
3. Test thoroughly

### Phase 4: Remove Generated Templates (1 day)

1. Delete `Generated/Templates/{Version}/*.scriban` folders
2. Keep only:
   - `Templates/Normative/Generic.scriban` (enhanced)
   - `Templates/Normative/{CoreResource}.scriban` (10-15 files)
3. Update `TemplateResolver` to prioritize: Hand-coded → Generic
4. Update template generator to only generate localization keys

### Phase 5: Verify & Deploy (1 day)

1. Run full test suite
2. Manual testing of all core resource types
3. Performance benchmarks
4. Documentation updates

**Total Effort**: ~6-8 days

---

## Consequences

### Decision 1: IElement Rendering (Implemented)

**Positive:**
- ✅ Better performance (single conversion)
- ✅ Cleaner abstraction
- ✅ Type safety

**Neutral:**
- ⚪ Minimal code changes required
- ⚪ Non-breaking change

**Negative:**
- None identified

### Decision 2: Template Strategy (Proposed: Hybrid)

**Positive:**
- ✅ 98% reduction in template files (719 → 16)
- ✅ 83% reduction in deployment size (180KB → 30KB)
- ✅ Automatic adaptation to spec changes
- ✅ Optimal UX for important resources
- ✅ Easy to maintain

**Neutral:**
- ⚪ One-time migration effort (~6-8 days)
- ⚪ Need to select core templates based on usage

**Negative:**
- ❌ Trial-Use resources get generic rendering (acceptable for rarely-viewed resources)
- ❌ Need to implement metadata helpers

---

## References

- [Scriban Documentation](https://github.com/scriban/scriban)
- [FHIR Narrative Documentation](https://hl7.org/fhir/narrative.html)
- [WCAG 2.1 AA Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)

---

## Related ADRs

- ADR-2600: IPS Generator Implementation Investigation
- ADR-2601: IPS Generator Implementation
- ADR-2500: Master Roadmap
