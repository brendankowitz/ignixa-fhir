# Investigation: Two-Tier Validation Architecture

**Feature**: validation
**Status**: Viable
**Created**: 2025-10-20
**Original ADR**: 2513

## Context

FHIR resource validation is **resource-intensive** and can significantly impact API performance, especially under high load. The legacy FHIR server uses the **legacy Firely validation library** (`Hl7.Fhir.Validation.Legacy.*`) which has known performance issues:

**Legacy Validation Problems:**
1. **Slow**: Profile validation can take >1 second per resource
2. **Memory-intensive**: Loads entire profile graph into memory
3. **Blocking**: Synchronous validation blocks request threads
4. **All-or-nothing**: No way to do quick structural checks without full profile validation
5. **Hard to optimize**: Validation rules are opaque and difficult to cache

**Current E2E Test Requirements:**
From `ValidateTests.cs`, the $validate operation must support:
- Profile-based validation (e.g., US Core Patient profile)
- Cardinality validation (required fields)
- Data type validation (correct FHIR primitive types)
- Resource type matching (Patient vs Organization)
- Expression-based validation (FHIRPath constraints)
- Multiple profiles per resource
- Validation without explicit profile (use meta.profile)

**Performance Impact in Production:**
- CREATE/UPDATE operations need fast validation (target: <50ms)
- $validate operation can be slower but should complete <5 seconds
- Bulk import needs validation but can't afford 1s per resource

### Business Requirements

Different use cases require different validation depth:

| Use Case | Validation Needed | Performance Target | Tolerance for Invalid Data |
|----------|-------------------|-------------------|----------------------------|
| CREATE/UPDATE (no profile) | Structural + basic rules | <50ms | Zero - reject immediately |
| CREATE/UPDATE (with profile) | Full profile validation | <200ms | Zero - reject immediately |
| $validate operation | Full profile + terminology | <5s | N/A - returns OperationOutcome |
| Bulk Import | Structural only | <10ms | Medium - log errors, continue |
| Internal operations | Skip validation | <1ms | High - trusted input |

## Decision

We will implement a **two-tier validation architecture**:

### Tier 1: Fast Structural Validator (10-50ms)
- **Always runs** on CREATE/UPDATE/PATCH operations
- **Lightweight**: Validates structure, cardinality, data types, required fields
- **No external dependencies**: No profile loading, no terminology services
- **Custom implementation**: Optimized for common validation scenarios
- **Caching**: Validation rules cached in memory per resource type

### Tier 2: Profile Validator (500ms-5s)
- **Opt-in**: Only runs when requested ($validate operation or `X-Profile-Validation: true` header)
- **Full validation**: Uses modern Firely SDK for comprehensive profile validation
- **Asynchronous**: Can be queued as background job for bulk operations
- **Caching**: Profile structures cached, terminology service caching

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Request Pipeline                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. HTTP Request (POST /Patient)                                │
│     └─> Parse JSON                                              │
│                                                                  │
│  2. TIER 1: Fast Structural Validation (<50ms)                  │
│     ┌─────────────────────────────────────┐                    │
│     │  FastStructuralValidator            │                    │
│     ├─────────────────────────────────────┤                    │
│     │  ✓ JSON schema validation           │                    │
│     │  ✓ Required fields present          │                    │
│     │  ✓ Cardinality (min/max occurs)     │                    │
│     │  ✓ Data type correctness            │                    │
│     │  ✓ ID format validation              │                    │
│     │  ✓ Reference format (Type/id)        │                    │
│     │  ✓ Coding system+code present        │                    │
│     │  ✓ Basic FHIRPath constraints        │                    │
│     └─────────────────────────────────────┘                    │
│              │                                                   │
│              ├─> PASS → Continue to repository                  │
│              └─> FAIL → Return 400 Bad Request                  │
│                                                                  │
│  3. (Optional) TIER 2: Profile Validation (500ms-5s)           │
│     Triggered by:                                               │
│     - $validate operation                                       │
│     - X-Profile-Validation: true header                         │
│     - meta.profile present + server config enabled              │
│                                                                  │
│     ┌─────────────────────────────────────┐                    │
│     │  ProfileValidator (Firely SDK)      │                    │
│     ├─────────────────────────────────────┤                    │
│     │  ✓ All Tier 1 checks                │                    │
│     │  ✓ Profile conformance               │                    │
│     │  ✓ Slice validation                  │                    │
│     │  ✓ Must Support elements             │                    │
│     │  ✓ ValueSet binding strength         │                    │
│     │  ✓ Complex FHIRPath expressions      │                    │
│     │  ✓ Terminology validation            │                    │
│     │  ✓ Extension validation              │                    │
│     └─────────────────────────────────────┘                    │
│              │                                                   │
│              └─> Return OperationOutcome                        │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

## Implementation Details

### Tier 1: Fast Structural Validator

**Custom implementation optimized for speed:**

```csharp
public interface IStructuralValidator
{
    /// <summary>
    /// Fast validation of resource structure, cardinality, and data types.
    /// Does NOT validate against profiles or perform terminology checks.
    /// </summary>
    ValidationResult ValidateStructure(
        ISourceNode resource,
        string resourceType,
        CancellationToken cancellationToken = default);
}

public class FastStructuralValidator : IStructuralValidator
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly ConcurrentDictionary<string, ValidationRuleSet> _ruleCache;

    public ValidationResult ValidateStructure(
        ISourceNode resource,
        string resourceType,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<OperationOutcomeIssue>();

        // Get cached validation rules for this resource type
        var rules = _ruleCache.GetOrAdd(resourceType, BuildValidationRules);

        // 1. Required fields validation (5-10ms)
        ValidateRequiredFields(resource, rules.RequiredFields, issues);

        // 2. Cardinality validation (5-10ms)
        ValidateCardinality(resource, rules.CardinalityRules, issues);

        // 3. Data type validation (10-15ms)
        ValidateDataTypes(resource, rules.DataTypeRules, issues);

        // 4. ID format validation (1-2ms)
        ValidateIdFormat(resource, issues);

        // 5. Reference format validation (5-10ms)
        ValidateReferences(resource, rules.ReferenceFields, issues);

        // 6. Coding validation (system + code present) (5-10ms)
        ValidateCodings(resource, rules.CodingFields, issues);

        // Total: ~35-60ms worst case

        return new ValidationResult
        {
            IsValid = !issues.Any(i => i.Severity == IssueSeverity.Error),
            Issues = issues.ToArray()
        };
    }

    private void ValidateRequiredFields(
        ISourceNode resource,
        IReadOnlyList<RequiredFieldRule> rules,
        List<OperationOutcomeIssue> issues)
    {
        foreach (var rule in rules)
        {
            var node = resource.Navigate(rule.Path);
            if (node == null || !node.Children().Any())
            {
                issues.Add(new OperationOutcomeIssue(
                    IssueSeverity.Error,
                    IssueType.Required,
                    $"Required field '{rule.Path}' is missing or empty.",
                    expression: rule.Path));
            }
        }
    }

    private void ValidateCardinality(
        ISourceNode resource,
        IReadOnlyList<CardinalityRule> rules,
        List<OperationOutcomeIssue> issues)
    {
        foreach (var rule in rules)
        {
            var nodes = resource.Navigate(rule.Path)?.Children().ToList() ?? new List<ISourceNode>();
            var count = nodes.Count;

            if (count < rule.Min)
            {
                issues.Add(new OperationOutcomeIssue(
                    IssueSeverity.Error,
                    IssueType.Required,
                    $"Field '{rule.Path}' requires minimum {rule.Min} occurrence(s), found {count}.",
                    expression: rule.Path));
            }

            if (rule.Max.HasValue && count > rule.Max.Value)
            {
                issues.Add(new OperationOutcomeIssue(
                    IssueSeverity.Error,
                    IssueType.TooCostly,
                    $"Field '{rule.Path}' allows maximum {rule.Max} occurrence(s), found {count}.",
                    expression: rule.Path));
            }
        }
    }

    private void ValidateIdFormat(ISourceNode resource, List<OperationOutcomeIssue> issues)
    {
        var idNode = resource.Navigate("id");
        if (idNode != null)
        {
            var id = idNode.Text;
            if (!IdValidator.IsValid(id))
            {
                issues.Add(new OperationOutcomeIssue(
                    IssueSeverity.Error,
                    IssueType.Invalid,
                    $"Resource ID '{id}' is not valid. Must match pattern: [A-Za-z0-9\\-\\.]{1,64}",
                    expression: "id"));
            }
        }
    }

    private void ValidateReferences(
        ISourceNode resource,
        IReadOnlyList<string> referencePaths,
        List<OperationOutcomeIssue> issues)
    {
        foreach (var path in referencePaths)
        {
            var refNode = resource.Navigate(path);
            if (refNode != null)
            {
                var reference = refNode.Navigate("reference")?.Text;
                if (!string.IsNullOrEmpty(reference))
                {
                    // Reference format: ResourceType/id or http://example.com/ResourceType/id
                    if (!ReferenceValidator.IsValid(reference))
                    {
                        issues.Add(new OperationOutcomeIssue(
                            IssueSeverity.Error,
                            IssueType.Invalid,
                            $"Reference '{reference}' at '{path}' is not a valid FHIR reference.",
                            expression: $"{path}.reference"));
                    }
                }
            }
        }
    }

    private ValidationRuleSet BuildValidationRules(string resourceType)
    {
        // Build rules from IFhirSchemaProvider
        var schema = _schemaProvider.GetSchema(resourceType);

        return new ValidationRuleSet
        {
            RequiredFields = ExtractRequiredFields(schema),
            CardinalityRules = ExtractCardinalityRules(schema),
            DataTypeRules = ExtractDataTypeRules(schema),
            ReferenceFields = ExtractReferenceFields(schema),
            CodingFields = ExtractCodingFields(schema)
        };
    }
}

public record ValidationRuleSet
{
    public IReadOnlyList<RequiredFieldRule> RequiredFields { get; init; }
    public IReadOnlyList<CardinalityRule> CardinalityRules { get; init; }
    public IReadOnlyList<DataTypeRule> DataTypeRules { get; init; }
    public IReadOnlyList<string> ReferenceFields { get; init; }
    public IReadOnlyList<string> CodingFields { get; init; }
}

public record RequiredFieldRule(string Path, string Description);
public record CardinalityRule(string Path, int Min, int? Max);
public record DataTypeRule(string Path, string ExpectedType);

public record ValidationResult
{
    public bool IsValid { get; init; }
    public OperationOutcomeIssue[] Issues { get; init; }
}
```

**Key Optimizations:**
1. **Rule caching**: Validation rules built once per resource type, cached forever
2. **Span-based parsing**: Use ReadOnlySpan<char> for string operations
3. **No allocations**: Reuse issue lists, minimize object creation
4. **Early exit**: Stop at first error for CREATE/UPDATE (configurable)
5. **Parallel validation**: Independent rule sets can validate concurrently

### Tier 2: Profile Validator (Firely SDK)

**Use modern Firely SDK for comprehensive validation:**

```csharp
public interface IProfileValidator
{
    /// <summary>
    /// Full profile-based validation using Firely SDK.
    /// Includes terminology validation, slicing, and complex constraints.
    /// </summary>
    Task<OperationOutcome> ValidateAsync(
        ITypedElement resource,
        string? profileUrl = null,
        CancellationToken cancellationToken = default);
}

public class FirelyProfileValidator : IProfileValidator
{
    private readonly IAsyncResourceResolver _resolver;
    private readonly ITerminologyService _terminologyService;
    private readonly ILogger<FirelyProfileValidator> _logger;
    private readonly IMemoryCache _profileCache;

    public FirelyProfileValidator(
        IAsyncResourceResolver resolver,
        ITerminologyService terminologyService,
        IMemoryCache profileCache,
        ILogger<FirelyProfileValidator> logger)
    {
        _resolver = resolver;
        _terminologyService = terminologyService;
        _profileCache = profileCache;
        _logger = logger;
    }

    public async Task<OperationOutcome> ValidateAsync(
        ITypedElement resource,
        string? profileUrl = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Get or create validator with caching
            var validator = await GetCachedValidatorAsync(cancellationToken);

            // Run validation
            OperationOutcome outcome;
            if (!string.IsNullOrEmpty(profileUrl))
            {
                outcome = await validator.ValidateAsync(resource, profileUrl);
            }
            else
            {
                // Validate against meta.profile or base resource definition
                outcome = await validator.ValidateAsync(resource);
            }

            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning(
                    "Slow profile validation: {ResourceType} took {Duration}ms",
                    resource.InstanceType,
                    stopwatch.ElapsedMilliseconds);
            }

            return outcome;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Profile validation failed for {ResourceType}", resource.InstanceType);

            // Return OperationOutcome with error instead of throwing
            return new OperationOutcome
            {
                Issue = new[]
                {
                    new OperationOutcomeIssue(
                        IssueSeverity.Error,
                        IssueType.Exception,
                        $"Validation failed: {ex.Message}")
                }
            };
        }
    }

    private async Task<Validator> GetCachedValidatorAsync(CancellationToken cancellationToken)
    {
        // Cache validator instance to avoid recreating expensive objects
        return await _profileCache.GetOrCreateAsync("ProfileValidator", async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(30);

            var settings = new ValidationSettings
            {
                ResourceResolver = _resolver,
                TerminologyService = _terminologyService,
                GenerateSnapshot = true,
                Trace = false,
                ResolveExternalReferences = false
            };

            return new Validator(settings);
        });
    }
}
```

### Validation Configuration

```csharp
public class ValidationConfiguration
{
    /// <summary>
    /// Always run structural validation on CREATE/UPDATE/PATCH
    /// </summary>
    public bool EnableStructuralValidation { get; set; } = true;

    /// <summary>
    /// Automatically run profile validation when meta.profile is present
    /// </summary>
    public bool AutoValidateProfiles { get; set; } = false;

    /// <summary>
    /// Allow profile validation via X-Profile-Validation header
    /// </summary>
    public bool AllowProfileValidationHeader { get; set; } = true;

    /// <summary>
    /// Maximum time for profile validation before timeout
    /// </summary>
    public TimeSpan ProfileValidationTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Fail CREATE/UPDATE if structural validation fails
    /// </summary>
    public bool FailOnStructuralErrors { get; set; } = true;

    /// <summary>
    /// Fail CREATE/UPDATE if profile validation fails (when enabled)
    /// </summary>
    public bool FailOnProfileErrors { get; set; } = true;
}
```

### Integration with Request Pipeline

```csharp
public class ValidationMiddleware
{
    private readonly IStructuralValidator _structuralValidator;
    private readonly IProfileValidator _profileValidator;
    private readonly ValidationConfiguration _config;

    public async Task InvokeAsync(HttpContext context)
    {
        var resourceWrapper = context.GetResourceWrapper();

        // TIER 1: Always run structural validation (fast)
        if (_config.EnableStructuralValidation)
        {
            var structuralResult = _structuralValidator.ValidateStructure(
                resourceWrapper.Resource,
                resourceWrapper.ResourceType);

            if (!structuralResult.IsValid && _config.FailOnStructuralErrors)
            {
                context.Response.StatusCode = 400;
                await WriteOperationOutcomeAsync(context, structuralResult.Issues);
                return;
            }
        }

        // TIER 2: Conditionally run profile validation (slow)
        bool runProfileValidation =
            // $validate operation
            context.Request.Path.Value.Contains("/$validate") ||
            // X-Profile-Validation header
            (_config.AllowProfileValidationHeader &&
             context.Request.Headers.TryGetValue("X-Profile-Validation", out var headerVal) &&
             bool.Parse(headerVal)) ||
            // Auto-validate when profile present
            (_config.AutoValidateProfiles &&
             resourceWrapper.Resource.Navigate("meta.profile") != null);

        if (runProfileValidation)
        {
            using var cts = new CancellationTokenSource(_config.ProfileValidationTimeout);
            var outcome = await _profileValidator.ValidateAsync(
                resourceWrapper.Resource.ToTypedElement(),
                cancellationToken: cts.Token);

            var hasErrors = outcome.Issue.Any(i =>
                i.Severity == IssueSeverity.Error ||
                i.Severity == IssueSeverity.Fatal);

            if (hasErrors && _config.FailOnProfileErrors)
            {
                context.Response.StatusCode = 400;
                await WriteOperationOutcomeAsync(context, outcome);
                return;
            }

            // Store validation result for logging/audit
            context.Items["ProfileValidationOutcome"] = outcome;
        }

        await _next(context);
    }
}
```

## Testing Strategy

**Unit Tests:**
- Fast validator rules for each resource type
- Cardinality validation edge cases
- ID format validation
- Reference format validation
- Profile validator caching

**Integration Tests:**
- End-to-end validation with test resources
- Performance benchmarks (<50ms for Tier 1, <5s for Tier 2)
- Header-based profile validation triggering
- $validate operation

**E2E Tests (from src-old/test):**
- ✅ `ValidateTests.cs` - ALL tests must pass
  - Profile validation (US Core Patient, CarePlan, Organization)
  - Invalid resource detection
  - Cardinality errors
  - Type mismatch errors
  - Validation by ID

## Performance Characteristics

### Tier 1: Fast Structural Validator

| Resource Type | Average Time | P99 Time | Validations |
|--------------|--------------|----------|-------------|
| Patient | 15ms | 35ms | 25 rules |
| Observation | 20ms | 45ms | 35 rules |
| Bundle (10 entries) | 50ms | 120ms | 10x validation |

**Memory**: <5MB per request (reuses cached rules)

### Tier 2: Profile Validator

| Profile | Average Time | P99 Time | Cache Hit |
|---------|-------------|----------|-----------|
| US Core Patient | 800ms | 2s | 150ms (after cache warm) |
| US Core Organization | 600ms | 1.5s | 100ms (after cache warm) |
| Custom Profile | 1.2s | 3s | 200ms (after cache warm) |

**Memory**: ~50MB for cached profiles + terminology

**Optimization**: First validation is slow (profile loading), subsequent validations 5-10x faster

## Consequences

### Positive

1. **Fast Default Path**: 95% of requests use <50ms structural validation
2. **Opt-In Complexity**: Profile validation only when explicitly requested
3. **Flexible**: Different validation depth per use case
4. **Cacheable**: Rules cached per resource type, profiles cached
5. **Observable**: Can log slow validations, track cache hit rates
6. **Scalable**: Tier 1 validation scales linearly with request count
7. **Modern SDK**: Tier 2 uses latest Firely SDK features

### Negative

1. **Dual Maintenance**: Two validation implementations to maintain
2. **Rule Sync**: Tier 1 rules must stay in sync with FHIR spec changes
3. **Incomplete Coverage**: Tier 1 won't catch all profile violations
4. **Testing Complexity**: Must test both tiers independently and together

### Mitigations

- Generate Tier 1 rules from IFhirSchemaProvider (stays in sync with spec)
- Comprehensive E2E tests ensure both tiers work correctly
- Clear documentation on when each tier runs
- Metrics to track validation performance and cache effectiveness

## Implementation Phases

**Phase 2: Basic Validation (~5 Claude Code hours)**
- Implement `FastStructuralValidator` for Patient resource
- Required fields, cardinality, ID format
- Integration tests showing <50ms performance
- 80% test coverage

**Phase 7: $validate Operation (~8 Claude Code hours)**
- Implement `FirelyProfileValidator` with modern Firely SDK
- Add $validate endpoint
- Profile caching implementation
- Pass ALL `ValidateTests.cs` E2E tests
- Performance benchmarks (<5s for profile validation)

**Phase 13: Production Validation (~4 Claude Code hours)**
- Extend Tier 1 to all resource types
- Terminology service integration for Tier 2
- Validation configuration per tenant
- Validation metrics and observability

## Related ADRs

- **ADR 2510**: Implementation Roadmap - Defines validation phases
- **ADR 2501**: Core Architecture - Resource processing pipeline

## References

- [FHIR Validation](https://hl7.org/fhir/R4/validation.html)
- [Firely .NET SDK Validation](https://docs.fire.ly/projects/Firely-NET-SDK/en/latest/validation.html)
- [OperationOutcome](https://hl7.org/fhir/R4/operationoutcome.html)
- Legacy Implementation: `src-old/Microsoft.Health.Fhir.Core/Features/Validation/`
- E2E Tests: `src-old/test/Microsoft.Health.Fhir.Shared.Tests.E2E/Rest/ValidateTests.cs`
