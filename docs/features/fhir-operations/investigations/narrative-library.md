# Investigation: Narrative Generator Library

**Feature**: fhir-operations
**Status**: Merged
**Created**: 2025-01-16
**Implemented**: PR #125 (Ignixa.NarrativeGenerator)
**Date**: 2025-12-16
**Package**: Core SDK (public NuGet package)
**Dependencies**:
- Ignixa.FhirPath (FHIRPath evaluation engine)
- Ignixa.Abstractions (multi-version resource models)
- fhir-codegen (template generation tooling)
**CI/CD Impact**: Requires updates to Core SDK build and release pipelines

---

## Context

### Problem Statement

FHIR narrative generation is required across multiple features:
1. **IPS Generator** - Section narratives for patient summaries (ADR-2601)
2. **Resource Display** - Human-readable rendering in UI/APIs
3. **Document Generation** - Composition narratives, clinical notes
4. **Accessibility Compliance** - WCAG 2.1 requirements for FHIR UIs
5. **Debugging/Audit** - Human-readable resource dumps

Current challenges:
- No centralized narrative generation infrastructure
- Each feature would implement its own templating (code duplication)
- Multi-version FHIR support requires version-aware templates
- Template customization is critical for production deployments
- FHIRPath should be leveraged for clean, declarative expressions

### Goals

1. **Multi-Version Support** - STU3, R4, R4B, R5 with version-aware templates
2. **Tiered Rendering** - Rich templates for normative resources, basic ToText for others
3. **FHIRPath-First** - Use FHIRPath for data extraction, not brittle property navigation
4. **Extensibility** - Easy to override built-in templates or add custom ones
5. **Performance** - Compiled templates, cached FHIRPath expressions
6. **Codegen Integration** - Leverage fhir-codegen for template scaffolding

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    INarrativeGenerator                          │
│  Public API: GenerateNarrativeAsync(resource, options)         │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                    NarrativeGeneratorService                    │
│  - Template resolution (built-in vs custom)                    │
│  - Version detection (STU3/R4/R4B/R5)                          │
│  - Tier selection (Rich/Medium/Basic)                          │
└────────────────────────────┬────────────────────────────────────┘
                             │
                  ┌──────────┴──────────┐
                  │                     │
                  ▼                     ▼
┌─────────────────────────────┐  ┌──────────────────────────────┐
│   TemplateNarrativeEngine   │  │   ToTextNarrativeEngine      │
│  - Scriban templates        │  │  - FHIRPath-based fallback   │
│  - FHIRPath helpers         │  │  - Auto-generated display    │
│  - XHTML output             │  │  - Plain text output         │
└─────────────────────────────┘  └──────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Template Registry                          │
│  Built-in:                                                      │
│    Templates/                                                   │
│      R4/                                                        │
│        Patient.scriban         (Rich - Normative)              │
│        Observation.scriban     (Rich - Normative)              │
│        AllergyIntolerance.scriban (Medium)                     │
│        Condition.scriban       (Medium)                        │
│        Generic.scriban         (Fallback)                      │
│      R5/                                                        │
│        Patient.scriban                                         │
│        ... (same structure)                                    │
│  Custom:                                                        │
│    User-provided templates via configuration                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Design Decisions

### 1. Template Organization Strategy

#### Recommended Approach: Normative + Version-Specific Overrides

```
Templates/
├── Normative/                          # Stable across FHIR versions
│   ├── Patient.scriban                 # Normative in R4+
│   ├── Observation.scriban             # Normative in R4+
│   ├── Medication.scriban              # Normative in R4+
│   ├── Encounter.scriban               # Normative in R4+
│   ├── Procedure.scriban               # Normative in R4+
│   ├── Condition.scriban               # Normative in R4+
│   ├── AllergyIntolerance.scriban      # Normative in R4+
│   ├── DiagnosticReport.scriban        # Normative in R4+
│   └── Generic.scriban                 # Fallback for all versions
├── R4/                                 # R4-specific overrides/additions
│   ├── Immunization.scriban            # Trial-use (breaking changes in R5)
│   ├── MedicationRequest.scriban       # Trial-use
│   └── CarePlan.scriban                # Trial-use
├── R5/                                 # R5-specific overrides/additions
│   ├── Immunization.scriban            # R5 has breaking changes from R4
│   ├── MedicationRequest.scriban       # R5 changes
│   └── Consent.scriban                 # R5 changes
├── R4B/                                # R4B-specific (rare)
│   └── (only if R4B differs from R4)
└── STU3/                               # STU3-specific
    ├── Patient.scriban                 # STU3 predates normative
    └── Observation.scriban             # STU3 predates normative
```

**Rationale**:
- **Normative resources** (Patient, Observation, etc.) are **stable across FHIR versions** → single template in `/Normative`
- **Trial-use resources** (Immunization, MedicationRequest) may have **breaking changes** → version-specific folders
- **Reduces duplication** - Patient template written once, not 4 times
- **Clear intent** - `/Normative` indicates stability, version folders indicate variance
- **Easy to maintain** - Update Patient template in one place, all versions benefit

#### Template Resolution Logic

```csharp
public Template? ResolveTemplate(string resourceType, FhirVersion version)
{
    // 1. Check version-specific folder first (e.g., R4/Immunization.scriban)
    var versionSpecific = $"{version}/{resourceType}.scriban";
    if (_templates.ContainsKey(versionSpecific))
    {
        return _templates[versionSpecific];
    }

    // 2. Fallback to Normative folder (e.g., Normative/Patient.scriban)
    var normative = $"Normative/{resourceType}.scriban";
    if (_templates.ContainsKey(normative))
    {
        return _templates[normative];
    }

    // 3. Use Generic fallback
    var generic = $"{version}/Generic.scriban";
    if (_templates.ContainsKey(generic))
    {
        return _templates[generic];
    }

    // 4. Use Normative Generic fallback
    return _templates["Normative/Generic.scriban"];
}
```

**Example Resolution**:
- `Patient` (R4) → `Normative/Patient.scriban` ✅
- `Patient` (R5) → `Normative/Patient.scriban` ✅ (same template)
- `Immunization` (R4) → `R4/Immunization.scriban` ✅
- `Immunization` (R5) → `R5/Immunization.scriban` ✅ (different template)
- `MedicationAdministration` (R4) → `R4/Generic.scriban` ✅ (no specific template)

#### FHIR Normative Resources (Stable Across Versions)

Per [FHIR Normative List](https://hl7.org/fhir/versions.html#normative), these resources are stable:

| Resource | Normative Since | Template Location |
|----------|-----------------|-------------------|
| Patient | R4 | `Normative/Patient.scriban` |
| Observation | R4 | `Normative/Observation.scriban` |
| Medication | R4 | `Normative/Medication.scriban` |
| Encounter | R4 | `Normative/Encounter.scriban` |
| Procedure | R4 | `Normative/Procedure.scriban` |
| Condition | R4 | `Normative/Condition.scriban` |
| AllergyIntolerance | R4 | `Normative/AllergyIntolerance.scriban` |
| DiagnosticReport | R4 | `Normative/DiagnosticReport.scriban` |

#### Trial-Use Resources (Version-Specific Templates Needed)

| Resource | Breaking Changes | Templates |
|----------|------------------|-----------|
| Immunization | R4 → R5 changes | `R4/Immunization.scriban`, `R5/Immunization.scriban` |
| MedicationRequest | R4 → R5 changes | `R4/MedicationRequest.scriban`, `R5/MedicationRequest.scriban` |
| CarePlan | R4 → R5 changes | `R4/CarePlan.scriban`, `R5/CarePlan.scriban` |
| Consent | R4 → R5 major overhaul | `R4/Consent.scriban`, `R5/Consent.scriban` |
| DocumentReference | R4 → R5 changes | `R4/DocumentReference.scriban`, `R5/DocumentReference.scriban` |

**Benefits**:
1. ✅ **Reduced Maintenance** - Update Patient once, not 4 times
2. ✅ **Clear Intent** - Normative = stable, version folders = variance
3. ✅ **Easy to Add Versions** - New FHIR version? Just add trial-use overrides
4. ✅ **Prevents Drift** - Single source of truth for normative resources
5. ✅ **FHIRPath-Based** - Templates use version-agnostic FHIRPath expressions

---

### 2. Template Tiers

#### Tier 1: Rich Templates (Normative Resources)

**Target Resources**: Patient, Observation, Medication, Encounter, Procedure, Condition, AllergyIntolerance, DiagnosticReport, Immunization

**Characteristics**:
- Custom HTML layout with semantic structure
- Accessibility-compliant (ARIA labels, proper headings)
- Styled tables, lists, sections
- Handles complex nested structures (e.g., Observation components)

**Example**: Patient.scriban
```html
<div class="fhir-patient">
  <h3>{{ fhirpath resource "name.given.first()" }} {{ fhirpath resource "name.family" }}</h3>
  <dl>
    <dt>Birth Date</dt>
    <dd>{{ format_date (fhirpath resource "birthDate") }}</dd>

    <dt>Gender</dt>
    <dd>{{ fhirpath resource "gender" }}</dd>

    {{ if (fhirpath resource "telecom.exists()") }}
    <dt>Contact</dt>
    <dd>
      <ul>
        {{ for contact in (fhirpath_all resource "telecom") }}
        <li>{{ contact.system }}: {{ contact.value }}</li>
        {{ end }}
      </ul>
    </dd>
    {{ end }}
  </dl>
</div>
```

#### Tier 2: Medium Templates (Common Resources)

**Target Resources**: MedicationRequest, CarePlan, DocumentReference, Questionnaire, QuestionnaireResponse

**Characteristics**:
- Table-based rendering
- Key fields extracted via FHIRPath
- Less custom styling, more generic structure

**Example**: MedicationRequest.scriban
```html
<div class="fhir-medication-request">
  <table>
    <tr>
      <th>Medication</th>
      <td>{{ fhirpath resource "medicationCodeableConcept.coding.display.first() | medicationCodeableConcept.text" }}</td>
    </tr>
    <tr>
      <th>Status</th>
      <td>{{ fhirpath resource "status" }}</td>
    </tr>
    <tr>
      <th>Dosage</th>
      <td>{{ fhirpath resource "dosageInstruction.text.first()" }}</td>
    </tr>
  </table>
</div>
```

#### Tier 3: Basic ToText (Fallback)

**Target Resources**: All others (150+ resource types)

**Characteristics**:
- Auto-generated via FHIRPath
- Plain text or minimal HTML
- No custom layout

**Implementation**:
```csharp
public class ToTextNarrativeEngine
{
    public string GenerateToText(ResourceJsonNode resource)
    {
        var resourceType = resource.ResourceType;
        var text = $"{resourceType}";

        // Try standard display patterns
        var display = TryGetDisplay(resource);
        if (display is not null)
        {
            text += $": {display}";
        }

        return $"<div>{text}</div>";
    }

    private string? TryGetDisplay(ResourceJsonNode resource)
    {
        // Try common FHIR patterns in order
        var patterns = new[]
        {
            "title",                                    // Composition, DocumentReference
            "name",                                     // Patient, Practitioner, Organization
            "code.coding.display.first() | code.text",  // CodeableConcept-based
            "description",                              // StructureDefinition, etc.
            "text.div"                                  // Existing narrative
        };

        foreach (var pattern in patterns)
        {
            var result = _compiler.Compile(pattern).Evaluate(resource.ToTypedElement());
            var value = result.FirstOrDefault()?.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
```

---

### 3. FHIRPath Integration

#### Custom Scriban Functions

Register FHIRPath helpers in template context:

```csharp
public class FhirPathScriptFunctions
{
    private readonly IFhirPathCompiler _compiler;

    // Single value extraction
    [ScriptMemberIgnore]
    public string FhirPath(ResourceJsonNode resource, string expression)
    {
        var compiled = _compiler.Compile(expression);
        var result = compiled.Evaluate(resource.ToTypedElement(), EvaluationContext.CreateDefault());
        return result.FirstOrDefault()?.Value?.ToString() ?? "";
    }

    // Multiple value extraction (for loops)
    [ScriptMemberIgnore]
    public IEnumerable<ITypedElement> FhirPathAll(ResourceJsonNode resource, string expression)
    {
        var compiled = _compiler.Compile(expression);
        return compiled.Evaluate(resource.ToTypedElement(), EvaluationContext.CreateDefault());
    }

    // CodeableConcept display (common pattern)
    [ScriptMemberIgnore]
    public string Display(object? codeableConcept)
    {
        if (codeableConcept is null) return "";

        // Standard FHIR display pattern: coding.display | text
        var expression = "coding.where(display.exists()).first().display | text";
        return FhirPath(codeableConcept, expression);
    }

    // Date formatting
    [ScriptMemberIgnore]
    public string FormatDate(string? fhirDate)
    {
        if (string.IsNullOrWhiteSpace(fhirDate)) return "";

        if (DateTime.TryParse(fhirDate, out var date))
        {
            return date.ToString("yyyy-MM-dd");
        }

        return fhirDate;
    }

    // Quantity formatting
    [ScriptMemberIgnore]
    public string FormatQuantity(object? quantity)
    {
        if (quantity is null) return "";

        var value = FhirPath(quantity, "value");
        var unit = FhirPath(quantity, "unit | code");

        return $"{value} {unit}".Trim();
    }
}
```

#### Template Usage

```html
<!-- Clean, declarative FHIRPath expressions -->
<div class="observation">
  <h4>{{ fhirpath resource "code.coding.display.first()" }}</h4>

  <dl>
    <dt>Value</dt>
    <dd>{{ format_quantity (fhirpath resource "valueQuantity") }}</dd>

    <dt>Effective</dt>
    <dd>{{ format_date (fhirpath resource "effectiveDateTime") }}</dd>

    <dt>Status</dt>
    <dd>{{ fhirpath resource "status" }}</dd>
  </dl>

  <!-- Loop over components -->
  {{ if (fhirpath resource "component.exists()") }}
  <h5>Components</h5>
  <ul>
    {{ for component in (fhirpath_all resource "component") }}
    <li>
      {{ fhirpath component "code.coding.display.first()" }}:
      {{ format_quantity (fhirpath component "valueQuantity") }}
    </li>
    {{ end }}
  </ul>
  {{ end }}
</div>
```

---

### 4. fhir-codegen Integration

#### Option 1: Generate Template Stubs

**Command**: `fhir-codegen generate-narrative-templates`

**Output**: Scaffold templates for all resource types with placeholder FHIRPath expressions

```bash
# Generate R4 templates
fhir-codegen generate-narrative-templates \
  --fhir-version R4 \
  --output Templates/R4 \
  --tier rich \
  --resources Patient,Observation,Condition

# Output: Templates/R4/Patient.scriban (with common fields pre-populated)
```

**Generated Template**:
```html
<!-- Auto-generated by fhir-codegen -->
<!-- Resource: Patient (R4) -->
<div class="fhir-patient">
  <h3>Patient</h3>
  <dl>
    <!-- Common Patient fields (detected from StructureDefinition) -->
    <dt>Name</dt>
    <dd>{{ fhirpath resource "name.given.first()" }} {{ fhirpath resource "name.family" }}</dd>

    <dt>Birth Date</dt>
    <dd>{{ format_date (fhirpath resource "birthDate") }}</dd>

    <!-- TODO: Customize layout and add additional fields -->
  </dl>
</div>
```

**Benefits**:
- Bootstraps template creation
- Ensures FHIRPath expressions match FHIR version
- Detects common fields from StructureDefinition metadata

#### Option 2: Generate ViewModel Classes

**Command**: `fhir-codegen generate-narrative-models`

**Output**: Strongly-typed ViewModels for use in C# preprocessing

```csharp
// Auto-generated: PatientNarrativeViewModel.cs
public record PatientNarrativeViewModel
{
    public string FullName { get; init; } = "";
    public string BirthDate { get; init; } = "";
    public string Gender { get; init; } = "";
    public IReadOnlyList<ContactViewModel> Contacts { get; init; } = [];

    public static PatientNarrativeViewModel FromResource(PatientJsonNode patient, IFhirPathCompiler compiler)
    {
        return new PatientNarrativeViewModel
        {
            FullName = compiler.Evaluate(patient, "name.given.first() + ' ' + name.family").FirstOrDefault()?.Value?.ToString() ?? "",
            BirthDate = compiler.Evaluate(patient, "birthDate").FirstOrDefault()?.Value?.ToString() ?? "",
            Gender = patient.Gender ?? "",
            Contacts = ExtractContacts(patient, compiler)
        };
    }
}
```

**Template becomes trivial**:
```html
<div class="fhir-patient">
  <h3>{{ model.full_name }}</h3>
  <dl>
    <dt>Birth Date</dt>
    <dd>{{ model.birth_date }}</dd>
    <dt>Gender</dt>
    <dd>{{ model.gender }}</dd>
  </dl>
</div>
```

**Decision**: **Implement Option 1 (template stubs)** first. Option 2 (ViewModels) is valuable but adds complexity - can be added later if templates become unwieldy.

#### Option 3: Generate FHIRPath Expression Constants

**Output**: C# constants for common FHIRPath patterns

```csharp
// Auto-generated: FhirPathExpressions.R4.cs
public static class FhirPathExpressions
{
    public static class Patient
    {
        public const string FullName = "name.given.first() + ' ' + name.family";
        public const string BirthDate = "birthDate";
        public const string PrimaryPhone = "telecom.where(system='phone').value.first()";
    }

    public static class Observation
    {
        public const string Code = "code.coding.display.first() | code.text";
        public const string Value = "value.ofType(Quantity).value + ' ' + value.ofType(Quantity).unit";
        public const string EffectiveDateTime = "effectiveDateTime | effectivePeriod.start";
    }
}
```

**Usage in templates**:
```csharp
// In ScribanNarrativeEngine.cs
scriptObject.Import("expressions", FhirPathExpressions.Patient);
```

```html
<!-- In template -->
<h3>{{ fhirpath resource expressions.full_name }}</h3>
```

**Decision**: **Implement this** - provides compile-time safety for common expressions, good middle ground.

---

### 5. Template Override Mechanism

#### Configuration

```csharp
public class NarrativeGeneratorOptions
{
    /// <summary>
    /// Custom template directory. If provided, overrides built-in templates.
    /// </summary>
    public string? CustomTemplateDirectory { get; set; }

    /// <summary>
    /// Template resolution strategy.
    /// </summary>
    public TemplateResolutionStrategy Strategy { get; set; } = TemplateResolutionStrategy.CustomThenBuiltIn;

    /// <summary>
    /// FHIR version-specific template directories.
    /// </summary>
    public Dictionary<FhirVersion, string> VersionSpecificTemplates { get; set; } = new();

    /// <summary>
    /// Fallback to ToText for resources without templates.
    /// </summary>
    public bool EnableToTextFallback { get; set; } = true;

    /// <summary>
    /// Cache compiled templates.
    /// </summary>
    public bool CacheTemplates { get; set; } = true;
}

public enum TemplateResolutionStrategy
{
    BuiltInOnly,           // Only use embedded templates
    CustomOnly,            // Only use custom templates (fail if not found)
    CustomThenBuiltIn,     // Try custom first, fallback to built-in
    BuiltInThenCustom      // Try built-in first, allow custom overrides
}
```

#### Template Resolution

```csharp
public class TemplateResolver
{
    private readonly NarrativeGeneratorOptions _options;
    private readonly FrozenDictionary<string, Template> _builtInTemplates;
    private readonly ConcurrentDictionary<string, Template> _customTemplates;

    public Template? ResolveTemplate(string resourceType, FhirVersion version)
    {
        var key = $"{version}/{resourceType}";

        return _options.Strategy switch
        {
            TemplateResolutionStrategy.CustomOnly => LoadCustomTemplate(key),
            TemplateResolutionStrategy.BuiltInOnly => _builtInTemplates.GetValueOrDefault(key),
            TemplateResolutionStrategy.CustomThenBuiltIn =>
                LoadCustomTemplate(key) ?? _builtInTemplates.GetValueOrDefault(key),
            TemplateResolutionStrategy.BuiltInThenCustom =>
                _builtInTemplates.GetValueOrDefault(key) ?? LoadCustomTemplate(key),
            _ => null
        };
    }

    private Template? LoadCustomTemplate(string key)
    {
        if (_options.CustomTemplateDirectory is null)
        {
            return null;
        }

        return _customTemplates.GetOrAdd(key, k =>
        {
            var path = Path.Combine(_options.CustomTemplateDirectory, $"{k}.scriban");
            if (!File.Exists(path))
            {
                return null;
            }

            var content = File.ReadAllText(path);
            return Template.Parse(content);
        });
    }
}
```

#### Usage

```csharp
// appsettings.json
{
  "NarrativeGenerator": {
    "CustomTemplateDirectory": "/app/narrative-templates",
    "Strategy": "CustomThenBuiltIn",
    "EnableToTextFallback": true,
    "VersionSpecificTemplates": {
      "R4": "/app/narrative-templates/R4",
      "R5": "/app/narrative-templates/R5"
    }
  }
}

// Program.cs
builder.Services.AddNarrativeGenerator(options =>
{
    builder.Configuration.GetSection("NarrativeGenerator").Bind(options);
});

// Usage in code
var narrative = await _narrativeGenerator.GenerateNarrativeAsync(patient);
```

---

### 6. Public API

```csharp
// File: Api/INarrativeGenerator.cs
public interface INarrativeGenerator
{
    /// <summary>
    /// Generates XHTML narrative for a FHIR resource.
    /// </summary>
    Task<string> GenerateNarrativeAsync(
        ResourceJsonNode resource,
        NarrativeGenerationContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates narratives for multiple resources (batch).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GenerateNarrativesAsync(
        IEnumerable<ResourceJsonNode> resources,
        NarrativeGenerationContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a rich template exists for a resource type.
    /// </summary>
    bool HasTemplate(string resourceType, FhirVersion version);

    /// <summary>
    /// Gets the template tier for a resource type.
    /// </summary>
    TemplateTier GetTemplateTier(string resourceType, FhirVersion version);
}

public record NarrativeGenerationContext
{
    /// <summary>
    /// Additional data available to templates (e.g., related resources).
    /// </summary>
    public Dictionary<string, object> ContextData { get; init; } = new();

    /// <summary>
    /// Override FHIR version detection.
    /// </summary>
    public FhirVersion? FhirVersionOverride { get; init; }

    /// <summary>
    /// Custom template name to use.
    /// </summary>
    public string? CustomTemplateName { get; init; }

    /// <summary>
    /// Output format.
    /// </summary>
    public NarrativeFormat Format { get; init; } = NarrativeFormat.Xhtml;
}

public enum NarrativeFormat
{
    Xhtml,      // FHIR-compliant XHTML
    PlainText,  // Plain text (accessibility)
    Markdown    // Markdown (for human editing)
}

public enum TemplateTier
{
    Rich,       // Custom template with full layout
    Medium,     // Table-based template
    Basic,      // Auto-generated ToText
    None        // No template available
}
```

---

## Server Integration (Ignixa Pipeline)

### Automatic Narrative Generation via Medino Behavior

The narrative generator integrates into the Ignixa server pipeline as a **Medino behavior** that automatically generates narratives for resources after CRUD operations. This is similar to how validation behaviors work.

#### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    FHIR API Request                             │
│  POST /Patient, PUT /Patient/123, PATCH /Patient/123           │
└────────────────────────────┬────────────────────────────────────┘
                             │ IMediator.SendAsync
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                   Medino Pipeline Behaviors                     │
│  1. ValidationBehavior (validate resource)                     │
│  2. AuthorizationBehavior (check permissions)                  │
│  3. NarrativeGenerationBehavior ← NEW                          │
│  4. AuditBehavior (log operation)                              │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│              Handler (Create/Update/Patch)                      │
│  Persists resource to database                                 │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│         Response (resource with .text narrative)                │
└─────────────────────────────────────────────────────────────────┘
```

#### NarrativeGenerationBehavior Implementation

```csharp
// File: src/Ignixa.Application/Behaviors/NarrativeGenerationBehavior.cs
public class NarrativeGenerationBehavior<TRequest, TResponse>(
    INarrativeGenerator narrativeGenerator,
    NarrativeGenerationOptions options,
    ILogger<NarrativeGenerationBehavior<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Execute the handler first (create/update/patch)
        var response = await next();

        // Only generate narratives if enabled
        if (!options.Enabled)
        {
            return response;
        }

        // Generate narrative for single resource responses
        if (response is IResourceResponse resourceResponse)
        {
            await GenerateNarrativeAsync(resourceResponse.Resource, cancellationToken);
        }
        // Generate narratives for bundle responses (search results)
        else if (response is IBundleResponse bundleResponse && options.GenerateForSearchResults)
        {
            await GenerateNarrativesForBundleAsync(bundleResponse.Bundle, cancellationToken);
        }

        return response;
    }

    private async Task GenerateNarrativeAsync(
        ResourceJsonNode resource,
        CancellationToken cancellationToken)
    {
        // Skip if resource already has a narrative and we shouldn't override
        if (resource.Text is not null && !options.OverrideExistingNarrative)
        {
            logger.LogDebug(
                "Skipping narrative generation for {ResourceType}/{Id} - narrative already exists",
                resource.ResourceType,
                resource.Id);
            return;
        }

        try
        {
            var narrative = await narrativeGenerator.GenerateNarrativeAsync(
                resource,
                context: null,
                cancellationToken);

            // Update resource.text
            resource.Text = new NarrativeJsonNode
            {
                Status = "generated",
                Div = narrative
            };

            logger.LogDebug(
                "Generated narrative for {ResourceType}/{Id}",
                resource.ResourceType,
                resource.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to generate narrative for {ResourceType}/{Id}",
                resource.ResourceType,
                resource.Id);

            // Don't fail the request if narrative generation fails
            if (options.FailOnError)
            {
                throw;
            }
        }
    }

    private async Task GenerateNarrativesForBundleAsync(
        BundleJsonNode bundle,
        CancellationToken cancellationToken)
    {
        if (bundle.Entry is null)
        {
            return;
        }

        var tasks = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => GenerateNarrativeAsync(e.Resource!, cancellationToken));

        await Task.WhenAll(tasks);
    }
}

// Marker interfaces for type safety
public interface IResourceResponse
{
    ResourceJsonNode Resource { get; }
}

public interface IBundleResponse
{
    BundleJsonNode Bundle { get; }
}
```

#### Configuration Options

```csharp
// File: src/Ignixa.Application/Behaviors/NarrativeGenerationOptions.cs
public class NarrativeGenerationOptions
{
    /// <summary>
    /// Enable automatic narrative generation (default: true).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Override existing narratives (default: false).
    /// If false, only generate narratives when resource.text is null.
    /// </summary>
    public bool OverrideExistingNarrative { get; set; } = false;

    /// <summary>
    /// Generate narratives for search result bundles (default: false).
    /// WARNING: Can be expensive for large search results.
    /// </summary>
    public bool GenerateForSearchResults { get; set; } = false;

    /// <summary>
    /// Fail request if narrative generation fails (default: false).
    /// If false, log warning and continue without narrative.
    /// </summary>
    public bool FailOnError { get; set; } = false;

    /// <summary>
    /// Resource types to include in automatic generation.
    /// If empty, all resource types are included.
    /// </summary>
    public HashSet<string> IncludedResourceTypes { get; set; } = [];

    /// <summary>
    /// Resource types to exclude from automatic generation.
    /// </summary>
    public HashSet<string> ExcludedResourceTypes { get; set; } = [];

    /// <summary>
    /// Only generate narratives for operations (Create, Update, Patch).
    /// If empty, all operations trigger generation.
    /// </summary>
    public HashSet<FhirOperation> IncludedOperations { get; set; } = [];

    /// <summary>
    /// Determines if narrative should be generated for a resource type.
    /// </summary>
    public bool ShouldGenerateFor(string resourceType, FhirOperation operation)
    {
        // Check excluded types first
        if (ExcludedResourceTypes.Contains(resourceType))
        {
            return false;
        }

        // If included types specified, must be in the list
        if (IncludedResourceTypes.Count > 0 && !IncludedResourceTypes.Contains(resourceType))
        {
            return false;
        }

        // Check operation filter
        if (IncludedOperations.Count > 0 && !IncludedOperations.Contains(operation))
        {
            return false;
        }

        return true;
    }
}

public enum FhirOperation
{
    Create,
    Read,
    Update,
    Patch,
    Delete,
    Search
}
```

#### Registration in Program.cs

```csharp
// File: src/Ignixa.Api/Program.cs

// Register NarrativeGenerator library
builder.Services.AddNarrativeGenerator(options =>
{
    builder.Configuration.GetSection("NarrativeGenerator").Bind(options);
});

// Register Medino behavior
builder.RegisterType<NarrativeGenerationBehavior<,>>()
    .As(typeof(IPipelineBehavior<,>))
    .InstancePerLifetimeScope();

// Configure options
builder.Services.Configure<NarrativeGenerationOptions>(options =>
{
    builder.Configuration.GetSection("NarrativeGeneration").Bind(options);
});
```

#### appsettings.json Configuration

```jsonc
{
  "NarrativeGenerator": {
    "CustomTemplateDirectory": "/app/narrative-templates",
    "Strategy": "CustomThenBuiltIn",
    "EnableToTextFallback": true
  },
  "NarrativeGeneration": {
    // Feature flag: enable/disable automatic generation
    "Enabled": true,

    // Don't override manually created narratives
    "OverrideExistingNarrative": false,

    // Don't generate for search results (performance)
    "GenerateForSearchResults": false,

    // Don't fail requests if narrative generation fails
    "FailOnError": false,

    // Only generate for specific resource types (optional)
    "IncludedResourceTypes": [
      "Patient",
      "Observation",
      "Condition",
      "AllergyIntolerance"
    ],

    // Or exclude specific types
    "ExcludedResourceTypes": [
      "AuditEvent",    // Too verbose
      "Provenance",    // Internal tracking
      "Binary"         // No meaningful narrative
    ],

    // Only generate for write operations (optional)
    "IncludedOperations": [
      "Create",
      "Update",
      "Patch"
    ]
  }
}
```

#### Environment-Specific Configuration

```jsonc
// appsettings.Development.json - Verbose narratives for debugging
{
  "NarrativeGeneration": {
    "Enabled": true,
    "OverrideExistingNarrative": true,  // Always regenerate
    "GenerateForSearchResults": true,   // Include in search
    "FailOnError": true                 // Fail fast in dev
  }
}

// appsettings.Production.json - Performance-optimized
{
  "NarrativeGeneration": {
    "Enabled": true,
    "OverrideExistingNarrative": false,  // Respect client narratives
    "GenerateForSearchResults": false,   // Too expensive
    "FailOnError": false,                // Don't break production
    "ExcludedResourceTypes": [
      "AuditEvent",
      "Provenance",
      "Binary",
      "DocumentReference"  // Large documents
    ]
  }
}
```

### Integration with Existing Operations

#### Create Operation

```csharp
// File: src/Ignixa.Application/Features/Create/CreateResourceHandler.cs
public class CreateResourceHandler : IRequestHandler<CreateResourceCommand, ResourceResponse>
{
    public async Task<ResourceResponse> HandleAsync(
        CreateResourceCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Validate resource (ValidationBehavior)
        // 2. Check authorization (AuthorizationBehavior)
        // 3. Persist to database
        var resource = await _repository.CreateAsync(request.Resource, cancellationToken);

        // 4. NarrativeGenerationBehavior automatically runs after this
        return new ResourceResponse(resource);
    }
}
```

#### Update Operation

```csharp
// File: src/Ignixa.Application/Features/Update/UpdateResourceHandler.cs
public class UpdateResourceHandler : IRequestHandler<UpdateResourceCommand, ResourceResponse>
{
    public async Task<ResourceResponse> HandleAsync(
        UpdateResourceCommand request,
        CancellationToken cancellationToken)
    {
        // Update logic
        var updated = await _repository.UpdateAsync(request.Resource, cancellationToken);

        // NarrativeGenerationBehavior runs automatically
        return new ResourceResponse(updated);
    }
}
```

#### Patch Operation

**CRITICAL**: Patch operations require special handling because the narrative might reference fields that were just modified.

```csharp
// File: src/Ignixa.Application/Features/Patch/PatchResourceHandler.cs
public class PatchResourceHandler : IRequestHandler<PatchResourceCommand, ResourceResponse>
{
    public async Task<ResourceResponse> HandleAsync(
        PatchResourceCommand request,
        CancellationToken cancellationToken)
    {
        // Apply patch operations
        var patched = await ApplyPatchAsync(request, cancellationToken);

        // IMPORTANT: Narrative is regenerated AFTER patch is applied
        // NarrativeGenerationBehavior will see the updated resource
        return new ResourceResponse(patched);
    }
}
```

**Example Flow**:
```
1. PATCH /Patient/123 (change name.family to "Smith")
2. PatchResourceHandler applies patch
3. NarrativeGenerationBehavior runs
4. Template uses {{ fhirpath resource "name.family" }}
5. Narrative shows "Smith" (the new value) ✅
```

### Performance Considerations

#### Caching Strategy

The behavior should leverage the template cache in `NarrativeGeneratorService`:

```csharp
// Templates are compiled once and cached
private readonly FrozenDictionary<string, Template> _templates;

// FHIRPath expressions are compiled once and cached
private readonly ConcurrentDictionary<string, CompiledExpression> _compiledExpressions;
```

**Performance Metrics**:
- First request: ~100ms (template compilation + narrative generation)
- Subsequent requests: ~50ms (cached template + narrative generation)
- Memory overhead: ~5MB for template cache

#### Async Processing for Large Bundles

For search results with many resources:

```csharp
// Generate narratives in parallel
var tasks = bundle.Entry
    .Select(e => GenerateNarrativeAsync(e.Resource, cancellationToken));

await Task.WhenAll(tasks);
```

**Recommendation**: Disable `GenerateForSearchResults` by default due to:
- Search bundles can contain 100+ resources
- Narrative generation is ~50ms per resource
- Total latency: 100 resources × 50ms = 5 seconds (unacceptable)
- Better to generate on-demand or use pagination

### Opt-Out Mechanism

Clients can opt-out of narrative generation via HTTP header:

```csharp
public class NarrativeGenerationBehavior
{
    public async Task<TResponse> HandleAsync(...)
    {
        // Check for opt-out header
        if (_httpContextAccessor.HttpContext?.Request.Headers.TryGetValue("X-Narrative-Generation", out var value) == true
            && value == "disabled")
        {
            return await next();
        }

        // Continue with narrative generation
        ...
    }
}
```

**Usage**:
```bash
# Disable narrative generation for this request
curl -H "X-Narrative-Generation: disabled" \
     -X POST /Patient \
     -d @patient.json
```

### CapabilityStatement Advertisement

Update CapabilityStatement to advertise narrative generation support:

```csharp
// File: src/Ignixa.Api/Features/Metadata/CapabilityStatementBuilder.cs
public class CapabilityStatementBuilder
{
    public CapabilityStatementJsonNode Build()
    {
        var statement = new CapabilityStatementJsonNode
        {
            // ...
            Rest =
            [
                new CapabilityStatementRestJsonNode
                {
                    Resource = BuildResourceSupport()
                }
            ]
        };

        return statement;
    }

    private List<CapabilityStatementResourceJsonNode> BuildResourceSupport()
    {
        return
        [
            new CapabilityStatementResourceJsonNode
            {
                Type = "Patient",
                Interaction =
                [
                    new CapabilityStatementInteractionJsonNode { Code = "create" },
                    new CapabilityStatementInteractionJsonNode { Code = "read" },
                    new CapabilityStatementInteractionJsonNode { Code = "update" }
                ],
                // Advertise narrative generation support
                Extension =
                [
                    new ExtensionJsonNode
                    {
                        Url = "http://ignixa.io/fhir/StructureDefinition/narrative-generation",
                        ValueBoolean = _narrativeOptions.Enabled
                    }
                ]
            }
        ];
    }
}
```

---

## File Structure

**Location**: Core SDK (`/core` repository, public NuGet package)

```
core/
└── src/
    └── Ignixa.NarrativeGenerator/
        ├── Api/
        │   ├── INarrativeGenerator.cs
        │   ├── NarrativeGenerationContext.cs
        │   ├── NarrativeGeneratorOptions.cs
        │   └── TemplateTier.cs
        ├── Engine/
        │   ├── NarrativeGeneratorService.cs
        │   ├── TemplateNarrativeEngine.cs
        │   ├── ToTextNarrativeEngine.cs
        │   ├── TemplateResolver.cs
        │   ├── XhtmlSanitizer.cs
        │   └── NarrativeValidator.cs
        ├── FhirPath/
        │   ├── FhirPathScriptFunctions.cs
        │   ├── FhirPathExpressions.R4.cs        (auto-generated)
        │   ├── FhirPathExpressions.R5.cs        (auto-generated)
        │   └── FhirPathExpressions.STU3.cs      (auto-generated)
        ├── Templates/
        │   ├── Normative/                        # Stable across versions
        │   │   ├── Patient.scriban               (Normative - Rich)
        │   │   ├── Observation.scriban           (Normative - Rich)
        │   │   ├── Condition.scriban             (Normative - Rich)
        │   │   ├── AllergyIntolerance.scriban    (Normative - Rich)
        │   │   ├── Medication.scriban            (Normative - Rich)
        │   │   ├── Encounter.scriban             (Normative - Rich)
        │   │   ├── Procedure.scriban             (Normative - Rich)
        │   │   ├── DiagnosticReport.scriban      (Normative - Rich)
        │   │   └── Generic.scriban               (Fallback for all)
        │   ├── R4/                               # R4-specific trial-use
        │   │   ├── Immunization.scriban          (Trial-use - Medium)
        │   │   ├── MedicationRequest.scriban     (Trial-use - Medium)
        │   │   ├── CarePlan.scriban              (Trial-use - Medium)
        │   │   └── DocumentReference.scriban     (Trial-use - Medium)
        │   ├── R5/                               # R5-specific (breaking changes)
        │   │   ├── Immunization.scriban          (R5 changes from R4)
        │   │   ├── MedicationRequest.scriban     (R5 changes)
        │   │   ├── CarePlan.scriban              (R5 changes)
        │   │   └── Consent.scriban               (R5 major overhaul)
        │   ├── R4B/                              # R4B-specific (rare overrides)
        │   │   └── (only if differs from R4)
        │   └── STU3/                             # STU3 predates normative
        │       ├── Patient.scriban               (STU3-specific)
        │       ├── Observation.scriban           (STU3-specific)
        │       └── Generic.scriban               (STU3 fallback)
        ├── Codegen/
        │   ├── TemplateGenerator.cs             (generates .scriban stubs)
        │   ├── FhirPathExpressionGenerator.cs   (generates expression constants)
        │   └── ViewModel/
        │       └── ViewModelGenerator.cs        (optional: for future use)
        └── Ignixa.NarrativeGenerator.csproj

codegen/fhir-codegen/
├── Commands/
│   ├── GenerateNarrativeTemplatesCommand.cs
│   └── GenerateFhirPathExpressionsCommand.cs
└── Templates/
    └── NarrativeTemplate.scriban.liquid (meta-template!)
```

**Package Output**:
- **NuGet Package**: `Ignixa.NarrativeGenerator` (public, versioned with Core SDK)
- **Embedded Resources**: Templates are embedded in assembly (`.scriban` files)
- **Target Frameworks**: net8.0, net9.0
```

---

## CI/CD Pipeline Updates

### Overview

Since `Ignixa.NarrativeGenerator` is part of the **Core SDK**, all build and release pipelines must be updated to include this new package.

### Build Pipeline Changes

#### 1. Core SDK Build Pipeline

**File**: `.github/workflows/core-build.yml` (or equivalent)

**Required Changes**:

```yaml
jobs:
  build:
    steps:
      # ... existing steps ...

      # Add: Build Ignixa.NarrativeGenerator
      - name: Build Ignixa.NarrativeGenerator
        run: dotnet build core/src/Ignixa.NarrativeGenerator/Ignixa.NarrativeGenerator.csproj --configuration Release

      # Add: Run narrative generator tests
      - name: Test Ignixa.NarrativeGenerator
        run: |
          dotnet test core/test/Ignixa.NarrativeGenerator.Tests/Ignixa.NarrativeGenerator.Tests.csproj
          dotnet test core/test/Ignixa.NarrativeGenerator.SecurityTests/Ignixa.NarrativeGenerator.SecurityTests.csproj
          dotnet test core/test/Ignixa.NarrativeGenerator.AccessibilityTests/Ignixa.NarrativeGenerator.AccessibilityTests.csproj

      # Add: Pack NuGet package
      - name: Pack Ignixa.NarrativeGenerator
        run: dotnet pack core/src/Ignixa.NarrativeGenerator/Ignixa.NarrativeGenerator.csproj --configuration Release --output ./nupkgs

      # Add: Embed template resources
      - name: Embed Scriban Templates
        run: |
          # Templates are embedded as resources during build via .csproj configuration
          # Verify embedded resources exist
          dotnet build-server shutdown
```

#### 2. Template Resource Embedding

**File**: `core/src/Ignixa.NarrativeGenerator/Ignixa.NarrativeGenerator.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
    <PackageId>Ignixa.NarrativeGenerator</PackageId>
    <Description>FHIR narrative generation library with WCAG 2.1 AA compliance</Description>
    <PackageTags>fhir;narrative;accessibility;wcag;healthcare</PackageTags>
  </PropertyGroup>

  <!-- Embed Scriban templates as embedded resources -->
  <ItemGroup>
    <EmbeddedResource Include="Templates\**\*.scriban" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Scriban" Version="5.10.0" />
    <ProjectReference Include="..\Ignixa.FhirPath\Ignixa.FhirPath.csproj" />
    <ProjectReference Include="..\Ignixa.Abstractions\Ignixa.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

### Release Pipeline Changes

#### 1. NuGet Package Publishing

**File**: `.github/workflows/core-release.yml` (or equivalent)

**Required Changes**:

```yaml
jobs:
  release:
    steps:
      # ... existing steps ...

      # Add: Publish Ignixa.NarrativeGenerator to NuGet
      - name: Publish Ignixa.NarrativeGenerator to NuGet
        run: |
          dotnet nuget push ./nupkgs/Ignixa.NarrativeGenerator.*.nupkg \
            --api-key ${{ secrets.NUGET_API_KEY }} \
            --source https://api.nuget.org/v3/index.json

      # Add: Publish to GitHub Packages (optional)
      - name: Publish to GitHub Packages
        run: |
          dotnet nuget push ./nupkgs/Ignixa.NarrativeGenerator.*.nupkg \
            --api-key ${{ secrets.GITHUB_TOKEN }} \
            --source https://nuget.pkg.github.com/ignixa/index.json
```

#### 2. Versioning Strategy

**Approach**: Version `Ignixa.NarrativeGenerator` with the Core SDK version.

```yaml
# Example: Core SDK is v2.5.0 → Ignixa.NarrativeGenerator is also v2.5.0
- name: Set Version
  run: |
    VERSION=$(cat version.txt)  # e.g., 2.5.0
    dotnet pack --configuration Release \
      /p:Version=$VERSION \
      /p:PackageVersion=$VERSION
```

### Pre-release Validation

**File**: `.github/workflows/core-pr-validation.yml`

```yaml
jobs:
  validate-narrative-generator:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      # Validate templates are valid Scriban
      - name: Validate Scriban Templates
        run: |
          # Build project (this compiles templates)
          dotnet build core/src/Ignixa.NarrativeGenerator/Ignixa.NarrativeGenerator.csproj

      # Run security tests (XSS, malicious input)
      - name: Security Tests
        run: dotnet test core/test/Ignixa.NarrativeGenerator.SecurityTests/ --filter Category=Security

      # Run accessibility tests (WCAG compliance)
      - name: Accessibility Tests
        run: dotnet test core/test/Ignixa.NarrativeGenerator.AccessibilityTests/ --filter Category=Accessibility

      # Verify embedded resources
      - name: Verify Embedded Resources
        run: |
          # Check that .scriban templates are embedded
          unzip -l ./nupkgs/Ignixa.NarrativeGenerator.*.nupkg | grep "Templates.*\.scriban"
```

### Template Update Workflow

When templates are updated, they must be regenerated and committed:

```yaml
# .github/workflows/generate-templates.yml
name: Generate Narrative Templates

on:
  workflow_dispatch:
    inputs:
      fhir_version:
        description: 'FHIR version to generate (R4, R5, R4B, STU3)'
        required: true
        default: 'R4'

jobs:
  generate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'

      - name: Generate Templates
        run: |
          cd codegen/fhir-codegen
          dotnet run -- generate-narrative-templates \
            --fhir-version ${{ github.event.inputs.fhir_version }} \
            --output ../../core/src/Ignixa.NarrativeGenerator/Templates/${{ github.event.inputs.fhir_version }}

      - name: Generate FHIRPath Expressions
        run: |
          cd codegen/fhir-codegen
          dotnet run -- generate-fhirpath-expressions \
            --fhir-version ${{ github.event.inputs.fhir_version }} \
            --output ../../core/src/Ignixa.NarrativeGenerator/FhirPath

      - name: Create PR
        uses: peter-evans/create-pull-request@v5
        with:
          title: "feat: Update narrative templates for ${{ github.event.inputs.fhir_version }}"
          body: "Auto-generated narrative templates via fhir-codegen"
          branch: "auto/narrative-templates-${{ github.event.inputs.fhir_version }}"
```

### Quality Gates

Add quality gates to prevent merging PRs with issues:

```yaml
# .github/workflows/core-pr-validation.yml (continued)
jobs:
  quality-gates:
    steps:
      # ... other steps ...

      - name: Check Template Coverage
        run: |
          # Ensure all normative resources have templates
          NORMATIVE_RESOURCES="Patient Observation Condition AllergyIntolerance Medication"
          for resource in $NORMATIVE_RESOURCES; do
            if [ ! -f "core/src/Ignixa.NarrativeGenerator/Templates/R4/$resource.scriban" ]; then
              echo "ERROR: Missing template for $resource"
              exit 1
            fi
          done

      - name: Check Security Compliance
        run: |
          # Verify no html.raw usage in templates
          if grep -r "html\.raw" core/src/Ignixa.NarrativeGenerator/Templates/; then
            echo "ERROR: Found html.raw in templates (XSS risk)"
            exit 1
          fi

      - name: Check WCAG Compliance
        run: |
          # Verify all templates have lang attribute
          for template in core/src/Ignixa.NarrativeGenerator/Templates/**/*.scriban; do
            if ! grep -q 'lang=' "$template"; then
              echo "WARNING: $template missing lang attribute (WCAG 3.1.1)"
            fi
          done
```

### Integration with Ignixa Server Pipeline

**File**: `src/Ignixa.Api/Ignixa.Api.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <!-- ... existing config ... -->

  <ItemGroup>
    <!-- Add NuGet reference to Ignixa.NarrativeGenerator -->
    <PackageReference Include="Ignixa.NarrativeGenerator" Version="$(CoreSdkVersion)" />
  </ItemGroup>
</Project>
```

**Pipeline**: Ignixa server build will automatically pull the latest `Ignixa.NarrativeGenerator` NuGet package during restore.

### Package Dependencies

Ensure Core SDK packages are published in correct order:

```
1. Ignixa.Abstractions        (base types)
    ↓
2. Ignixa.FhirPath            (FHIRPath compiler)
    ↓
3. Ignixa.NarrativeGenerator  (depends on both)
```

**Pipeline Order**:
```yaml
jobs:
  build-and-publish:
    steps:
      - name: Publish Abstractions
        run: dotnet nuget push Ignixa.Abstractions.*.nupkg

      - name: Publish FhirPath
        run: dotnet nuget push Ignixa.FhirPath.*.nupkg

      - name: Publish NarrativeGenerator
        run: dotnet nuget push Ignixa.NarrativeGenerator.*.nupkg
```

### Documentation Updates

**Required Documentation**:
- [ ] Update Core SDK README with `Ignixa.NarrativeGenerator` package
- [ ] Add migration guide for consumers
- [ ] Document CSP requirements for narrative rendering
- [ ] Add examples to Core SDK samples project

---

## fhir-codegen Implementation

### Template Generator Command

```csharp
// File: codegen/fhir-codegen/Commands/GenerateNarrativeTemplatesCommand.cs
public class GenerateNarrativeTemplatesCommand
{
    public async Task<int> ExecuteAsync(
        FhirVersion fhirVersion,
        string outputDirectory,
        TemplateTier tier,
        string[] resourceTypes)
    {
        var definitions = await LoadStructureDefinitionsAsync(fhirVersion);

        foreach (var resourceType in resourceTypes)
        {
            var definition = definitions.FirstOrDefault(d => d.Name == resourceType);
            if (definition is null)
            {
                Console.WriteLine($"Warning: {resourceType} not found");
                continue;
            }

            var template = GenerateTemplate(definition, tier);
            var outputPath = Path.Combine(outputDirectory, $"{resourceType}.scriban");

            await File.WriteAllTextAsync(outputPath, template);
            Console.WriteLine($"Generated: {outputPath}");
        }

        return 0;
    }

    private string GenerateTemplate(StructureDefinition definition, TemplateTier tier)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"<!-- Auto-generated narrative template -->");
        builder.AppendLine($"<!-- Resource: {definition.Name} -->");
        builder.AppendLine($"<!-- Tier: {tier} -->");
        builder.AppendLine();
        builder.AppendLine($"<div class=\"fhir-{definition.Name.ToLowerInvariant()}\">");

        // Extract key elements from StructureDefinition
        var keyElements = GetKeyElements(definition);

        if (tier == TemplateTier.Rich)
        {
            builder.AppendLine($"  <h3>{definition.Name}</h3>");
            builder.AppendLine("  <dl>");

            foreach (var element in keyElements)
            {
                var fhirPath = element.Path.Replace($"{definition.Name}.", "");
                builder.AppendLine($"    <dt>{element.Short}</dt>");
                builder.AppendLine($"    <dd>{{{{ fhirpath resource \"{fhirPath}\" }}}}</dd>");
                builder.AppendLine();
            }

            builder.AppendLine("  </dl>");
        }
        else if (tier == TemplateTier.Medium)
        {
            builder.AppendLine("  <table>");

            foreach (var element in keyElements)
            {
                var fhirPath = element.Path.Replace($"{definition.Name}.", "");
                builder.AppendLine("    <tr>");
                builder.AppendLine($"      <th>{element.Short}</th>");
                builder.AppendLine($"      <td>{{{{ fhirpath resource \"{fhirPath}\" }}}}</td>");
                builder.AppendLine("    </tr>");
            }

            builder.AppendLine("  </table>");
        }

        builder.AppendLine("</div>");
        return builder.ToString();
    }

    private IReadOnlyList<ElementDefinition> GetKeyElements(StructureDefinition definition)
    {
        // Extract commonly displayed elements (cardinality 1..1, 1..*, or must-support)
        return definition.Snapshot.Element
            .Where(e => e.MustSupport ||
                       e.Min >= 1 ||
                       IsCommonDisplayElement(e.Path))
            .Take(10) // Limit to top 10 for initial template
            .ToList();
    }

    private bool IsCommonDisplayElement(string path)
    {
        var commonPatterns = new[]
        {
            "name", "title", "code", "status", "subject", "date",
            "effective", "value", "description", "category"
        };

        return commonPatterns.Any(p => path.EndsWith($".{p}", StringComparison.OrdinalIgnoreCase));
    }
}
```

### FHIRPath Expression Generator

```csharp
// File: codegen/fhir-codegen/Commands/GenerateFhirPathExpressionsCommand.cs
public class GenerateFhirPathExpressionsCommand
{
    public async Task<int> ExecuteAsync(FhirVersion fhirVersion, string outputDirectory)
    {
        var definitions = await LoadStructureDefinitionsAsync(fhirVersion);
        var builder = new StringBuilder();

        builder.AppendLine("// Auto-generated FHIRPath expression constants");
        builder.AppendLine($"// FHIR Version: {fhirVersion}");
        builder.AppendLine();
        builder.AppendLine("namespace Ignixa.NarrativeGenerator.FhirPath;");
        builder.AppendLine();
        builder.AppendLine($"public static class FhirPathExpressions");
        builder.AppendLine("{");

        foreach (var definition in definitions.Where(d => d.Kind == "resource"))
        {
            builder.AppendLine($"    public static class {definition.Name}");
            builder.AppendLine("    {");

            // Generate common patterns
            var expressions = GenerateCommonExpressions(definition);
            foreach (var (name, expression) in expressions)
            {
                builder.AppendLine($"        public const string {name} = \"{expression}\";");
            }

            builder.AppendLine("    }");
            builder.AppendLine();
        }

        builder.AppendLine("}");

        var outputPath = Path.Combine(outputDirectory, $"FhirPathExpressions.{fhirVersion}.cs");
        await File.WriteAllTextAsync(outputPath, builder.ToString());

        Console.WriteLine($"Generated: {outputPath}");
        return 0;
    }

    private IEnumerable<(string Name, string Expression)> GenerateCommonExpressions(StructureDefinition definition)
    {
        var expressions = new List<(string, string)>();

        // Resource-specific patterns
        if (definition.Name == "Patient")
        {
            expressions.Add(("FullName", "name.given.first() + ' ' + name.family"));
            expressions.Add(("BirthDate", "birthDate"));
            expressions.Add(("Gender", "gender"));
            expressions.Add(("PrimaryPhone", "telecom.where(system='phone').value.first()"));
        }
        else if (definition.Name == "Observation")
        {
            expressions.Add(("Code", "code.coding.display.first() | code.text"));
            expressions.Add(("Value", "value.toString()"));
            expressions.Add(("ValueQuantity", "value.ofType(Quantity).value + ' ' + value.ofType(Quantity).unit"));
            expressions.Add(("EffectiveDateTime", "effectiveDateTime | effectivePeriod.start"));
        }
        else if (definition.Name == "Condition")
        {
            expressions.Add(("Code", "code.coding.display.first() | code.text"));
            expressions.Add(("ClinicalStatus", "clinicalStatus.coding.code.first()"));
            expressions.Add(("OnsetDateTime", "onsetDateTime | onsetPeriod.start"));
        }

        // Generic patterns for CodeableConcept-based resources
        if (definition.Snapshot.Element.Any(e => e.Path.EndsWith(".code") && e.Type.Any(t => t.Code == "CodeableConcept")))
        {
            expressions.Add(("CodeDisplay", "code.coding.display.first() | code.text"));
        }

        return expressions;
    }
}
```

---

## Implementation Phases

### Phase 1: Core Infrastructure (25 hours)

- [ ] Create project structure
- [ ] Implement `INarrativeGenerator` interface
- [ ] Implement `NarrativeGeneratorService`
- [ ] Implement `TemplateResolver` with override mechanism
- [ ] Implement `FhirPathScriptFunctions` for Scriban
- [ ] Unit tests for core service

### Phase 2: Template Engine with Security (25 hours)

- [ ] Implement `TemplateNarrativeEngine` with Scriban
- [ ] Implement `ToTextNarrativeEngine` fallback
- [ ] Template caching mechanism
- [ ] Multi-version support (version detection)
- [ ] **Security**: Implement `XhtmlSanitizer` with FHIR-compliant element/attribute filtering
- [ ] **Security**: Implement `NarrativeValidator` for XHTML schema validation
- [ ] **Security**: Configure Scriban auto-escaping (no `html.raw` usage)
- [ ] Unit tests for engines
- [ ] Security tests (XSS, malicious input sanitization)

### Phase 3: Built-In Templates - Normative + Version-Specific (30 hours)

- [ ] **Normative templates** (Tier 1 - Rich, WCAG 2.1 AA compliant):
  - [ ] Normative/Patient.scriban (semantic `<dl>`, proper heading hierarchy, `lang` attribute)
  - [ ] Normative/Observation.scriban (table with `<th>`, alt text for images)
  - [ ] Normative/Condition.scriban
  - [ ] Normative/AllergyIntolerance.scriban
  - [ ] Normative/Medication.scriban
  - [ ] Normative/Encounter.scriban
  - [ ] Normative/Procedure.scriban
  - [ ] Normative/DiagnosticReport.scriban
  - [ ] Normative/Generic.scriban (fallback for all versions)

- [ ] **R4 trial-use templates** (Tier 2 - Medium):
  - [ ] R4/Immunization.scriban
  - [ ] R4/MedicationRequest.scriban
  - [ ] R4/CarePlan.scriban
  - [ ] R4/DocumentReference.scriban

- [ ] **Template resolution logic**:
  - [ ] Implement fallback chain: version-specific → Normative → Generic
  - [ ] Version detection from resource

- [ ] **Accessibility testing**:
  - [ ] Heading hierarchy tests (no skipped levels)
  - [ ] Table header tests (all tables have `<th>`)
  - [ ] Lang attribute tests
  - [ ] Semantic HTML validation

### Phase 4: fhir-codegen Integration (20 hours)

- [ ] Implement `GenerateNarrativeTemplatesCommand`
  - [ ] Support `--normative` flag to generate in Normative/ folder
  - [ ] Support `--version` flag for version-specific templates
- [ ] Implement `GenerateFhirPathExpressionsCommand`
- [ ] Generate `FhirPathExpressions.R4.cs`
- [ ] Update templates to use generated expressions
- [ ] Documentation for template generation

### Phase 5: Multi-Version Support (12 hours)

- [ ] **R5 trial-use templates** (only where breaking changes exist):
  - [ ] R5/Immunization.scriban (R5 has breaking changes from R4)
  - [ ] R5/MedicationRequest.scriban
  - [ ] R5/CarePlan.scriban
  - [ ] R5/Consent.scriban (major R5 overhaul)

- [ ] **STU3 templates** (predates normative):
  - [ ] STU3/Patient.scriban
  - [ ] STU3/Observation.scriban
  - [ ] STU3/Generic.scriban

- [ ] **Generate FHIRPath expressions**:
  - [ ] Generate `FhirPathExpressions.R5.cs`
  - [ ] Generate `FhirPathExpressions.STU3.cs`

- [ ] **Testing**:
  - [ ] Version detection tests
  - [ ] Template resolution tests (normative fallback)
  - [ ] Cross-version template compatibility tests

### Phase 6: Server Integration (Medino Behavior) (15 hours)

- [ ] Implement `NarrativeGenerationBehavior`
- [ ] Implement `NarrativeGenerationOptions` with configuration
- [ ] Add `IResourceResponse` and `IBundleResponse` marker interfaces
- [ ] Register behavior in Ignixa pipeline
- [ ] Configuration support (appsettings.json)
- [ ] HTTP header opt-out mechanism (`X-Narrative-Generation`)
- [ ] CapabilityStatement advertisement
- [ ] Integration tests (create/update/patch with narrative generation)

### Phase 7: Integration & Documentation (10 hours)

- [ ] Integration tests with real FHIR resources
- [ ] Template override examples
- [ ] API documentation
- [ ] Migration guide for custom templates
- [ ] Performance benchmarks
- [ ] Update ADR-2601 (IPS Generator) to use library

### Phase 8: CI/CD Pipeline Integration (8 hours)

- [ ] **Core SDK Build Pipeline**:
  - [ ] Add build step for `Ignixa.NarrativeGenerator`
  - [ ] Add test execution (unit, security, accessibility tests)
  - [ ] Add NuGet pack step
  - [ ] Configure embedded resource verification

- [ ] **Core SDK Release Pipeline**:
  - [ ] Add NuGet publish step for `Ignixa.NarrativeGenerator`
  - [ ] Configure versioning (sync with Core SDK version)
  - [ ] Add GitHub Packages publish (optional)

- [ ] **PR Validation Pipeline**:
  - [ ] Add template validation (Scriban syntax check)
  - [ ] Add security compliance checks (no `html.raw` in templates)
  - [ ] Add WCAG compliance checks (`lang` attributes)
  - [ ] Add template coverage checks (normative resources)
  - [ ] Add embedded resource verification

- [ ] **Template Generation Workflow**:
  - [ ] Create `generate-templates.yml` workflow
  - [ ] Support workflow_dispatch with FHIR version input
  - [ ] Auto-create PR with generated templates

- [ ] **Update Ignixa Server Pipeline**:
  - [ ] Add `Ignixa.NarrativeGenerator` NuGet reference to `Ignixa.Api.csproj`
  - [ ] Verify package restore pulls correct version

- [ ] **Documentation**:
  - [ ] Update Core SDK README with new package
  - [ ] Document package dependency order
  - [ ] Document CSP requirements

**Total Estimate**: 145 hours (4 weeks)

**Breakdown**:
- Core Infrastructure: 25h
- Template Engine + Security: 25h
- Templates + WCAG Compliance (Normative + Version-Specific): 30h ⬇️ (reduced via Normative folder)
- fhir-codegen: 20h
- Multi-Version: 12h ⬇️ (reduced - only trial-use resources need version-specific templates)
- Server Integration: 15h
- Documentation: 10h
- **CI/CD Pipeline: 8h**

**Note**: Internationalization (i18n) support via .NET resource files (.resx) is **recommended** but not included in initial estimate. See "Open Questions" section for localization strategy.

---

## Performance Targets

| Metric | Target | Rationale |
|--------|--------|-----------|
| **Template compilation** | <10ms | One-time cost, cached |
| **Single resource narrative** | <50ms | For IPS section generation (100ms budget) |
| **Batch (10 resources)** | <200ms | Parallel generation |
| **Memory overhead** | <5MB | Template cache + FHIRPath compiler |
| **Cache hit ratio** | >95% | Compiled templates reused |

---

## Extension Points

### Custom Template Registration

```csharp
services.AddNarrativeGenerator(options =>
{
    // Directory-based override
    options.CustomTemplateDirectory = "/app/templates";

    // Or programmatic registration
    options.RegisterTemplate("Patient", FhirVersion.R4, myCustomTemplate);

    // Or inline template
    options.RegisterTemplateFromString("Patient", FhirVersion.R4,
        "<div>{{ fhirpath resource \"name.family\" }}</div>");
});
```

### Custom FHIRPath Functions

```csharp
services.AddNarrativeGenerator(options =>
{
    options.RegisterFhirPathFunction("my_formatter", (resource, arg) =>
    {
        // Custom formatting logic
        return $"Formatted: {arg}";
    });
});
```

Usage in template:
```html
<div>{{ my_formatter resource "some value" }}</div>
```

### Custom ToText Provider

```csharp
public class CustomToTextProvider : IToTextProvider
{
    public string GenerateToText(ResourceJsonNode resource)
    {
        // Custom fallback logic
        return $"Custom text for {resource.ResourceType}";
    }
}

services.AddNarrativeGenerator(options =>
{
    options.ToTextProvider = new CustomToTextProvider();
});
```

---

## Testing Strategy

### Unit Tests

```csharp
public class NarrativeGeneratorTests
{
    [Theory]
    [InlineData("Patient", FhirVersion.R4, TemplateTier.Rich)]
    [InlineData("Observation", FhirVersion.R4, TemplateTier.Rich)]
    [InlineData("MedicationRequest", FhirVersion.R4, TemplateTier.Medium)]
    [InlineData("RiskAssessment", FhirVersion.R4, TemplateTier.Basic)]
    public void GetTemplateTier_ReturnsExpectedTier(string resourceType, FhirVersion version, TemplateTier expected)
    {
        var generator = CreateGenerator();
        var actual = generator.GetTemplateTier(resourceType, version);
        actual.Should().Be(expected);
    }

    [Fact]
    public async Task GenerateNarrative_Patient_ContainsName()
    {
        var patient = new PatientJsonNode
        {
            Name = [new HumanNameJsonNode { Given = ["John"], Family = "Doe" }]
        };

        var narrative = await _generator.GenerateNarrativeAsync(patient);

        narrative.Should().Contain("John Doe");
    }

    [Fact]
    public async Task GenerateNarrative_CustomTemplate_UsesCustom()
    {
        var options = new NarrativeGeneratorOptions
        {
            CustomTemplateDirectory = "/custom",
            Strategy = TemplateResolutionStrategy.CustomOnly
        };

        var generator = CreateGenerator(options);
        var patient = CreateTestPatient();

        var narrative = await generator.GenerateNarrativeAsync(patient);

        narrative.Should().Contain("CUSTOM");
    }
}
```

### Template Tests

```csharp
public class TemplateTests
{
    [Theory]
    [InlineData("Patient")]
    [InlineData("Observation")]
    public void Template_IsValidScriban(string resourceType)
    {
        var templateText = LoadBuiltInTemplate($"R4/{resourceType}.scriban");

        var template = Template.Parse(templateText);
        template.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task PatientTemplate_WithCompleteData_RendersAllSections()
    {
        var patient = CreateCompletePatient();
        var narrative = await RenderTemplate("Patient", patient);

        narrative.Should().Contain("Birth Date");
        narrative.Should().Contain("Gender");
        narrative.Should().Contain("Contact");
    }
}
```

### Integration Tests

```csharp
public class NarrativeGeneratorIntegrationTests
{
    [Fact]
    public async Task GenerateNarrative_AllBuiltInTemplates_Succeed()
    {
        var generator = CreateGenerator();
        var resourceTypes = new[] { "Patient", "Observation", "Condition", /* ... */ };

        foreach (var resourceType in resourceTypes)
        {
            var resource = CreateSampleResource(resourceType);
            var narrative = await generator.GenerateNarrativeAsync(resource);

            narrative.Should().NotBeNullOrWhiteSpace();
            narrative.Should().Contain($"class=\"fhir-{resourceType.ToLowerInvariant()}\"");
        }
    }
}
```

---

## Accessibility and Security Compliance

### Standards Overview

The narrative generator must comply with:

1. **[WCAG 2.1 Level AA](https://www.w3.org/TR/WCAG21/)** - Microsoft's baseline accessibility standard
2. **[Microsoft Security Development Lifecycle (SDL)](https://www.microsoft.com/en-us/securityengineering/sdl/practices)** - XSS prevention, input sanitization
3. **[FHIR Narrative Restrictions](https://hl7.org/fhir/narrative.html)** - Limited XHTML subset, no scripts

### WCAG 2.1 Level AA Compliance

#### Key Requirements for Generated Narratives

| WCAG Criterion | Requirement | Implementation |
|----------------|-------------|----------------|
| **1.1.1 Non-text Content** | Alt text for images | Templates must include `alt` attributes for any `<img>` elements |
| **1.3.1 Info and Relationships** | Semantic HTML structure | Use proper heading hierarchy (`<h1>-<h6>`), `<dl>`, `<table>` with headers |
| **1.3.2 Meaningful Sequence** | Logical content order | Templates follow DOM order = visual order |
| **1.4.3 Contrast** | 4.5:1 minimum contrast | Use semantic classes only, no hardcoded colors |
| **2.4.4 Link Purpose** | Descriptive link text | Avoid "click here", use resource references |
| **3.1.1 Language** | Specify page language | Add `lang` attribute to narrative div |
| **4.1.1 Parsing** | Valid HTML | Validate generated XHTML against FHIR schema |
| **4.1.2 Name, Role, Value** | ARIA labels where needed | Use `aria-label` for icon-only buttons |

#### Template Compliance Example

```html
<!-- WCAG 2.1 AA Compliant Template -->
<div xmlns="http://www.w3.org/1999/xhtml" lang="en" class="fhir-patient">
  <!-- 1.3.1: Semantic structure with proper heading hierarchy -->
  <h3>Patient Summary</h3>

  <!-- 1.3.1: Use <dl> for key-value pairs (semantic) -->
  <dl>
    <dt>Name</dt>
    <dd>{{ fhirpath resource "name.given.first()" }} {{ fhirpath resource "name.family" }}</dd>

    <dt>Birth Date</dt>
    <dd>{{ format_date (fhirpath resource "birthDate") }}</dd>

    <dt>Gender</dt>
    <dd>{{ fhirpath resource "gender" }}</dd>
  </dl>

  <!-- 1.3.1: Use proper table semantics with <th> -->
  {{ if (fhirpath resource "telecom.exists()") }}
  <h4>Contact Information</h4>
  <table>
    <thead>
      <tr>
        <th scope="col">Type</th>
        <th scope="col">Value</th>
      </tr>
    </thead>
    <tbody>
      {{ for contact in (fhirpath_all resource "telecom") }}
      <tr>
        <td>{{ contact.system }}</td>
        <td>{{ contact.value }}</td>
      </tr>
      {{ end }}
    </tbody>
  </table>
  {{ end }}

  <!-- 1.1.1: If including images, always provide alt text -->
  {{ if photo_url }}
  <img src="{{ photo_url }}" alt="Patient photo" />
  {{ end }}
</div>
```

#### Accessibility Testing

```csharp
// File: src/Ignixa.NarrativeGenerator.Tests/AccessibilityTests.cs
public class AccessibilityTests
{
    [Theory]
    [InlineData("Patient")]
    [InlineData("Observation")]
    public async Task GeneratedNarrative_HasProperHeadingHierarchy(string resourceType)
    {
        var resource = CreateSampleResource(resourceType);
        var narrative = await _generator.GenerateNarrativeAsync(resource);

        // Parse XHTML
        var doc = XDocument.Parse(narrative);

        // Verify heading hierarchy (no skipped levels)
        var headings = doc.Descendants()
            .Where(e => Regex.IsMatch(e.Name.LocalName, "^h[1-6]$"))
            .Select(e => int.Parse(e.Name.LocalName.Substring(1)))
            .ToList();

        for (int i = 1; i < headings.Count; i++)
        {
            var diff = headings[i] - headings[i - 1];
            diff.Should().BeLessOrEqualTo(1, "Heading hierarchy should not skip levels");
        }
    }

    [Theory]
    [InlineData("Patient")]
    public async Task GeneratedNarrative_TablesHaveHeaders(string resourceType)
    {
        var resource = CreateSampleResource(resourceType);
        var narrative = await _generator.GenerateNarrativeAsync(resource);

        var doc = XDocument.Parse(narrative);
        var tables = doc.Descendants().Where(e => e.Name.LocalName == "table");

        foreach (var table in tables)
        {
            var headers = table.Descendants().Where(e => e.Name.LocalName == "th");
            headers.Should().NotBeEmpty("Tables must have <th> elements for accessibility");
        }
    }

    [Fact]
    public async Task GeneratedNarrative_HasLangAttribute()
    {
        var patient = CreateSamplePatient();
        var narrative = await _generator.GenerateNarrativeAsync(patient);

        var doc = XDocument.Parse(narrative);
        var rootDiv = doc.Root!;

        rootDiv.Attribute("lang").Should().NotBeNull("Root div must have lang attribute");
    }
}
```

### Security Compliance

#### FHIR Narrative Restrictions

Per the [FHIR specification](https://hl7.org/fhir/narrative.html), narratives are **restricted to a limited XHTML subset**:

**Allowed Elements**:
- Structural: `div`, `p`, `span`, `br`, `hr`
- Headings: `h1`, `h2`, `h3`, `h4`, `h5`, `h6`
- Lists: `ul`, `ol`, `li`, `dl`, `dt`, `dd`
- Tables: `table`, `thead`, `tbody`, `tfoot`, `tr`, `th`, `td`, `caption`
- Text formatting: `b`, `i`, `u`, `strong`, `em`, `mark`, `small`, `del`, `ins`, `sub`, `sup`
- Links/media: `a` (name/href only), `img`

**Prohibited Elements** (XSS attack vectors):
- ❌ `script`, `object`, `embed`, `iframe`, `frame`
- ❌ `form`, `input`, `button`, `select`, `textarea`
- ❌ `base`, `link`, `meta`, `style` (external stylesheets)
- ❌ Event attributes (`onclick`, `onerror`, `onload`, etc.)

**Allowed Attributes**:
- `class`, `id`, `style` (inline CSS only)
- `href`, `name` (for `<a>`)
- `src`, `alt`, `width`, `height` (for `<img>`)
- `colspan`, `rowspan` (for tables)

#### Microsoft SDL XSS Prevention

Following [Microsoft's XSS prevention guidance](https://learn.microsoft.com/en-us/aspnet/core/security/cross-site-scripting):

1. **Input Validation** - FHIRPath expressions are pre-compiled, not user input
2. **Output Encoding** - All resource data is encoded before insertion
3. **HTML Sanitization** - Template output is validated against FHIR XHTML schema
4. **Content Security Policy** - Recommend CSP headers for narrative rendering

#### XSS Attack Surface Analysis

| Attack Vector | Risk | Mitigation |
|---------------|------|------------|
| **Malicious template injection** | ❌ BLOCKED | Templates are loaded from trusted sources (assembly resources or admin-controlled directory) |
| **Resource data injection** | ⚠️ MEDIUM | Resource data could contain malicious HTML/JS |
| **FHIRPath expression injection** | ❌ BLOCKED | Expressions are pre-compiled at build time, not runtime |
| **Template variable injection** | ⚠️ MEDIUM | Scriban auto-escapes by default |

#### Critical: Resource Data Sanitization

**PROBLEM**: A malicious client could create a Patient with:
```json
{
  "resourceType": "Patient",
  "name": [{
    "family": "<script>alert('XSS')</script>Smith"
  }]
}
```

**SOLUTION**: Sanitize all resource data before template rendering:

```csharp
// File: src/Ignixa.NarrativeGenerator/Engine/XhtmlSanitizer.cs
public class XhtmlSanitizer
{
    private static readonly HashSet<string> AllowedElements = new()
    {
        "div", "p", "span", "br", "hr",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li", "dl", "dt", "dd",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption",
        "b", "i", "u", "strong", "em", "mark", "small", "del", "ins", "sub", "sup",
        "a", "img"
    };

    private static readonly HashSet<string> AllowedAttributes = new()
    {
        "class", "id", "style", "href", "name", "src", "alt", "width", "height",
        "colspan", "rowspan", "lang"
    };

    private static readonly Regex DisallowedPatterns = new(
        @"javascript:|data:|vbscript:|on\w+\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes generated narrative XHTML to comply with FHIR restrictions.
    /// </summary>
    public string Sanitize(string xhtml)
    {
        var doc = XDocument.Parse(xhtml);
        SanitizeNode(doc.Root!);
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private void SanitizeNode(XElement element)
    {
        // Remove prohibited elements
        if (!AllowedElements.Contains(element.Name.LocalName.ToLowerInvariant()))
        {
            element.Remove();
            return;
        }

        // Sanitize attributes
        var attributesToRemove = new List<XAttribute>();
        foreach (var attr in element.Attributes())
        {
            var attrName = attr.Name.LocalName.ToLowerInvariant();

            // Remove prohibited attributes
            if (!AllowedAttributes.Contains(attrName))
            {
                attributesToRemove.Add(attr);
                continue;
            }

            // Check for malicious patterns (javascript:, data:, event handlers)
            if (DisallowedPatterns.IsMatch(attr.Value))
            {
                attributesToRemove.Add(attr);
                continue;
            }

            // Sanitize href/src attributes
            if (attrName is "href" or "src")
            {
                if (!IsValidUrl(attr.Value))
                {
                    attributesToRemove.Add(attr);
                }
            }
        }

        foreach (var attr in attributesToRemove)
        {
            attr.Remove();
        }

        // Recursively sanitize child elements
        foreach (var child in element.Elements().ToList())
        {
            SanitizeNode(child);
        }
    }

    private bool IsValidUrl(string url)
    {
        // Allow relative URLs and safe protocols
        if (url.StartsWith("/") || url.StartsWith("#"))
        {
            return true;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" or "ftp";
        }

        return false;
    }
}
```

#### Scriban Auto-Escaping

Scriban provides **automatic HTML encoding** by default:

```html
<!-- Scriban auto-escapes by default -->
<div>{{ resource.name }}</div>

<!-- If resource.name = "<script>alert('XSS')</script>" -->
<!-- Output: <div>&lt;script&gt;alert('XSS')&lt;/script&gt;</div> -->

<!-- To output raw HTML (dangerous!), must explicitly use 'html.raw' -->
<div>{{ html.raw resource.name }}</div>  <!-- DON'T DO THIS -->
```

**IMPORTANT**: Never use `html.raw` in templates for user-controlled data.

#### Validation Pipeline

```csharp
// File: src/Ignixa.NarrativeGenerator/Engine/TemplateNarrativeEngine.cs
public class TemplateNarrativeEngine
{
    private readonly XhtmlSanitizer _sanitizer;
    private readonly NarrativeValidator _validator;

    public async Task<string> GenerateNarrativeAsync(
        ResourceJsonNode resource,
        CancellationToken cancellationToken)
    {
        // 1. Render template (Scriban auto-escapes)
        var rendered = await RenderTemplateAsync(resource, cancellationToken);

        // 2. Sanitize XHTML (defense in depth)
        var sanitized = _sanitizer.Sanitize(rendered);

        // 3. Validate against FHIR narrative schema (optional)
        if (_options.ValidateOutput)
        {
            var validation = _validator.Validate(sanitized);
            if (!validation.IsValid)
            {
                _logger.LogWarning(
                    "Generated narrative failed validation: {Errors}",
                    string.Join(", ", validation.Errors));

                if (_options.FailOnInvalidOutput)
                {
                    throw new InvalidNarrativeException(validation.Errors);
                }
            }
        }

        return sanitized;
    }
}
```

### Potential Conflicts and Resolutions

#### Conflict 1: WCAG Requires `<label>` for Form Controls

**WCAG 2.1**: Form inputs must have associated `<label>` elements.

**FHIR**: Prohibits `<form>`, `<input>`, `<label>` elements in narratives.

**Resolution**: ✅ **No conflict** - Narratives are read-only displays, not interactive forms. Any form elements would violate FHIR spec.

#### Conflict 2: WCAG Allows `<button>` for Interactive Elements

**WCAG 2.1**: Interactive elements should use semantic HTML like `<button>`.

**FHIR**: Prohibits `<button>` elements.

**Resolution**: ✅ **No conflict** - Narratives are not interactive. Use `<a>` for links to related resources (allowed).

#### Conflict 3: WCAG Recommends External Stylesheets for Consistency

**WCAG 2.1**: External stylesheets provide consistent styling and easier maintenance.

**FHIR**: Prohibits external stylesheets (`<link rel="stylesheet">`).

**Resolution**: ⚠️ **Minor conflict** - FHIR allows inline `style` attributes and `class` attributes.

**Mitigation**:
- Use semantic `class` names (`.fhir-patient`, `.fhir-observation`)
- Consumers can apply external stylesheets based on class names
- Avoid inline `style` attributes (accessibility anti-pattern)

```html
<!-- GOOD: Semantic classes -->
<div class="fhir-patient">
  <dl class="patient-demographics">
    <dt class="label">Name</dt>
    <dd class="value">John Doe</dd>
  </dl>
</div>

<!-- BAD: Inline styles (accessibility nightmare) -->
<div style="background: #fff; color: #000;">
  <dl style="margin: 10px;">
    <dt style="font-weight: bold;">Name</dt>
    <dd>John Doe</dd>
  </dl>
</div>
```

#### Conflict 4: Content Security Policy (CSP)

**Microsoft SDL**: Recommends strict CSP headers to prevent XSS.

**FHIR**: Inline `style` attributes allowed.

**Resolution**: ⚠️ **Configurable**

**Recommended CSP for FHIR Narratives**:
```http
Content-Security-Policy: default-src 'none';
                         style-src 'unsafe-inline';
                         img-src https: data:;
```

- `default-src 'none'` - Block all by default
- `style-src 'unsafe-inline'` - Allow inline styles (FHIR requirement)
- `img-src https: data:` - Allow images from HTTPS and data URIs

### Configuration

```csharp
// File: src/Ignixa.NarrativeGenerator/NarrativeGeneratorOptions.cs
public class NarrativeGeneratorOptions
{
    /// <summary>
    /// Validate generated narratives against FHIR XHTML schema.
    /// </summary>
    public bool ValidateOutput { get; set; } = true;

    /// <summary>
    /// Fail narrative generation if output validation fails.
    /// </summary>
    public bool FailOnInvalidOutput { get; set; } = false;

    /// <summary>
    /// Sanitize generated XHTML (defense in depth).
    /// Even with Scriban auto-escaping, this provides an additional safety layer.
    /// </summary>
    public bool SanitizeOutput { get; set; } = true;

    /// <summary>
    /// Include lang attribute in narrative div for WCAG 3.1.1 compliance.
    /// </summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>
    /// Use semantic HTML structure for WCAG 1.3.1 compliance.
    /// </summary>
    public bool UseSemanticHtml { get; set; } = true;
}
```

### Compliance Testing

```csharp
// File: src/Ignixa.NarrativeGenerator.Tests/SecurityTests.cs
public class SecurityTests
{
    [Theory]
    [InlineData("<script>alert('XSS')</script>")]
    [InlineData("javascript:alert('XSS')")]
    [InlineData("<img src='x' onerror='alert(1)'>")]
    [InlineData("<a href='javascript:void(0)'>Click</a>")]
    public async Task GenerateNarrative_MaliciousInput_IsSanitized(string maliciousValue)
    {
        var patient = new PatientJsonNode
        {
            Name = [new HumanNameJsonNode { Family = maliciousValue }]
        };

        var narrative = await _generator.GenerateNarrativeAsync(patient);

        // Should not contain malicious patterns
        narrative.Should().NotContain("<script");
        narrative.Should().NotContain("javascript:");
        narrative.Should().NotContain("onerror=");

        // Should be properly escaped
        if (maliciousValue.Contains("<script>"))
        {
            narrative.Should().Contain("&lt;script&gt;");
        }
    }

    [Fact]
    public async Task GenerateNarrative_ValidatesAgainstFhirSchema()
    {
        var patient = CreateCompletePatient();
        var narrative = await _generator.GenerateNarrativeAsync(patient);

        var validator = new NarrativeValidator();
        var result = validator.Validate(narrative);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task GeneratedNarrative_OnlyContainsAllowedElements()
    {
        var patient = CreateCompletePatient();
        var narrative = await _generator.GenerateNarrativeAsync(patient);

        var doc = XDocument.Parse(narrative);
        var elements = doc.Descendants().Select(e => e.Name.LocalName.ToLowerInvariant());

        var allowedElements = new[]
        {
            "div", "p", "span", "br", "h1", "h2", "h3", "h4", "h5", "h6",
            "ul", "ol", "li", "dl", "dt", "dd", "table", "tr", "th", "td",
            "b", "i", "strong", "em"
        };

        elements.Should().AllSatisfy(e =>
            allowedElements.Should().Contain(e, $"Element '{e}' is not allowed in FHIR narratives")
        );
    }
}
```

### Compliance Summary

| Standard | Status | Notes |
|----------|--------|-------|
| **WCAG 2.1 Level A** | ✅ **Compliant** | Semantic HTML, proper heading hierarchy |
| **WCAG 2.1 Level AA** | ✅ **Compliant** | Contrast via classes, alt text for images |
| **FHIR Narrative Restrictions** | ✅ **Compliant** | Limited XHTML subset, no scripts/forms |
| **Microsoft SDL** | ✅ **Compliant** | XSS prevention via sanitization + auto-escaping |
| **OWASP Top 10 (XSS)** | ✅ **Mitigated** | Defense in depth: validation, escaping, sanitization |
| **CSP Compatibility** | ⚠️ **Partial** | Requires `style-src 'unsafe-inline'` for FHIR inline styles |

### Recommendations

1. ✅ **Enable output validation by default** - Catch malformed XHTML early
2. ✅ **Enable sanitization by default** - Defense in depth
3. ✅ **Use semantic HTML** - WCAG 1.3.1 compliance + better accessibility
4. ✅ **Avoid inline styles** - Use CSS classes instead
5. ✅ **Include `lang` attribute** - WCAG 3.1.1 compliance
6. ⚠️ **Document CSP requirements** - Consuming apps need `style-src 'unsafe-inline'`
7. ✅ **Regular security audits** - Review templates for new XSS vectors

---

## Open Questions

1. **Localization Support**: Should templates support i18n for multi-language narratives?
   - **Recommendation**: **Yes - use .NET Resource Files (.resx)** for Phase 2 feature

   **Proposed Implementation**:

   **Resource Files** (`Resources/NarrativeStrings.resx`, `NarrativeStrings.es.resx`, etc.):
   ```xml
   <!-- NarrativeStrings.resx (English - default) -->
   <data name="Patient.BirthDate" xml:space="preserve">
     <value>Birth Date</value>
   </data>
   <data name="Patient.Gender" xml:space="preserve">
     <value>Gender</value>
   </data>
   <data name="Patient.Contact" xml:space="preserve">
     <value>Contact</value>
   </data>

   <!-- NarrativeStrings.es.resx (Spanish) -->
   <data name="Patient.BirthDate" xml:space="preserve">
     <value>Fecha de Nacimiento</value>
   </data>
   <data name="Patient.Gender" xml:space="preserve">
     <value>Género</value>
   </data>
   <data name="Patient.Contact" xml:space="preserve">
     <value>Contacto</value>
   </data>
   ```

   **Scriban Helper Function**:
   ```csharp
   public class LocalizationScriptFunctions
   {
       private readonly IStringLocalizer<NarrativeStrings> _localizer;

       [ScriptMemberIgnore]
       public string T(string key)
       {
           return _localizer[key];
       }
   }
   ```

   **Template Usage** (clean and translatable):
   ```html
   <div class="fhir-patient" lang="{{ lang }}">
     <h3>{{ fhirpath resource "name.given.first()" }} {{ fhirpath resource "name.family" }}</h3>
     <dl>
       <dt>{{ t "Patient.BirthDate" }}</dt>
       <dd>{{ format_date (fhirpath resource "birthDate") }}</dd>

       <dt>{{ t "Patient.Gender" }}</dt>
       <dd>{{ fhirpath resource "gender" }}</dd>

       {{ if (fhirpath resource "telecom.exists()") }}
       <dt>{{ t "Patient.Contact" }}</dt>
       <dd>
         <ul>
           {{ for contact in (fhirpath_all resource "telecom") }}
           <li>{{ contact.system }}: {{ contact.value }}</li>
           {{ end }}
         </ul>
       </dd>
       {{ end }}
     </dl>
   </div>
   ```

   **Language Selection**:
   ```csharp
   // From HTTP Accept-Language header
   var narrative = await _generator.GenerateNarrativeAsync(
       patient,
       context: new NarrativeGenerationContext
       {
           Culture = new CultureInfo("es-ES") // Spanish
       });
   ```

   **Benefits**:
   - ✅ **Standard .NET approach** - Use existing `IStringLocalizer` infrastructure
   - ✅ **Easy for translators** - .resx files can be edited with standard tools
   - ✅ **Clean templates** - `{{ t "key" }}` is readable and maintainable
   - ✅ **Automatic fallback** - Missing translations fall back to default language
   - ✅ **WCAG 3.1.2 compliance** - Supports "Language of Parts" for multilingual content

   **Effort Estimate**: +15 hours (Phase 2 feature, not in initial MVP)

2. **Narrative Status**: Should we auto-populate `Narrative.status` (generated/extensions/additional)?
   - **Recommendation**: Yes - default to "generated" for templated, "additional" for custom

3. **CSS Styling**: Should we embed CSS classes or provide separate stylesheet?
   - **Recommendation**: Use semantic classes (`.fhir-patient`, `.fhir-observation`), no inline styles

4. **Validation**: Should we validate generated XHTML against FHIR narrative requirements?
   - **Recommendation**: Yes - optional validation mode using XSD/schema validation

5. **Async Template Rendering**: Do we need async template compilation?
   - **Recommendation**: No - compile synchronously at startup, render async for I/O operations

---

## Success Criteria

### Functional

- [ ] Generates valid FHIR narratives for all normative resources (R4/R5)
- [ ] Performance: <50ms per resource narrative generation
- [ ] Custom template override works with directory-based and programmatic approaches
- [ ] fhir-codegen can scaffold new templates in <5 minutes
- [ ] IPS Generator (ADR-2601) successfully integrates library
- [ ] Medino behavior automatically generates narratives for create/update/patch operations
- [ ] Configuration allows fine-grained control (resource types, operations, enabled/disabled)
- [ ] Narrative generation never fails FHIR operations (graceful degradation)
- [ ] Zero breaking changes when adding new FHIR versions

### Accessibility (WCAG 2.1 Level AA)

- [ ] All templates use semantic HTML (`<dl>`, `<table>` with `<th>`, proper heading hierarchy)
- [ ] Generated narratives include `lang` attribute (WCAG 3.1.1)
- [ ] No skipped heading levels (h1 → h3 prohibited)
- [ ] All tables have proper header cells (`<th>` with `scope` attribute)
- [ ] Images include `alt` text (if used)
- [ ] Links have descriptive text (no "click here")
- [ ] Accessibility tests verify WCAG compliance

### Security (Microsoft SDL)

- [ ] XSS prevention: Scriban auto-escaping enabled (no `html.raw` usage)
- [ ] XSS prevention: `XhtmlSanitizer` removes prohibited elements/attributes
- [ ] FHIR compliance: Only allowed XHTML elements/attributes in output
- [ ] URL validation: `href`/`src` attributes only allow safe protocols (http/https/ftp)
- [ ] Malicious input tests: `<script>`, `javascript:`, `onerror=` patterns blocked
- [ ] Defense in depth: 3-layer validation (Scriban escape → Sanitize → Validate)
- [ ] Security tests verify protection against OWASP Top 10 XSS vectors

### Quality

- [ ] 100% test coverage on core service and template engine
- [ ] Integration tests with malicious input (XSS attempts)
- [ ] Accessibility tests for all built-in templates
- [ ] Documentation includes migration guide for custom templates
- [ ] Documentation includes CSP requirements for consuming applications

---

## References

### FHIR Specification

- [Narrative Data Type](https://hl7.org/fhir/narrative.html)
- [Narrative Generation Rules](https://hl7.org/fhir/narrative-definitions.html#Narrative.status)
- [XHTML Rules](https://hl7.org/fhir/narrative.html#xhtml)

### Accessibility Standards

- [Web Content Accessibility Guidelines (WCAG) 2.1](https://www.w3.org/TR/WCAG21/)
- [Microsoft Compliance - WCAG 2.1](https://learn.microsoft.com/en-us/compliance/regulatory/offering-wcag-2-1)
- [Microsoft Accessibility Conformance Reports](https://www.microsoft.com/en-us/accessibility/conformance-reports)

### Security Standards

- [Microsoft Security Development Lifecycle Practices](https://www.microsoft.com/en-us/securityengineering/sdl/practices)
- [Prevent Cross-Site Scripting (XSS) in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/cross-site-scripting)
- [OWASP Cross Site Scripting Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html)
- [HTML Sanitization in Anti-XSS Library](https://learn.microsoft.com/en-us/archive/blogs/securitytools/html-sanitization-in-anti-xss-library)

### Libraries

- [Scriban](https://github.com/scriban/scriban) - Templating engine
- [Hl7.FhirPath](https://github.com/FirelyTeam/firely-net-sdk) - FHIRPath evaluation

### Related ADRs

- [ADR-2601: IPS Generator](./ADR-2601-ips-generator-implementation.md) - Primary consumer
- [ADR-2540: Advanced FHIR Operations](./ADR-2540-advanced-fhir-operations.md) - Operation infrastructure

---

## Decision Log

| Decision | Rationale | Date |
|----------|-----------|------|
| Use Scriban over Liquid | Better performance, native .NET, async support | 2025-12-16 |
| Version-specific templates | Handles breaking changes between FHIR versions | 2025-12-16 |
| Three-tier template system | Balance rich UX with maintenance burden | 2025-12-16 |
| FHIRPath-first design | Cleaner than property navigation, version-resilient | 2025-12-16 |
| fhir-codegen integration | Scaffolding reduces manual template creation | 2025-12-16 |
| Directory-based override | Simple, no code changes needed for customization | 2025-12-16 |
| Medino behavior integration | Consistent with validation/auth pipeline, operation-agnostic | 2025-12-16 |
| Enabled by default | Improves accessibility and UX out-of-box | 2025-12-16 |
| Don't fail on narrative errors | Narrative generation should never block FHIR operations | 2025-12-16 |
| WCAG 2.1 AA compliance | Microsoft baseline, no conflicts with FHIR spec | 2025-12-16 |
| 3-layer security (escape + sanitize + validate) | Defense in depth against XSS, FHIR compliance | 2025-12-16 |
| Semantic HTML over inline styles | WCAG 1.3.1, allows consuming apps to apply CSS | 2025-12-16 |
| Part of Core SDK (public NuGet) | Reusable across Ignixa products, public consumption | 2025-12-16 |
| Embed templates as resources | Self-contained package, no external file dependencies | 2025-12-16 |
| Version with Core SDK | Consistent versioning, simpler dependency management | 2025-12-16 |
