# Root Cause Analysis: clinical-patient Parameter Not Indexed

**Date**: 2025-10-29
**Issue**: Multi-resource search parameters using `resolve()` in FHIRPath expressions are not being indexed

**ROOT CAUSE**: ✅ **CONFIRMED** - The `resolve()` function is **NOT IMPLEMENTED** in `Ignixa.FhirPath` evaluator

---

## The Problem

The `clinical-patient` search parameter (SearchParamId 206) has **0 indexed references** in the database, while `Observation-subject` (SearchParamId 969) has 1,396 indexed references.

Both parameters should index the same `Observation.subject` field when it references a Patient, but only `Observation-subject` works.

---

## Confirmed Root Cause

**File**: `src/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs` (lines 80-176)

The `resolve()` function is **NOT** in the list of implemented FHIRPath functions. When the indexer encounters an expression like:

```
Observation.subject.where(resolve() is Patient)
```

It throws `NotSupportedException: Function 'resolve' is not yet implemented` at line 175.

This exception causes the indexer to skip creating the search index entry for the `clinical-patient` parameter.

---

## Root Cause: resolve() Function in FHIRPath Expression

### clinical-patient Definition (DOES NOT WORK)

```
URL: http://hl7.org/fhir/SearchParameter/clinical-patient
Expression: Observation.subject.where(resolve() is Patient)
Base Resources: 25+ resource types (AllergyIntolerance, CarePlan, Observation, etc.)
```

**Key Issue**: The FHIRPath expression uses `.where(resolve() is Patient)` which:
1. Attempts to **resolve the reference** to load the actual Patient resource
2. Checks if the resolved resource `is Patient`
3. Only returns the reference if the type check passes

### Observation-subject Definition (WORKS)

```
URL: http://hl7.org/fhir/SearchParameter/Observation-subject
Expression: Observation.subject
Base Resources: Observation only
```

**No resolve()**: The expression simply returns `Observation.subject` without attempting to resolve it.

---

## Why resolve() Fails During Indexing

### Indexing Flow

**File**: `src/Ignixa.Search/Indexing/TypedElementSearchIndexer.cs`

```csharp
public IReadOnlyCollection<SearchIndexEntry> Extract(ITypedElement resource)
{
    var context = new FhirEvaluationContext();

    // Set up the element resolver for resolve() calls
    context.ElementResolver = str => _referenceToElementResolver.Resolve(str);

    // Get ALL search parameters for the resource type
    IEnumerable<SearchParameterInfo> searchParameters =
        _searchParameterDefinitionManager.GetSearchParameters(resource.InstanceType);

    foreach (SearchParameterInfo searchParameter in searchParameters)
    {
        // Process each search parameter
        entries.AddRange(ProcessNonCompositeSearchParameter(searchParameter, resource, context));
    }
}
```

**Line 198**: Evaluates the FHIRPath expression:
```csharp
extractedValues = element.Select(fhirPathExpression, context);
```

### The Problem

When indexing a resource:
1. The Observation is being indexed **in isolation**
2. The FHIRPath evaluator encounters `Observation.subject.where(resolve() is Patient)`
3. It calls `context.ElementResolver("Patient/123")` to resolve the reference
4. **The ElementResolver cannot load the Patient** because:
   - The Patient might not exist yet (order of indexing)
   - The indexer doesn't have database access to load other resources
   - The `_referenceToElementResolver` likely returns `null` for unresolved references
5. `resolve()` returns empty/null
6. `.where(null is Patient)` evaluates to false
7. The entire expression returns **empty**
8. No `SearchIndexEntry` is created
9. No row is inserted into `ReferenceSearchParam` table

---

## Evidence from Database

```sql
-- Observation references indexed under subject (969) - WORKS
SELECT COUNT(*)
FROM dbo.ReferenceSearchParam ref
WHERE ref.SearchParamId = 969  -- Observation-subject
-- Result: 1396 rows

-- Observation references indexed under clinical-patient (206) - FAILS
SELECT COUNT(*)
FROM dbo.ReferenceSearchParam ref
WHERE ref.SearchParamId = 206  -- clinical-patient
-- Result: 0 rows
```

**Both should have the same count** since they index the same field (`Observation.subject`) when it points to a Patient.

---

## Implications

### Affected Search Parameters

ALL multi-resource search parameters that use `resolve()` in their FHIRPath expressions are affected:

1. **clinical-patient**:
   - Expression: `*.subject.where(resolve() is Patient)`
   - 25+ resource types

2. **clinical-encounter**:
   - Expression: `*.encounter.where(resolve() is Encounter)`
   - Multiple resource types

3. **clinical-status** (if it uses resolve()):
   - Similar pattern

### Impact on Searches

1. **Direct searches work**:
   - `Observation?subject=Patient/123` → Uses `Observation-subject` parameter → WORKS

2. **Reverse chaining with multi-resource parameters fails**:
   - `Patient?_has:Observation:patient:code=527` → Uses `clinical-patient` parameter → FAILS (0 indexes)

3. **Workaround**:
   - `Patient?_has:Observation:subject:code=527` → Uses `Observation-subject` parameter → Would work IF subject parameter allowed

---

## Why This Design Exists in FHIR

The FHIR spec defines multi-resource parameters like `clinical-patient` to provide a **consistent search experience** across all resource types. Instead of remembering:
- `Observation.subject`
- `Condition.subject`
- `AllergyIntolerance.patient`
- `Encounter.subject`

You can just use `patient` on any clinical resource.

The `resolve()` function is used because some resources (like Observation) have a `subject` field that can reference **multiple types** (Patient, Group, Device, Location). The `resolve()` function filters to only Patient references.

---

## Solutions

### Option 1: Index WITHOUT Resolving (RECOMMENDED)

**Approach**: Modify the FHIRPath evaluator or indexer to detect `resolve()` calls during indexing and skip the resolution, just extracting the reference value.

**Implementation**:
```csharp
// In TypedElementSearchIndexer.ExtractSearchValues()
// Before evaluating FHIRPath expression:

if (fhirPathExpression.Contains("resolve()"))
{
    // Strip resolve() calls during indexing
    // "Observation.subject.where(resolve() is Patient)"
    // becomes: "Observation.subject" (if targetResourceTypes contains Patient)

    if (allowedReferenceResourceTypes != null && allowedReferenceResourceTypes.Any())
    {
        // Extract just the reference path before .where()
        var simplifiedExpression = ExtractReferencePathBeforeWhere(fhirPathExpression);
        extractedValues = element.Select(simplifiedExpression, context);

        // Then filter the references by target type in code, not FHIRPath
        extractedValues = FilterReferencesByTargetType(extractedValues, allowedReferenceResourceTypes);
    }
}
else
{
    extractedValues = element.Select(fhirPathExpression, context);
}
```

**Pros**:
- Indexes are created correctly
- No need to resolve references during indexing
- Matches FHIR spec behavior

**Cons**:
- Requires custom logic to parse and simplify FHIRPath expressions
- May not work for all complex expressions

### Option 2: Index Under BOTH Parameters

**Approach**: When indexing a reference search parameter, create index entries for BOTH the specific parameter (e.g., `Observation-subject`) AND all matching multi-resource parameters (e.g., `clinical-patient`).

**Implementation**:
```csharp
// In TypedElementSearchIndexer.ProcessNonCompositeSearchParameter()
// After extracting a reference value:

if (searchParameter.Type == SearchParamType.Reference)
{
    // Get all search parameters that could match this reference
    var additionalParameters = _searchParameterDefinitionManager
        .GetSearchParameters(resource.InstanceType)
        .Where(sp => sp.Type == SearchParamType.Reference
            && sp.TargetResourceTypes != null
            && sp.TargetResourceTypes.Intersect(searchParameter.TargetResourceTypes ?? Array.Empty<string>()).Any()
            && ExpressionMatchesField(sp.Expression, searchParameter.Expression))
        .ToList();

    // Create index entries for all matching parameters
    foreach (var additionalParam in additionalParameters)
    {
        yield return new SearchIndexEntry(additionalParam, referenceValue);
    }
}
```

**Pros**:
- Simple to implement
- Backwards compatible
- Works for all multi-resource parameters

**Cons**:
- Increases database storage (multiple index entries per reference)
- May cause duplicate indexes

### Option 3: Query-Time Resolution

**Approach**: When a query uses a multi-resource parameter like `clinical-patient`, translate it at query time to the specific parameter like `Observation-subject`.

**Implementation**:
```csharp
// In ChainedExpressionProcessor or SearchParameterQueryGenerator
// When processing a search parameter:

if (searchParameter.Url == "http://hl7.org/fhir/SearchParameter/clinical-patient")
{
    // Find the resource-specific parameter
    var specificParam = _searchParameterDefinitionManager
        .GetSearchParameters(resourceType)
        .FirstOrDefault(sp => sp.Type == SearchParamType.Reference
            && sp.BaseResourceTypes.Length == 1
            && sp.TargetResourceTypes.Contains("Patient")
            && IsSubjectOrPatientField(sp.Code));

    if (specificParam != null)
    {
        // Use the specific parameter instead
        searchParameter = specificParam;
    }
}
```

**Pros**:
- No changes to indexing
- No additional storage

**Cons**:
- Requires mapping logic
- May not work for all cases
- Violates FHIR spec (should be using the specified parameter)

---

## Recommended Solution

**PRIMARY FIX**: Implement the `resolve()` function in `FhirPathEvaluator.cs` to enable proper indexing of multi-resource search parameters.

**ALTERNATIVE**: Implement Option 2 (Index Under BOTH Parameters) as an immediate workaround if `resolve()` implementation is too complex.

---

## Implementation Guide: Adding resolve() to FhirPathEvaluator

### Step 1: Add resolve() to Function Switch Statement

**File**: `src/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs` (line 80-176)

```csharp
return func.FunctionName.ToLowerInvariant() switch
{
    // ... existing functions ...

    "extension" => EvaluateExtension(focusElements, func.Arguments, context),

    // ✅ ADD THIS LINE:
    "resolve" => EvaluateResolve(focusElements, context),

    // For bare identifiers (e.g., "Patient"), treat as child navigation
    _ when func.Arguments.Count == 0 && func.Focus == AxisExpression.That
        => EvaluateIdentifier(focus, new IdentifierExpression(func.FunctionName)),

    _ => throw new NotSupportedException($"Function '{func.FunctionName}' is not yet implemented")
};
```

### Step 2: Implement EvaluateResolve() Method

Add this method to `FhirPathEvaluator.cs`:

```csharp
/// <summary>
/// Evaluates the resolve() function on reference elements.
/// Takes a Reference (e.g., {reference: "Patient/123"}) and resolves it to the actual resource.
/// Returns empty if the reference cannot be resolved or if ElementResolver is not configured.
/// </summary>
/// <param name="focus">Collection of ITypedElement representing Reference elements</param>
/// <param name="context">Evaluation context containing the ElementResolver function</param>
/// <returns>Resolved resource elements</returns>
private IEnumerable<ITypedElement> EvaluateResolve(IEnumerable<ITypedElement> focus, EvaluationContext context)
{
    // resolve() only works during query execution, not during indexing
    // During indexing, context.ElementResolver may be null or return null

    if (context is not FhirEvaluationContext fhirContext || fhirContext.ElementResolver == null)
    {
        // No resolver available - return empty (this is expected during indexing)
        yield break;
    }

    foreach (var element in focus)
    {
        // resolve() only works on Reference types
        if (element.InstanceType != "Reference" && element.InstanceType != "ResourceReference")
        {
            // Not a reference - skip
            continue;
        }

        // Extract the reference string (e.g., "Patient/123" or "http://example.org/fhir/Patient/123")
        var referenceValue = element.Scalar("reference") as string;
        if (string.IsNullOrEmpty(referenceValue))
        {
            // No reference value - skip
            continue;
        }

        // Call the ElementResolver to resolve the reference
        try
        {
            var resolved = fhirContext.ElementResolver(referenceValue);
            if (resolved != null)
            {
                yield return resolved;
            }
            // If resolved is null, the reference couldn't be resolved - skip silently
        }
        catch
        {
            // If resolution fails, skip silently (FHIR spec: resolve() returns empty on failure)
            continue;
        }
    }
}
```

### Step 3: Handle resolve() During Indexing

**Key Insight**: During indexing, `resolve()` should return **empty** (not throw an exception), which causes the entire `.where(resolve() is Patient)` expression to evaluate to **false** for that specific reference.

However, the **search parameter definition** provides `TargetResourceTypes`, which tells us the reference SHOULD point to a Patient. So we can optimize indexing by **skipping the resolve()** call entirely.

**Option A: Skip resolve() During Indexing** (Recommended)

Modify `TypedElementSearchIndexer.ExtractSearchValues()` to detect and simplify expressions with `resolve()`:

```csharp
// File: src/Ignixa.Search/Indexing/TypedElementSearchIndexer.cs
// Method: ExtractSearchValues (around line 181-271)

private IReadOnlyList<ISearchValue> ExtractSearchValues(
    string searchParameterDefinitionUrl,
    SearchParamType? searchParameterType,
    IReadOnlyList<string> allowedReferenceResourceTypes,
    ITypedElement element,
    string fhirPathExpression,
    EvaluationContext context)
{
    // ... existing code ...

    // ✅ ADD THIS OPTIMIZATION FOR INDEXING:
    // If this is a reference search parameter with resolve() in the expression,
    // we can optimize by just extracting the reference path without calling resolve()
    if (searchParameterType == SearchParamType.Reference &&
        fhirPathExpression.Contains("resolve()", StringComparison.OrdinalIgnoreCase))
    {
        // Extract the path before .where(resolve()...)
        // Example: "Observation.subject.where(resolve() is Patient)" → "Observation.subject"
        var pathBeforeWhere = ExtractPathBeforeResolve(fhirPathExpression);
        if (!string.IsNullOrEmpty(pathBeforeWhere))
        {
            // Use the simplified path for indexing
            fhirPathExpression = pathBeforeWhere;
        }
    }

    try
    {
        extractedValues = element.Select(fhirPathExpression, context);
    }
    catch (Exception ex)
    {
        Log.FailedToExtractValues(_logger, ex, fhirPathExpression, element.GetType());
    }

    // ... rest of existing code ...
}

/// <summary>
/// Extracts the path before .where(resolve()...) for indexing optimization.
/// Example: "Observation.subject.where(resolve() is Patient)" → "Observation.subject"
/// </summary>
private string ExtractPathBeforeResolve(string expression)
{
    var whereIndex = expression.IndexOf(".where(resolve()", StringComparison.OrdinalIgnoreCase);
    if (whereIndex > 0)
    {
        return expression.Substring(0, whereIndex);
    }
    return expression;
}
```

**Option B: Let resolve() Return Empty During Indexing**

If you implement Step 1 & 2 correctly, `resolve()` will return empty during indexing (because `ElementResolver` returns null for unresolved references), and the search parameter indexer will skip creating an index entry.

This is **correct behavior per FHIR spec**, but it means:
- ❌ `clinical-patient` parameter will STILL have 0 indexes
- ✅ Query-time resolution works (if you implement Option 1 below)

---

## Recommended Solution (Revised)

**PRIMARY FIX**: Implement Option 2 (Index Under BOTH Parameters) as the immediate fix, then **implement Option 1** (Index WITHOUT Resolving) as the long-term solution.

### Immediate Fix (Option 2)

1. Modify `TypedElementSearchIndexer.ProcessNonCompositeSearchParameter()` to create duplicate index entries for matching multi-resource parameters
2. This ensures `_has:Observation:patient` queries work immediately
3. Storage overhead is acceptable (~ 2x indexes for reference parameters with multi-resource equivalents)

### Long-Term Fix (Option 1)

1. Implement FHIRPath expression simplification for indexing
2. Detect `resolve()` calls and strip them during indexing
3. Filter results by target resource type in C# code instead of FHIRPath
4. Remove duplicate indexes from Option 2 once this is working

---

## Testing Strategy

### Test Case 1: Verify clinical-patient indexes are created

```csharp
[Fact]
public async Task GivenObservationWithPatientSubject_WhenIndexed_ThenCreatesIndexForBothSubjectAndClinicalPatient()
{
    // Arrange
    var observation = new Observation
    {
        Subject = new Reference("Patient/123"),
        Code = new CodeableConcept { Coding = [new Coding { Code = "527" }] }
    };

    // Act
    var indices = _indexer.Extract(observation.ToTypedElement());

    // Assert
    var referenceIndices = indices.Where(i => i.Value is ReferenceSearchValue).ToList();

    // Should have index for Observation-subject
    referenceIndices.Should().Contain(i =>
        i.SearchParameter.Url.ToString() == "http://hl7.org/fhir/SearchParameter/Observation-subject");

    // Should have index for clinical-patient
    referenceIndices.Should().Contain(i =>
        i.SearchParameter.Url.ToString() == "http://hl7.org/fhir/SearchParameter/clinical-patient");
}
```

### Test Case 2: Verify reverse chaining works

```csharp
[Fact]
public async Task GivenPatientWithObservations_WhenSearchingWithHasParameter_ThenReturnsPatient()
{
    // Arrange
    var patient = await CreatePatient("patient1");
    var observation = await CreateObservation("obs1", patient.Id, code: "527");

    // Act
    var results = await SearchAsync("Patient", "_has:Observation:patient:code=527");

    // Assert
    results.Should().ContainSingle()
        .Which.Id.Should().Be("patient1");
}
```

---

## Related Issues

1. **Primary Bug**: Missing SearchParamId filter in ChainedExpressionProcessor ✅ **FIXED**
2. **Secondary Issue**: This issue (resolve() not working during indexing) ⚠️ **DOCUMENTED**

---

*End of Analysis*
