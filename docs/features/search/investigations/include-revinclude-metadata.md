# ADR 2510: Include/RevInclude Capability Metadata

## Status

Accepted

## Context

The CapabilityStatement needs to advertise supported `_include`, `_revinclude`, and chained parameter capabilities to allow clients to discover available search features. This metadata is populated through the `IncludeRevIncludeCapabilitySegment`.

## Decision

We will implement an `IncludeRevIncludeCapabilitySegment` that automatically populates `searchInclude` and `searchRevInclude` arrays in the CapabilityStatement based on reference-type search parameters.

## Implementation

### IncludeRevIncludeCapabilitySegment

**Location:** `src/Ignixa.Application/Features/Metadata/Segments/IncludeRevIncludeCapabilitySegment.cs`

**Priority:** 40 (runs after SearchParameterCapabilitySegment at priority 30)

This segment populates two key fields in each resource component:

1. **`searchInclude`** - Lists all valid `_include` parameters for this resource type
2. **`searchRevInclude`** - Lists all valid `_revinclude` parameters for this resource type

### How SearchInclude is Populated

For each resource type, the segment finds all **reference-type search parameters** and generates include entries.

**Format:** `ResourceType:searchParameter`

**Example for Patient:**
```json
{
  "type": "Patient",
  "searchInclude": [
    "Patient:general-practitioner",
    "Patient:link",
    "Patient:organization",
    "Patient:*"
  ]
}
```

The wildcard `ResourceType:*` is added if there are any reference parameters, allowing clients to include all referenced resources.

### How SearchRevInclude is Populated

For each resource type, the segment searches **all other resource types** to find reference parameters that target this resource.

**Format:** `SourceResourceType:searchParameter`

**Example for Patient (resources that reference Patient):**
```json
{
  "type": "Patient",
  "searchRevInclude": [
    "Account:patient",
    "Account:subject",
    "AllergyIntolerance:patient",
    "Appointment:actor",
    "Appointment:patient",
    "CareTeam:patient",
    "Observation:patient",
    "Observation:subject",
    ...
  ]
}
```

This tells clients that they can search for Patients and use `_revinclude=Observation:patient` to include all Observations that reference the found Patients.

## Chained Parameter Support

While chained parameters (e.g., `Patient?general-practitioner.name=Smith`) are not explicitly listed in the CapabilityStatement, they are **implicitly supported** through the search parameter metadata.

### How Clients Discover Chained Parameter Support

1. Client looks at the `searchParam` array for a resource
2. Identifies reference-type parameters (type = "reference")
3. For each reference parameter, examines the target resource types
4. Can chain to any search parameter on those target resource types

**Example:**

```json
{
  "type": "Patient",
  "searchParam": [
    {
      "name": "general-practitioner",
      "type": "reference",
      "definition": "http://hl7.org/fhir/SearchParameter/Patient-general-practitioner"
    }
  ]
}
```

The client can look up this SearchParameter definition to find:
- Target types: `Organization`, `Practitioner`, `PractitionerRole`
- Chain possibilities: `Patient?general-practitioner.name=Smith` (chain to Practitioner.name)

### Future Enhancement: Explicit Chain Documentation

If explicit chain documentation is needed, consider adding to the `SearchParamJsonNode` model:

```csharp
public class SearchParamJsonNode : BaseJsonNode
{
    // ...existing properties...
    
    /// <summary>
    /// For reference parameters, lists the target resource types that can be chained to.
    /// </summary>
    public IReadOnlyList<string>? Target { get; set; }
    
    /// <summary>
    /// Examples of supported chained parameters (for documentation).
    /// </summary>
    public IReadOnlyList<string>? ChainExamples { get; set; }
}
```

This could be populated in `SearchParameterCapabilitySegment`:

```csharp
if (sp.Type == SearchParamType.Reference && sp.TargetResourceTypes?.Any() == true)
{
    searchParamNode.Target = sp.TargetResourceTypes;
    searchParamNode.ChainExamples = new List<string> 
    { 
        $"{resourceType}?{sp.Code}.name=...",
        $"{resourceType}?{sp.Code}.identifier=..."
    };
}
```

## Version Hashing

The segment's version hash is based on:
- All reference search parameters (code, base resource types, target resource types)
- Ensures cache invalidation when reference parameters change

## Registration

The segment is registered in `Program.cs`:

```csharp
containerBuilder.RegisterType<Ignixa.Application.Features.Metadata.Segments.IncludeRevIncludeCapabilitySegment>()
    .As<Ignixa.Application.Features.Metadata.Segments.ICapabilitySegment>()
    .SingleInstance();
```

## Testing

To verify the implementation:

1. **Start the server** and request the CapabilityStatement:
   ```
   GET [base]/metadata
   ```

2. **Check a resource component** (e.g., Patient):
   ```json
   {
     "rest": [{
       "resource": [{
         "type": "Patient",
         "searchInclude": [
           "Patient:general-practitioner",
           "Patient:link",
           "Patient:organization",
           "Patient:*"
         ],
         "searchRevInclude": [
           "Account:patient",
           "AllergyIntolerance:patient",
           "Observation:patient",
           ...
         ]
       }]
     }]
   }
   ```

3. **Test _include queries:**
   ```
   GET [base]/Patient?_include=Patient:organization
   GET [base]/Patient?_include=Patient:*
   ```

4. **Test _revinclude queries:**
   ```
   GET [base]/Patient?_revinclude=Observation:patient
   ```

5. **Test chained parameters:**
   ```
   GET [base]/Patient?general-practitioner.name=Smith
   GET [base]/Observation?patient.name=John
   ```

## Benefits

1. **Client Discovery** - Clients can programmatically discover supported include/revinclude operations
2. **API Documentation** - Self-documenting API capabilities
3. **Standards Compliance** - Follows FHIR CapabilityStatement specification
4. **Dynamic Updates** - Automatically reflects changes to search parameters
5. **Cache Efficiency** - Version hash ensures proper cache invalidation

## Related Files

- `IncludeRevIncludeCapabilitySegment.cs` - The segment implementation
- `ResourceComponentJsonNode.cs` - Model with SearchInclude/SearchRevInclude properties
- `SearchParamJsonNode.cs` - Search parameter metadata model
- `SearchParameterInfo.cs` - Source data with TargetResourceTypes

