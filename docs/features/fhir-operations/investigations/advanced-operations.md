# Investigation: Advanced FHIR Operations Implementation

**Feature**: fhir-operations
**Status**: Proposed
**Created**: 2025-11-18
**Dependencies**:
- Existing operation infrastructure (`IPackageFeature`, `OperationsSegment`)
- Package resource repository (for OperationDefinitions)
- Multi-tenant routing (existing)
- DurableTask framework (for async operations)

---

## Context

### Problem Statement

Based on [fhir-operations-support-analysis.md](./fhir-operations-support-analysis.md), Ignixa currently supports 3 FHIR operations: $validate, $export (system-level), and Group/$export (bulk data export for patient cohorts). To achieve compliance with US Core, IPA, and Da Vinci Implementation Guides, we need to implement 5 additional high-priority operations:

1. **$docref** - Document retrieval (US Core, IPA - **SHALL**)
2. **$member-match** - Member identity matching (Da Vinci HRex - **SHALL** for payers)
3. **$submit-attachment** - Clinical attachments for claims (Da Vinci CDex - **SHALL**)
4. **$questionnaire-package** - Prior auth questionnaires (Da Vinci DTR - **SHALL**)
5. **$document** - FHIR document generation (FHIR Core - common)

**Note**: The existing Group/$export and Patient/$export operations provide equivalent functionality to $group-everything and $patient-everything operations, satisfying CARIN Blue Button and PDex requirements for bulk patient data access.

### Certification Impact

| Operation | Blocks Certification | Priority |
|-----------|---------------------|----------|
| $docref | ONC (g)(10), US Core, IPA | **P0** |
| $member-match | Da Vinci Implementer (payers) | **P1** |
| $submit-attachment | Da Vinci CDex | **P1** |
| $questionnaire-package | Da Vinci DTR | **P1** |
| $document | Clinical document workflows | **P2** |

**Note**: CARIN Blue Button and PDex requirements are already satisfied by the existing Group/$export operation.

---

## Architecture Overview

### Existing Operation Infrastructure

Ignixa has proven patterns for FHIR operations:

**1. Operation Registration** (`IPackageFeature`)
```csharp
public interface IPackageFeature {
    string PackageId { get; }
    IReadOnlyList<string> SystemOperations { get; }
    IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations { get; }
    IReadOnlyList<string>? SupportedFhirVersions { get; }
}
```

**2. Endpoint Pattern** (Minimal API)
- File: `src/Ignixa.Api/Endpoints/*Endpoints.cs`
- Multi-tenant routing: `/tenant/{tenantId}/{resource}/$operation`
- Tenant-agnostic routing: `/{resource}/$operation` (single-tenant only)

**3. Handler Pattern** (Medino)
- Query/Command: `IRequest<TResult>`
- Handler: `IRequestHandler<TRequest, TResult>`
- Async processing: DurableTask orchestrations

**4. CapabilityStatement Integration**
- `OperationsSegment` dynamically adds operations from `IPackageFeature`
- Loads `OperationDefinition` resources from `PackageResource` table
- Generates `capability.rest.operation[]` entries

---

## Operation Specifications

### 1. $docref - Document Reference Query

**Canonical**: `http://hl7.org/fhir/OperationDefinition/DocumentReference-docref`
**Type**: Type-level (DocumentReference)
**Priority**: **P0** (blocks US Core certification)
**Effort**: 30-40 hours

#### FHIR Specification

**Invocation**:
```
GET [base]/DocumentReference/$docref?patient={id}&start={date}&end={date}&type={code}
```

**Input Parameters**:

| Parameter | Card | Type | Description |
|-----------|------|------|-------------|
| patient | 1..1 | id | Patient ID on this server |
| start | 0..1 | dateTime | Optional start of care date range |
| end | 0..1 | dateTime | Optional end of care date range |
| type | 0..1 | CodeableConcept | Document type (LOINC, e.g., 34133-9 for CCD) |
| on-demand | 0..1 | boolean | Include on-demand documents |

**Output**: Bundle (searchset) containing DocumentReference resources

**Use Cases**:
- US Core: "Blue Button" patient document access
- IPA: Cross-border patient document retrieval
- Clinical: Fetch latest CCD for care transitions

#### Implementation Design

**File Structure**:
```
src/Ignixa.Api/Endpoints/DocumentEndpoints.cs (new)
src/Ignixa.Application.Operations/Features/DocRef/
  ├── DocRefQuery.cs
  ├── DocRefHandler.cs
  └── DocumentReferenceFeature.cs
```

**Endpoint Implementation**:

```csharp
// File: src/Ignixa.Api/Endpoints/DocumentEndpoints.cs
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-explicit route
        endpoints.MapGet("/tenant/{tenantId:int}/DocumentReference/$docref", HandleDocRefAsync)
            .WithName("DocRefTenant")
            .Produces<BundleJsonNode>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<OperationOutcomeJsonNode>(StatusCodes.Status400BadRequest);

        // Tenant-agnostic route (single-tenant only)
        endpoints.MapGet("/DocumentReference/$docref", HandleDocRefAgnosticAsync)
            .WithName("DocRefAgnostic")
            .Produces<BundleJsonNode>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson);

        return endpoints;
    }

    private static async Task<IResult> HandleDocRefAsync(
        HttpContext context,
        int tenantId,
        [FromQuery] string patient,
        [FromQuery] DateTimeOffset? start,
        [FromQuery] DateTimeOffset? end,
        [FromQuery] string? type,
        [FromQuery] bool? onDemand,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Validate patient parameter
        if (string.IsNullOrWhiteSpace(patient))
        {
            return Results.BadRequest(CreateOperationOutcome(
                "error",
                "required",
                "Missing required parameter: patient"));
        }

        // Create query
        var query = new DocRefQuery(
            TenantId: tenantId,
            PatientId: patient,
            Start: start,
            End: end,
            DocumentType: type,
            OnDemand: onDemand ?? false);

        // Execute query
        var result = await mediator.SendAsync(query, cancellationToken);

        // Return Bundle
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleDocRefAgnosticAsync(
        HttpContext context,
        [FromQuery] string patient,
        [FromQuery] DateTimeOffset? start,
        [FromQuery] DateTimeOffset? end,
        [FromQuery] string? type,
        [FromQuery] bool? onDemand,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Extract tenant from context
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "error",
                "required",
                "TenantId not found. Use /tenant/{tenantId}/DocumentReference/$docref"));
        }

        // Delegate to tenant-explicit handler logic
        var query = new DocRefQuery(tenantId, patient, start, end, type, onDemand ?? false);
        var result = await mediator.SendAsync(query, cancellationToken);
        return Results.Ok(result);
    }

    private static OperationOutcomeJsonNode CreateOperationOutcome(
        string severity,
        string code,
        string diagnostics)
    {
        // Create FHIR OperationOutcome
        var outcome = new OperationOutcomeJsonNode();
        outcome.AddIssue(severity, code, diagnostics);
        return outcome;
    }
}
```

**Query/Handler Implementation**:

```csharp
// File: src/Ignixa.Application.Operations/Features/DocRef/DocRefQuery.cs
public record DocRefQuery(
    int TenantId,
    string PatientId,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? DocumentType,
    bool OnDemand
) : IRequest<BundleJsonNode>;

// File: src/Ignixa.Application.Operations/Features/DocRef/DocRefHandler.cs
public class DocRefHandler : IRequestHandler<DocRefQuery, BundleJsonNode>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ILogger<DocRefHandler> _logger;

    public DocRefHandler(
        IFhirRepositoryFactory repositoryFactory,
        ILogger<DocRefHandler> logger)
    {
        _repositoryFactory = repositoryFactory;
        _logger = logger;
    }

    public async Task<BundleJsonNode> HandleAsync(
        DocRefQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing $docref for patient {PatientId} in tenant {TenantId}",
            request.PatientId,
            request.TenantId);

        var repository = await _repositoryFactory.GetRepositoryAsync(
            request.TenantId,
            cancellationToken);

        // Build search parameters
        var searchParams = new List<(string, string)>
        {
            ("patient", request.PatientId),
            ("_sort", "-date") // Most recent first
        };

        // Add date range filters
        if (request.Start.HasValue || request.End.HasValue)
        {
            var dateFilter = BuildDateFilter(request.Start, request.End);
            searchParams.Add(("date", dateFilter));
        }
        else
        {
            // No date range: return most recent document
            searchParams.Add(("_count", "1"));
        }

        // Add document type filter
        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            searchParams.Add(("type", request.DocumentType));
        }
        else
        {
            // Default: include CCD (34133-9)
            searchParams.Add(("type", "34133-9"));
        }

        // Add on-demand filter
        if (!request.OnDemand)
        {
            // Exclude on-demand documents
            searchParams.Add(("status", "current"));
        }

        // Execute search using existing search infrastructure
        var searchResult = await repository.SearchAsync(
            "DocumentReference",
            searchParams,
            cancellationToken);

        // Convert to Bundle
        var bundle = new BundleJsonNode
        {
            Type = "searchset",
            Total = searchResult.Total
        };

        foreach (var resource in searchResult.Resources)
        {
            bundle.AddEntry(resource);
        }

        _logger.LogInformation(
            "$docref returned {Count} documents for patient {PatientId}",
            searchResult.Resources.Count,
            request.PatientId);

        return bundle;
    }

    private static string BuildDateFilter(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start.HasValue && end.HasValue)
        {
            // ge{start}&date=le{end} → between start and end
            return $"ge{start.Value:yyyy-MM-ddTHH:mm:sszzz}";
        }
        else if (start.HasValue)
        {
            // ge{start} → after start
            return $"ge{start.Value:yyyy-MM-ddTHH:mm:sszzz}";
        }
        else if (end.HasValue)
        {
            // le{end} → before end
            return $"le{end.Value:yyyy-MM-ddTHH:mm:sszzz}";
        }

        return string.Empty;
    }
}
```

**Feature Registration**:

```csharp
// File: src/Ignixa.Application.Operations/Features/DocRef/DocumentReferenceFeature.cs
public class DocumentReferenceFeature : IPackageFeature
{
    public string PackageId => "hl7.fhir.core";

    public IReadOnlyList<string> SystemOperations => Array.Empty<string>();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>
        {
            { "DocumentReference", new[] { "docref" } }
        };

    public IReadOnlyList<string>? SupportedFhirVersions => null; // All versions
}
```

**Registration** (`Program.cs`):
```csharp
// Endpoints
app.MapDocumentEndpoints();

// Feature
builder.RegisterType<DocumentReferenceFeature>()
    .As<IPackageFeature>()
    .SingleInstance();

// Handler
builder.RegisterType<DocRefHandler>()
    .As<IRequestHandler<DocRefQuery, BundleJsonNode>>();
```

#### Testing Strategy

**Unit Tests**:
```csharp
[Fact]
public async Task DocRef_WithPatientId_ReturnsDocuments()
{
    // Arrange
    var query = new DocRefQuery(
        TenantId: 1,
        PatientId: "patient-123",
        Start: null,
        End: null,
        DocumentType: null,
        OnDemand: false);

    // Act
    var result = await _handler.HandleAsync(query, CancellationToken.None);

    // Assert
    result.Type.Should().Be("searchset");
    result.Entry.Should().NotBeEmpty();
}

[Fact]
public async Task DocRef_WithDateRange_FiltersDocuments()
{
    // Arrange
    var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    var end = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);

    var query = new DocRefQuery(1, "patient-123", start, end, null, false);

    // Act
    var result = await _handler.HandleAsync(query, CancellationToken.None);

    // Assert
    // Verify date filter was applied
}

[Fact]
public async Task DocRef_WithCCDType_ReturnsOnlyCCD()
{
    // Arrange
    var query = new DocRefQuery(1, "patient-123", null, null, "34133-9", false);

    // Act
    var result = await _handler.HandleAsync(query, CancellationToken.None);

    // Assert
    result.Entry.Should().AllSatisfy(entry =>
    {
        // Verify all documents are CCD type
    });
}
```

**Integration Tests**:
```csharp
[Fact]
public async Task DocRefEndpoint_ReturnsHttpOk()
{
    // Arrange
    var client = _factory.CreateClient();

    // Act
    var response = await client.GetAsync(
        "/tenant/1/DocumentReference/$docref?patient=patient-123");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var content = await response.Content.ReadAsStringAsync();
    var bundle = JsonNode.Parse(content);
    bundle["resourceType"].GetValue<string>().Should().Be("Bundle");
}
```

---

### 2. $member-match - Member Identity Matching

**Canonical**: `http://hl7.org/fhir/us/davinci-hrex/OperationDefinition/member-match`
**Type**: Type-level (Patient)
**Priority**: **P1** (Da Vinci HRex requirement for payers)
**Effort**: 40-50 hours

#### FHIR Specification

**Invocation**:
```
POST [base]/Patient/$member-match
```

**Input Parameters** (Parameters resource):

| Parameter | Card | Type | Description |
|-----------|------|------|-------------|
| MemberPatient | 1..1 | Patient | US Core Patient with demographics |
| Consent | 0..1 | Consent | Permission to access patient info |
| CoverageToMatch | 1..1 | Coverage | Member's coverage from health plan card |
| CoverageToLink | 0..1 | Coverage | Requesting payer's coverage (mustSupport for payers) |

**Output Parameters** (Parameters resource):

| Parameter | Card | Type | Description |
|-----------|------|------|-------------|
| MemberIdentifier | 1..1 | Identifier | Unique member ID from responding payer |
| MemberId | 0..1 | Reference(Patient) | RESTful reference to patient record |

**Status Codes**:
- **200 OK**: Successful match (exactly one result)
- **422 Unprocessable Entity**: No match or multiple matches

**Use Cases**:
- Payer-to-payer member identification for data exchange
- Coverage transition (member switching health plans)
- Duplicate patient record detection

#### Implementation Design

**File Structure**:
```
src/Ignixa.Api/Endpoints/PatientOperationsEndpoints.cs (new)
src/Ignixa.Application.Operations/Features/MemberMatch/
  ├── MemberMatchCommand.cs
  ├── MemberMatchHandler.cs
  ├── IPatientMatchingService.cs
  └── DemographicMatchingService.cs
src/Ignixa.Application.Operations/Features/MemberMatch/Models/
  ├── MemberMatchInput.cs
  └── MemberMatchOutput.cs
```

**Endpoint Implementation**:

```csharp
// File: src/Ignixa.Api/Endpoints/PatientOperationsEndpoints.cs
public static class PatientOperationsEndpoints
{
    public static IEndpointRouteBuilder MapPatientOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        // POST /tenant/{tenantId}/Patient/$member-match
        endpoints.MapPost("/tenant/{tenantId:int}/Patient/$member-match", HandleMemberMatchAsync)
            .WithName("MemberMatch")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<OperationOutcomeJsonNode>(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> HandleMemberMatchAsync(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // Read request body
        using var memoryStream = memoryStreamManager.GetStream();
        await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        if (memoryStream.Length == 0)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "error",
                "required",
                "Request body must contain a Parameters resource"));
        }

        // Parse Parameters resource
        ResourceJsonNode parametersNode;
        try
        {
            parametersNode = await JsonSourceNodeFactory.Parse(memoryStream);

            if (parametersNode.ResourceType != "Parameters")
            {
                return Results.BadRequest(CreateOperationOutcome(
                    "error",
                    "invalid",
                    "Request body must be a Parameters resource"));
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "error",
                "invalid",
                $"Invalid JSON: {ex.Message}"));
        }

        // Extract parameters
        var parameters = parametersNode.As<ParametersJsonNode>();

        ResourceJsonNode? memberPatient = null;
        ResourceJsonNode? consent = null;
        ResourceJsonNode? coverageToMatch = null;
        ResourceJsonNode? coverageToLink = null;

        foreach (var param in parameters.Parameter)
        {
            switch (param.Name)
            {
                case "MemberPatient":
                    memberPatient = param.GetValue("resource") as ResourceJsonNode;
                    break;
                case "Consent":
                    consent = param.GetValue("resource") as ResourceJsonNode;
                    break;
                case "CoverageToMatch":
                    coverageToMatch = param.GetValue("resource") as ResourceJsonNode;
                    break;
                case "CoverageToLink":
                    coverageToLink = param.GetValue("resource") as ResourceJsonNode;
                    break;
            }
        }

        // Validate required parameters
        if (memberPatient == null)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "error",
                "required",
                "Missing required parameter: MemberPatient"));
        }

        if (coverageToMatch == null)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "error",
                "required",
                "Missing required parameter: CoverageToMatch"));
        }

        // Create command
        var command = new MemberMatchCommand(
            TenantId: tenantId,
            MemberPatient: memberPatient,
            Consent: consent,
            CoverageToMatch: coverageToMatch,
            CoverageToLink: coverageToLink);

        // Execute matching
        try
        {
            var result = await mediator.SendAsync(command, cancellationToken);

            // Build Parameters response
            var responseParams = new ParametersJsonNode();

            // Add MemberIdentifier
            var identifierParam = responseParams.AddParameter("MemberIdentifier");
            identifierParam.SetValue("valueIdentifier", result.MemberIdentifier);

            // Add MemberId (optional)
            if (result.MemberId != null)
            {
                var idParam = responseParams.AddParameter("MemberId");
                idParam.SetValue("valueReference", result.MemberId);
            }

            return Results.Ok(responseParams);
        }
        catch (PatientMatchException ex) when (ex.MatchCount == 0)
        {
            // No match found
            return Results.UnprocessableEntity(CreateOperationOutcome(
                "error",
                "not-found",
                "No matching patient found"));
        }
        catch (PatientMatchException ex) when (ex.MatchCount > 1)
        {
            // Multiple matches found
            return Results.UnprocessableEntity(CreateOperationOutcome(
                "error",
                "multiple-matches",
                $"Multiple matching patients found: {ex.MatchCount}"));
        }
    }

    private static OperationOutcomeJsonNode CreateOperationOutcome(
        string severity,
        string code,
        string diagnostics)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.AddIssue(severity, code, diagnostics);
        return outcome;
    }
}
```

**Command/Handler Implementation**:

```csharp
// File: src/Ignixa.Application.Operations/Features/MemberMatch/MemberMatchCommand.cs
public record MemberMatchCommand(
    int TenantId,
    ResourceJsonNode MemberPatient,
    ResourceJsonNode? Consent,
    ResourceJsonNode CoverageToMatch,
    ResourceJsonNode? CoverageToLink
) : IRequest<MemberMatchOutput>;

// File: src/Ignixa.Application.Operations/Features/MemberMatch/Models/MemberMatchOutput.cs
public record MemberMatchOutput(
    JsonNode MemberIdentifier,
    JsonNode? MemberId
);

// File: src/Ignixa.Application.Operations/Features/MemberMatch/MemberMatchHandler.cs
public class MemberMatchHandler : IRequestHandler<MemberMatchCommand, MemberMatchOutput>
{
    private readonly IPatientMatchingService _matchingService;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ILogger<MemberMatchHandler> _logger;

    public MemberMatchHandler(
        IPatientMatchingService matchingService,
        IFhirRepositoryFactory repositoryFactory,
        ILogger<MemberMatchHandler> logger)
    {
        _matchingService = matchingService;
        _repositoryFactory = repositoryFactory;
        _logger = logger;
    }

    public async Task<MemberMatchOutput> HandleAsync(
        MemberMatchCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing $member-match in tenant {TenantId}",
            request.TenantId);

        // Extract matching criteria from Coverage
        var memberIdentifier = ExtractMemberIdentifier(request.CoverageToMatch);
        var subscriberId = ExtractSubscriberId(request.CoverageToMatch);

        // Extract demographics from Patient
        var demographics = ExtractDemographics(request.MemberPatient);

        // Search for matching patient
        var repository = await _repositoryFactory.GetRepositoryAsync(
            request.TenantId,
            cancellationToken);

        // Step 1: Try exact match on member identifier
        var matches = await _matchingService.FindByMemberIdentifierAsync(
            repository,
            memberIdentifier,
            subscriberId,
            cancellationToken);

        // Step 2: If no exact match, try demographic matching
        if (matches.Count == 0)
        {
            matches = await _matchingService.FindByDemographicsAsync(
                repository,
                demographics,
                cancellationToken);
        }

        // Validate match count
        if (matches.Count == 0)
        {
            _logger.LogWarning("No matching patient found for member identifier {MemberId}",
                memberIdentifier);
            throw new PatientMatchException("No match found", 0);
        }

        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "Multiple matching patients found: {Count}",
                matches.Count);
            throw new PatientMatchException("Multiple matches found", matches.Count);
        }

        var matchedPatient = matches[0];

        // Build response
        var memberIdNode = JsonNode.Parse($@"{{
            ""system"": ""http://example.org/member-id"",
            ""value"": ""{matchedPatient.Id}""
        }}");

        var memberRefNode = JsonNode.Parse($@"{{
            ""reference"": ""Patient/{matchedPatient.Id}""
        }}");

        _logger.LogInformation(
            "Successfully matched patient {PatientId}",
            matchedPatient.Id);

        return new MemberMatchOutput(
            MemberIdentifier: memberIdNode,
            MemberId: memberRefNode);
    }

    private static string ExtractMemberIdentifier(ResourceJsonNode coverage)
    {
        // Extract identifier from Coverage.identifier[]
        // TODO: Implement based on Coverage structure
        return string.Empty;
    }

    private static string ExtractSubscriberId(ResourceJsonNode coverage)
    {
        // Extract subscriber ID from Coverage.subscriberId
        // TODO: Implement based on Coverage structure
        return string.Empty;
    }

    private static PatientDemographics ExtractDemographics(ResourceJsonNode patient)
    {
        // Extract demographics from Patient resource
        // TODO: Implement extraction logic
        return new PatientDemographics();
    }
}

// File: src/Ignixa.Application.Operations/Features/MemberMatch/PatientMatchException.cs
public class PatientMatchException : Exception
{
    public int MatchCount { get; }

    public PatientMatchException(string message, int matchCount)
        : base(message)
    {
        MatchCount = matchCount;
    }
}
```

**Matching Service**:

```csharp
// File: src/Ignixa.Application.Operations/Features/MemberMatch/IPatientMatchingService.cs
public interface IPatientMatchingService
{
    Task<List<ResourceWrapper>> FindByMemberIdentifierAsync(
        IFhirRepository repository,
        string memberIdentifier,
        string? subscriberId,
        CancellationToken cancellationToken);

    Task<List<ResourceWrapper>> FindByDemographicsAsync(
        IFhirRepository repository,
        PatientDemographics demographics,
        CancellationToken cancellationToken);
}

// File: src/Ignixa.Application.Operations/Features/MemberMatch/DemographicMatchingService.cs
public class DemographicMatchingService : IPatientMatchingService
{
    private readonly ILogger<DemographicMatchingService> _logger;

    public DemographicMatchingService(ILogger<DemographicMatchingService> logger)
    {
        _logger = logger;
    }

    public async Task<List<ResourceWrapper>> FindByMemberIdentifierAsync(
        IFhirRepository repository,
        string memberIdentifier,
        string? subscriberId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Searching for patient by member identifier: {MemberId}",
            memberIdentifier);

        // Search Patient by identifier
        var searchParams = new List<(string, string)>
        {
            ("identifier", memberIdentifier)
        };

        if (!string.IsNullOrEmpty(subscriberId))
        {
            // Add subscriber ID to search
            // TODO: Implement subscriber ID search parameter
        }

        var searchResult = await repository.SearchAsync(
            "Patient",
            searchParams,
            cancellationToken);

        return searchResult.Resources;
    }

    public async Task<List<ResourceWrapper>> FindByDemographicsAsync(
        IFhirRepository repository,
        PatientDemographics demographics,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Searching for patient by demographics: {Name}, {BirthDate}",
            demographics.Name,
            demographics.BirthDate);

        // Build search parameters from demographics
        var searchParams = new List<(string, string)>();

        if (!string.IsNullOrEmpty(demographics.Name))
        {
            searchParams.Add(("name", demographics.Name));
        }

        if (demographics.BirthDate.HasValue)
        {
            searchParams.Add(("birthdate", demographics.BirthDate.Value.ToString("yyyy-MM-dd")));
        }

        if (!string.IsNullOrEmpty(demographics.Gender))
        {
            searchParams.Add(("gender", demographics.Gender));
        }

        // Execute search
        var searchResult = await repository.SearchAsync(
            "Patient",
            searchParams,
            cancellationToken);

        // Apply matching algorithm
        var scoredMatches = ScoreMatches(searchResult.Resources, demographics);

        // Return only high-confidence matches (score >= 0.8)
        return scoredMatches
            .Where(m => m.Score >= 0.8)
            .Select(m => m.Resource)
            .ToList();
    }

    private List<(ResourceWrapper Resource, double Score)> ScoreMatches(
        List<ResourceWrapper> candidates,
        PatientDemographics demographics)
    {
        // Implement probabilistic matching algorithm
        // Score based on:
        // - Name similarity (Levenshtein distance)
        // - Birth date match (exact)
        // - Gender match (exact)
        // - Address similarity
        // - Phone number match

        // TODO: Implement scoring logic
        return candidates.Select(c => (c, 1.0)).ToList();
    }
}

public record PatientDemographics
{
    public string? Name { get; init; }
    public DateTimeOffset? BirthDate { get; init; }
    public string? Gender { get; init; }
    public string? Address { get; init; }
    public string? Phone { get; init; }
}
```

#### Testing Strategy

**Unit Tests**:
```csharp
[Fact]
public async Task MemberMatch_WithExactIdentifier_ReturnsMatch()
{
    // Arrange
    var patient = CreateTestPatient("patient-123");
    var coverage = CreateTestCoverage("member-456");

    var command = new MemberMatchCommand(1, patient, null, coverage, null);

    // Act
    var result = await _handler.HandleAsync(command, CancellationToken.None);

    // Assert
    result.MemberIdentifier.Should().NotBeNull();
    result.MemberId.Should().NotBeNull();
}

[Fact]
public async Task MemberMatch_NoMatch_ThrowsException()
{
    // Arrange
    var patient = CreateTestPatient("unknown-patient");
    var coverage = CreateTestCoverage("unknown-member");

    var command = new MemberMatchCommand(1, patient, null, coverage, null);

    // Act & Assert
    await Assert.ThrowsAsync<PatientMatchException>(() =>
        _handler.HandleAsync(command, CancellationToken.None));
}

[Fact]
public async Task MemberMatch_MultipleMatches_ThrowsException()
{
    // Arrange - set up multiple patients with same demographics
    var patient = CreateTestPatient("common-name");
    var coverage = CreateTestCoverage("ambiguous-member");

    var command = new MemberMatchCommand(1, patient, null, coverage, null);

    // Act & Assert
    var ex = await Assert.ThrowsAsync<PatientMatchException>(() =>
        _handler.HandleAsync(command, CancellationToken.None));

    ex.MatchCount.Should().BeGreaterThan(1);
}
```

---

### 3. $submit-attachment - Clinical Attachment Submission

**Canonical**: `http://hl7.org/fhir/us/davinci-cdex/OperationDefinition/submit-attachment`
**Type**: Type-level (typically invoked on server root)
**Priority**: **P1** (Da Vinci CDex requirement)
**Effort**: 50-70 hours

#### FHIR Specification

**Invocation**:
```
POST [base]/$submit-attachment
```

**Input Parameters** (Parameters resource):

| Parameter | Card | Type | Description |
|-----------|------|------|-------------|
| TrackingId | 1..1 | Identifier | Links attachment to claim/prior auth |
| AdminRefNumber | 0..1 | Identifier | Optional payer-assigned identifier |
| AttachTo | 1..1 | code | "claim" or "preauthorization" |
| PayerId | 0..1 | Identifier | Receiving payer identifier |
| OrganizationId | 0..1 | Identifier | Sending organization (Type 2 NPI) |
| ProviderId | 0..1 | Identifier | Sending provider (Type 1 NPI) |
| MemberId | 1..1 | Identifier | Patient member ID |
| ServiceDate | 0..1 | dateTime | Required for claims |
| Attachment | 1..* | complex | Clinical/administrative attachments |
| Attachment.LineItem | 0..* | string | Line item reference |
| Attachment.Code | 0..1 | CodeableConcept | LOINC or PWK01 code |
| Attachment.Content | 1..1 | Resource | DocumentReference or Binary |
| Final | 0..1 | boolean | Last submission flag (default true) |

**Output**: HTTP status codes (no body)
- **200 OK**: Attachment accepted
- **400 Bad Request**: Invalid parameters
- **422 Unprocessable Entity**: Business rule violation

**Use Cases**:
- **Solicited**: Response to payer attachment request
- **Unsolicited**: Proactive submission with claim

#### Implementation Design

**File Structure**:
```
src/Ignixa.Api/Endpoints/AttachmentEndpoints.cs (new)
src/Ignixa.Application.Operations/Features/SubmitAttachment/
  ├── SubmitAttachmentCommand.cs
  ├── SubmitAttachmentHandler.cs
  └── AttachmentStorageService.cs
src/Ignixa.Application.Operations/Features/SubmitAttachment/Models/
  ├── AttachmentSubmission.cs
  └── AttachmentMetadata.cs
src/Ignixa.Application.BackgroundOperations/SubmitAttachment/
  ├── ProcessAttachmentOrchestration.cs (DurableTask)
  └── StoreAttachmentActivity.cs
```

**Endpoint Implementation**:

```csharp
// File: src/Ignixa.Api/Endpoints/AttachmentEndpoints.cs
public static class AttachmentEndpoints
{
    public static IEndpointRouteBuilder MapAttachmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        // POST /tenant/{tenantId}/$submit-attachment
        endpoints.MapPost("/tenant/{tenantId:int}/$submit-attachment", HandleSubmitAttachmentAsync)
            .WithName("SubmitAttachment")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status200OK)
            .Produces<OperationOutcomeJsonNode>(StatusCodes.Status400BadRequest);

        // GET /tenant/{tenantId}/_attachment/{attachmentId} - Poll status (async)
        endpoints.MapGet("/tenant/{tenantId:int}/_attachment/{attachmentId}", GetAttachmentStatusAsync)
            .WithName("GetAttachmentStatus")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<object>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleSubmitAttachmentAsync(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // Read and parse Parameters resource
        using var memoryStream = memoryStreamManager.GetStream();
        await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        if (memoryStream.Length == 0)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "error",
                "required",
                "Request body must contain a Parameters resource"));
        }

        ResourceJsonNode parametersNode;
        try
        {
            parametersNode = await JsonSourceNodeFactory.Parse(memoryStream);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "error",
                "invalid",
                $"Invalid JSON: {ex.Message}"));
        }

        // Extract parameters
        var submission = ParseAttachmentSubmission(parametersNode);

        // Validate required fields
        var validationError = ValidateSubmission(submission);
        if (validationError != null)
        {
            return Results.BadRequest(validationError);
        }

        // For large attachments, use async processing
        if (submission.Attachments.Sum(a => EstimateSize(a)) > 10 * 1024 * 1024) // 10 MB
        {
            // Start DurableTask orchestration
            var attachmentId = Guid.NewGuid().ToString("N");

            var orchestrationInput = new ProcessAttachmentInput
            {
                AttachmentId = attachmentId,
                TenantId = tenantId,
                Submission = submission
            };

            await taskHubClient.CreateOrchestrationInstanceAsync(
                typeof(ProcessAttachmentOrchestration),
                orchestrationInput);

            // Return 202 Accepted with status URL
            var statusUrl = $"/tenant/{tenantId}/_attachment/{attachmentId}";
            context.Response.Headers["Content-Location"] = statusUrl;

            return Results.Accepted(statusUrl);
        }
        else
        {
            // Process synchronously
            var command = new SubmitAttachmentCommand(tenantId, submission);
            await mediator.SendAsync(command, cancellationToken);

            return Results.Ok();
        }
    }

    private static AttachmentSubmission ParseAttachmentSubmission(
        ResourceJsonNode parametersNode)
    {
        var parameters = parametersNode.As<ParametersJsonNode>();

        var submission = new AttachmentSubmission();

        foreach (var param in parameters.Parameter)
        {
            switch (param.Name)
            {
                case "TrackingId":
                    submission.TrackingId = param.GetValueAs<JsonNode>("valueIdentifier");
                    break;
                case "AttachTo":
                    submission.AttachTo = param.GetValueAs<string>("valueCode");
                    break;
                case "MemberId":
                    submission.MemberId = param.GetValueAs<JsonNode>("valueIdentifier");
                    break;
                case "ServiceDate":
                    submission.ServiceDate = param.GetValueAs<DateTimeOffset?>("valueDateTime");
                    break;
                case "Attachment":
                    var attachment = ParseAttachment(param);
                    submission.Attachments.Add(attachment);
                    break;
                // ... other parameters
            }
        }

        return submission;
    }

    private static AttachmentItem ParseAttachment(ParameterJsonNode param)
    {
        var attachment = new AttachmentItem();

        foreach (var part in param.Part)
        {
            switch (part.Name)
            {
                case "LineItem":
                    attachment.LineItem = part.GetValueAs<string>("valueString");
                    break;
                case "Code":
                    attachment.Code = part.GetValueAs<JsonNode>("valueCodeableConcept");
                    break;
                case "Content":
                    attachment.Content = part.GetValue("resource") as ResourceJsonNode;
                    break;
            }
        }

        return attachment;
    }

    private static OperationOutcomeJsonNode? ValidateSubmission(
        AttachmentSubmission submission)
    {
        if (submission.TrackingId == null)
        {
            return CreateOperationOutcome("error", "required", "Missing TrackingId");
        }

        if (string.IsNullOrEmpty(submission.AttachTo))
        {
            return CreateOperationOutcome("error", "required", "Missing AttachTo");
        }

        if (submission.AttachTo == "claim" && submission.ServiceDate == null)
        {
            return CreateOperationOutcome("error", "required",
                "ServiceDate is required for claims");
        }

        if (submission.Attachments.Count == 0)
        {
            return CreateOperationOutcome("error", "required", "At least one attachment required");
        }

        return null;
    }

    private static long EstimateSize(AttachmentItem attachment)
    {
        // Estimate attachment size for async decision
        // TODO: Implement size estimation
        return 0;
    }

    private static OperationOutcomeJsonNode CreateOperationOutcome(
        string severity,
        string code,
        string diagnostics)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.AddIssue(severity, code, diagnostics);
        return outcome;
    }
}
```

**Models**:

```csharp
// File: src/Ignixa.Application.Operations/Features/SubmitAttachment/Models/AttachmentSubmission.cs
public class AttachmentSubmission
{
    public JsonNode? TrackingId { get; set; }
    public JsonNode? AdminRefNumber { get; set; }
    public string? AttachTo { get; set; } // "claim" or "preauthorization"
    public JsonNode? PayerId { get; set; }
    public JsonNode? OrganizationId { get; set; }
    public JsonNode? ProviderId { get; set; }
    public JsonNode? MemberId { get; set; }
    public DateTimeOffset? ServiceDate { get; set; }
    public List<AttachmentItem> Attachments { get; set; } = new();
    public bool Final { get; set; } = true;
}

public class AttachmentItem
{
    public string? LineItem { get; set; }
    public JsonNode? Code { get; set; }
    public ResourceJsonNode? Content { get; set; }
}
```

**DurableTask Orchestration** (for large attachments):

```csharp
// File: src/Ignixa.Application.BackgroundOperations/SubmitAttachment/ProcessAttachmentOrchestration.cs
public class ProcessAttachmentOrchestration
    : TaskOrchestration<ProcessAttachmentOutput, ProcessAttachmentInput>
{
    public override async Task<ProcessAttachmentOutput> RunTask(
        OrchestrationContext context,
        ProcessAttachmentInput input)
    {
        // Step 1: Validate and store metadata
        var metadata = await context.ScheduleTask<AttachmentMetadata>(
            typeof(ValidateAttachmentActivity),
            input);

        // Step 2: Store each attachment to blob storage
        var storedUrls = new List<string>();

        foreach (var attachment in input.Submission.Attachments)
        {
            var storeInput = new StoreAttachmentInput
            {
                AttachmentId = input.AttachmentId,
                TenantId = input.TenantId,
                Attachment = attachment
            };

            var url = await context.ScheduleTask<string>(
                typeof(StoreAttachmentActivity),
                storeInput);

            storedUrls.Add(url);
        }

        // Step 3: Create attachment record
        var completeInput = new CompleteAttachmentInput
        {
            AttachmentId = input.AttachmentId,
            TenantId = input.TenantId,
            Metadata = metadata,
            StoredUrls = storedUrls
        };

        await context.ScheduleTask<bool>(
            typeof(CompleteAttachmentActivity),
            completeInput);

        return new ProcessAttachmentOutput
        {
            AttachmentId = input.AttachmentId,
            Status = "Completed",
            StoredUrls = storedUrls
        };
    }
}

public record ProcessAttachmentInput
{
    public required string AttachmentId { get; init; }
    public required int TenantId { get; init; }
    public required AttachmentSubmission Submission { get; init; }
}

public record ProcessAttachmentOutput
{
    public required string AttachmentId { get; init; }
    public required string Status { get; init; }
    public required List<string> StoredUrls { get; init; }
}
```

**Activity**:

```csharp
// File: src/Ignixa.Application.BackgroundOperations/SubmitAttachment/StoreAttachmentActivity.cs
public class StoreAttachmentActivity : AsyncTaskActivity<StoreAttachmentInput, string>
{
    private readonly IBlobStorageClient _blobStorage;
    private readonly ILogger<StoreAttachmentActivity> _logger;

    public StoreAttachmentActivity(
        IBlobStorageClient blobStorage,
        ILogger<StoreAttachmentActivity> logger)
    {
        _blobStorage = blobStorage;
        _logger = logger;
    }

    protected override async Task<string> ExecuteAsync(
        TaskContext context,
        StoreAttachmentInput input)
    {
        _logger.LogInformation(
            "Storing attachment for {AttachmentId}",
            input.AttachmentId);

        // Convert attachment content to bytes
        byte[] contentBytes;

        if (input.Attachment.Content?.ResourceType == "Binary")
        {
            // Extract binary data
            var binaryData = input.Attachment.Content.MutableNode["data"]?
                .GetValue<string>();

            contentBytes = Convert.FromBase64String(binaryData ?? string.Empty);
        }
        else if (input.Attachment.Content?.ResourceType == "DocumentReference")
        {
            // Extract document from DocumentReference.content[0].attachment.data
            var attachmentData = input.Attachment.Content
                .MutableNode["content"]?[0]?
                ["attachment"]?["data"]?
                .GetValue<string>();

            contentBytes = Convert.FromBase64String(attachmentData ?? string.Empty);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported content type: {input.Attachment.Content?.ResourceType}");
        }

        // Generate blob path
        var blobPath = $"attachments/{input.TenantId}/{input.AttachmentId}/{Guid.NewGuid()}.bin";

        // Upload to blob storage
        await _blobStorage.UploadBlobAsync(blobPath, contentBytes, CancellationToken.None);

        // Generate URL
        var url = await _blobStorage.GetBlobUrlAsync(
            blobPath,
            TimeSpan.FromDays(365), // 1 year expiration
            CancellationToken.None);

        _logger.LogInformation(
            "Stored attachment at {Url}",
            url);

        return url;
    }
}

public record StoreAttachmentInput
{
    public required string AttachmentId { get; init; }
    public required int TenantId { get; init; }
    public required AttachmentItem Attachment { get; init; }
}
```

#### Testing Strategy

```csharp
[Fact]
public async Task SubmitAttachment_WithValidClaim_ReturnsOk()
{
    // Arrange
    var submission = new AttachmentSubmission
    {
        TrackingId = CreateTrackingId(),
        AttachTo = "claim",
        MemberId = CreateMemberId(),
        ServiceDate = DateTimeOffset.UtcNow,
        Attachments = new List<AttachmentItem>
        {
            new() { Content = CreateBinary() }
        }
    };

    var command = new SubmitAttachmentCommand(1, submission);

    // Act
    await _handler.HandleAsync(command, CancellationToken.None);

    // Assert - verify stored in blob storage
}

[Fact]
public async Task SubmitAttachment_MissingServiceDate_ReturnsBadRequest()
{
    // Arrange
    var submission = new AttachmentSubmission
    {
        AttachTo = "claim",
        ServiceDate = null // Missing for claim
    };

    // Act & Assert
    var result = await _endpoint.HandleAsync(submission);
    result.Should().BeOfType<BadRequestResult>();
}
```

---

### 4. $questionnaire-package - Prior Auth Questionnaires

**Canonical**: `http://hl7.org/fhir/us/davinci-dtr/OperationDefinition/questionnaire-package`
**Type**: Type-level (Questionnaire)
**Priority**: **P1** (Da Vinci DTR requirement)
**Effort**: 40-55 hours

#### FHIR Specification

**Invocation**:
```
POST [base]/Questionnaire/$questionnaire-package
```

**Input Parameters**:

| Parameter | Card | Type | Description |
|-----------|------|------|-------------|
| coverage | 0..* | Coverage | Member and coverage for documentation |
| order | 0..* | Resource | Order resources (ServiceRequest, etc.) |
| referenced | 0..* | Resource | Supporting resources (Patient, etc.) |
| questionnaire | 0..* | canonical | Questionnaire URL(s) to return |
| context | 0..1 | string | CRD/CDex context ID |
| changedsince | 0..1 | dateTime | Return only changed artifacts |

**Output Parameters**:

| Parameter | Card | Type | Description |
|-----------|------|------|-------------|
| PackageBundle | 0..* | Bundle | Collection Bundle with Questionnaire + dependencies |
| outcome | 0..1 | OperationOutcome | Warnings/info messages |

**Bundle Contents**:
- Questionnaire resource
- Library resources (CQL, ELM)
- ValueSet resources (expanded)
- QuestionnaireResponse (draft, pre-populated)

#### Implementation Design

**File Structure**:
```
src/Ignixa.Api/Endpoints/QuestionnaireEndpoints.cs (new)
src/Ignixa.Application.Operations/Features/QuestionnairePackage/
  ├── QuestionnairePackageQuery.cs
  ├── QuestionnairePackageHandler.cs
  └── QuestionnairePackageBuilder.cs
```

**Endpoint Implementation**:

```csharp
// File: src/Ignixa.Api/Endpoints/QuestionnaireEndpoints.cs
public static class QuestionnaireEndpoints
{
    public static IEndpointRouteBuilder MapQuestionnaireEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        // POST /tenant/{tenantId}/Questionnaire/$questionnaire-package
        endpoints.MapPost(
            "/tenant/{tenantId:int}/Questionnaire/$questionnaire-package",
            HandleQuestionnairePackageAsync)
            .WithName("QuestionnairePackage")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson);

        return endpoints;
    }

    private static async Task<IResult> HandleQuestionnairePackageAsync(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // Parse Parameters resource
        using var memoryStream = memoryStreamManager.GetStream();
        await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        ResourceJsonNode parametersNode = await JsonSourceNodeFactory.Parse(memoryStream);
        var parameters = parametersNode.As<ParametersJsonNode>();

        // Extract parameters
        var coverageResources = new List<ResourceJsonNode>();
        var orderResources = new List<ResourceJsonNode>();
        var questionnaireCanonicals = new List<string>();
        string? contextId = null;
        DateTimeOffset? changedSince = null;

        foreach (var param in parameters.Parameter)
        {
            switch (param.Name)
            {
                case "coverage":
                    var coverage = param.GetValue("resource") as ResourceJsonNode;
                    if (coverage != null) coverageResources.Add(coverage);
                    break;
                case "order":
                    var order = param.GetValue("resource") as ResourceJsonNode;
                    if (order != null) orderResources.Add(order);
                    break;
                case "questionnaire":
                    var canonical = param.GetValueAs<string>("valueCanonical");
                    if (canonical != null) questionnaireCanonicals.Add(canonical);
                    break;
                case "context":
                    contextId = param.GetValueAs<string>("valueString");
                    break;
                case "changedsince":
                    changedSince = param.GetValueAs<DateTimeOffset?>("valueDateTime");
                    break;
            }
        }

        // Create query
        var query = new QuestionnairePackageQuery(
            TenantId: tenantId,
            CoverageResources: coverageResources,
            OrderResources: orderResources,
            QuestionnaireCanonicals: questionnaireCanonicals,
            ContextId: contextId,
            ChangedSince: changedSince);

        // Execute query
        var result = await mediator.SendAsync(query, cancellationToken);

        // Return Parameters with PackageBundle[]
        var response = new ParametersJsonNode();

        foreach (var bundle in result.PackageBundles)
        {
            var bundleParam = response.AddParameter("PackageBundle");
            bundleParam.SetValue("resource", bundle);
        }

        if (result.Outcome != null)
        {
            var outcomeParam = response.AddParameter("outcome");
            outcomeParam.SetValue("resource", result.Outcome);
        }

        return Results.Ok(response);
    }
}
```

**Query/Handler**:

```csharp
// File: src/Ignixa.Application.Operations/Features/QuestionnairePackage/QuestionnairePackageQuery.cs
public record QuestionnairePackageQuery(
    int TenantId,
    List<ResourceJsonNode> CoverageResources,
    List<ResourceJsonNode> OrderResources,
    List<string> QuestionnaireCanonicals,
    string? ContextId,
    DateTimeOffset? ChangedSince
) : IRequest<QuestionnairePackageResult>;

public record QuestionnairePackageResult(
    List<BundleJsonNode> PackageBundles,
    OperationOutcomeJsonNode? Outcome
);

// File: src/Ignixa.Application.Operations/Features/QuestionnairePackage/QuestionnairePackageHandler.cs
public class QuestionnairePackageHandler
    : IRequestHandler<QuestionnairePackageQuery, QuestionnairePackageResult>
{
    private readonly IPackageResourceRepository _packageRepository;
    private readonly QuestionnairePackageBuilder _packageBuilder;
    private readonly ILogger<QuestionnairePackageHandler> _logger;

    public QuestionnairePackageHandler(
        IPackageResourceRepository packageRepository,
        QuestionnairePackageBuilder packageBuilder,
        ILogger<QuestionnairePackageHandler> logger)
    {
        _packageRepository = packageRepository;
        _packageBuilder = packageBuilder;
        _logger = logger;
    }

    public async Task<QuestionnairePackageResult> HandleAsync(
        QuestionnairePackageQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Building questionnaire package for {Count} questionnaires",
            request.QuestionnaireCanonicals.Count);

        var bundles = new List<BundleJsonNode>();

        // If context provided, determine questionnaires from context
        List<string> canonicals;
        if (!string.IsNullOrEmpty(request.ContextId))
        {
            canonicals = await DetermineQuestionnairesFromContextAsync(
                request.ContextId,
                cancellationToken);
        }
        else
        {
            canonicals = request.QuestionnaireCanonicals;
        }

        // Build package for each questionnaire
        foreach (var canonical in canonicals)
        {
            var bundle = await _packageBuilder.BuildPackageAsync(
                canonical,
                request.CoverageResources,
                request.OrderResources,
                request.ChangedSince,
                cancellationToken);

            if (bundle != null)
            {
                bundles.Add(bundle);
            }
        }

        _logger.LogInformation(
            "Built {Count} questionnaire packages",
            bundles.Count);

        return new QuestionnairePackageResult(
            PackageBundles: bundles,
            Outcome: null);
    }

    private async Task<List<string>> DetermineQuestionnairesFromContextAsync(
        string contextId,
        CancellationToken cancellationToken)
    {
        // TODO: Query CRD/CDex context to determine which questionnaires are needed
        // For now, return empty list
        return new List<string>();
    }
}
```

**Package Builder**:

```csharp
// File: src/Ignixa.Application.Operations/Features/QuestionnairePackage/QuestionnairePackageBuilder.cs
public class QuestionnairePackageBuilder
{
    private readonly IPackageResourceRepository _packageRepository;
    private readonly ILogger<QuestionnairePackageBuilder> _logger;

    public QuestionnairePackageBuilder(
        IPackageResourceRepository packageRepository,
        ILogger<QuestionnairePackageBuilder> logger)
    {
        _packageRepository = packageRepository;
        _logger = logger;
    }

    public async Task<BundleJsonNode?> BuildPackageAsync(
        string questionnaireCanonical,
        List<ResourceJsonNode> coverageResources,
        List<ResourceJsonNode> orderResources,
        DateTimeOffset? changedSince,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Building package for questionnaire {Canonical}",
            questionnaireCanonical);

        // Step 1: Load Questionnaire from PackageResource table
        var questionnaire = await _packageRepository.GetByCanonicalAsync(
            questionnaireCanonical,
            cancellationToken);

        if (questionnaire == null)
        {
            _logger.LogWarning(
                "Questionnaire not found: {Canonical}",
                questionnaireCanonical);
            return null;
        }

        // Check changedsince filter
        if (changedSince.HasValue && questionnaire.UpdatedAt < changedSince.Value)
        {
            _logger.LogDebug(
                "Questionnaire not changed since {ChangedSince}",
                changedSince);
            return null;
        }

        // Step 2: Create collection Bundle
        var bundle = new BundleJsonNode
        {
            Type = "collection",
            Id = Guid.NewGuid().ToString("N")
        };

        // Step 3: Add Questionnaire resource
        bundle.AddEntry(questionnaire.ResourceJson);

        // Step 4: Find and add dependent Library resources
        var libraryCanonicals = ExtractLibraryReferences(questionnaire.ResourceJson);
        foreach (var libraryCanonical in libraryCanonicals)
        {
            var library = await _packageRepository.GetByCanonicalAsync(
                libraryCanonical,
                cancellationToken);

            if (library != null)
            {
                bundle.AddEntry(library.ResourceJson);
            }
        }

        // Step 5: Find and add ValueSet resources (expanded)
        var valueSetCanonicals = ExtractValueSetReferences(questionnaire.ResourceJson);
        foreach (var valueSetCanonical in valueSetCanonicals)
        {
            var valueSet = await _packageRepository.GetByCanonicalAsync(
                valueSetCanonical,
                cancellationToken);

            if (valueSet != null)
            {
                // Expand value set
                var expandedValueSet = await ExpandValueSetAsync(
                    valueSet.ResourceJson,
                    cancellationToken);

                bundle.AddEntry(expandedValueSet);
            }
        }

        // Step 6: Create pre-populated QuestionnaireResponse
        var questionnaireResponse = CreatePrePopulatedResponse(
            questionnaire.ResourceJson,
            coverageResources,
            orderResources);

        bundle.AddEntry(questionnaireResponse);

        _logger.LogDebug(
            "Built package with {Count} entries",
            bundle.Entry?.Count ?? 0);

        return bundle;
    }

    private List<string> ExtractLibraryReferences(ResourceJsonNode questionnaire)
    {
        // Extract Library references from Questionnaire.extension[]
        // where url = "http://hl7.org/fhir/StructureDefinition/cqf-library"
        var canonicals = new List<string>();

        // TODO: Implement extraction logic
        return canonicals;
    }

    private List<string> ExtractValueSetReferences(ResourceJsonNode questionnaire)
    {
        // Extract ValueSet references from Questionnaire.item[].answerValueSet
        var canonicals = new List<string>();

        // TODO: Implement extraction logic
        return canonicals;
    }

    private async Task<ResourceJsonNode> ExpandValueSetAsync(
        ResourceJsonNode valueSet,
        CancellationToken cancellationToken)
    {
        // Expand ValueSet to include all codes
        // TODO: Implement expansion (will be part of terminology operations)
        return valueSet;
    }

    private ResourceJsonNode CreatePrePopulatedResponse(
        ResourceJsonNode questionnaire,
        List<ResourceJsonNode> coverageResources,
        List<ResourceJsonNode> orderResources)
    {
        // Create QuestionnaireResponse with:
        // - status = "in-progress"
        // - questionnaire = canonical URL
        // - item[] pre-populated from coverage/order data

        var response = new ResourceJsonNode
        {
            ResourceType = "QuestionnaireResponse",
            Id = Guid.NewGuid().ToString("N")
        };

        response.MutableNode["status"] = JsonValue.Create("in-progress");
        response.MutableNode["questionnaire"] = JsonValue.Create(
            questionnaire.MutableNode["url"]?.GetValue<string>());

        // TODO: Implement pre-population logic based on CQL expressions

        return response;
    }
}
```

#### Testing Strategy

```csharp
[Fact]
public async Task QuestionnairePackage_WithCanonical_ReturnsBundle()
{
    // Arrange
    var query = new QuestionnairePackageQuery(
        TenantId: 1,
        CoverageResources: new(),
        OrderResources: new(),
        QuestionnaireCanonicals: new() { "http://example.org/Questionnaire/pa-form" },
        ContextId: null,
        ChangedSince: null);

    // Act
    var result = await _handler.HandleAsync(query, CancellationToken.None);

    // Assert
    result.PackageBundles.Should().HaveCount(1);
    result.PackageBundles[0].Type.Should().Be("collection");
    result.PackageBundles[0].Entry.Should().Contain(e =>
        e.Resource.ResourceType == "Questionnaire");
}
```

---

### 5. $document - FHIR Document Generation

**Canonical**: `http://hl7.org/fhir/OperationDefinition/Composition-document`
**Type**: Instance-level (Composition)
**Priority**: **P2** (Clinical document workflows)
**Effort**: 30-40 hours

#### FHIR Specification

**Invocation**:
```
GET [base]/Composition/{id}/$document?persist={bool}
```

**Input Parameters**:

| Parameter | Card | Type | Description |
|-----------|------|------|-------------|
| persist | 0..1 | boolean | Store generated Bundle at /Bundle endpoint |
| graph | 0..1 | uri | GraphDefinition controlling included resources |

**Output Parameter**:
- **return** (1..1): Bundle (document type)

**Bundle Requirements**:
- `Bundle.type = "document"`
- First entry: Composition resource
- Subsequent entries: All referenced resources
- Signatures: Optional digital signatures

#### Implementation Design

**File Structure**:
```
src/Ignixa.Api/Endpoints/CompositionEndpoints.cs (new)
src/Ignixa.Application.Operations/Features/Document/
  ├── GenerateDocumentQuery.cs
  ├── GenerateDocumentHandler.cs
  └── DocumentBundleBuilder.cs
```

**Endpoint Implementation**:

```csharp
// File: src/Ignixa.Api/Endpoints/CompositionEndpoints.cs
public static class CompositionEndpoints
{
    public static IEndpointRouteBuilder MapCompositionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        // GET /tenant/{tenantId}/Composition/{id}/$document
        endpoints.MapGet(
            "/tenant/{tenantId:int}/Composition/{id}/$document",
            HandleDocumentAsync)
            .WithName("GenerateDocument")
            .Produces<BundleJsonNode>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<OperationOutcomeJsonNode>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleDocumentAsync(
        HttpContext context,
        int tenantId,
        string id,
        [FromQuery] bool? persist,
        [FromQuery] string? graph,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new GenerateDocumentQuery(
            TenantId: tenantId,
            CompositionId: id,
            Persist: persist ?? false,
            GraphDefinitionUrl: graph);

        try
        {
            var result = await mediator.SendAsync(query, cancellationToken);
            return Results.Ok(result.DocumentBundle);
        }
        catch (ResourceNotFoundException)
        {
            return Results.NotFound(CreateOperationOutcome(
                "error",
                "not-found",
                $"Composition/{id} not found"));
        }
    }

    private static OperationOutcomeJsonNode CreateOperationOutcome(
        string severity,
        string code,
        string diagnostics)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.AddIssue(severity, code, diagnostics);
        return outcome;
    }
}
```

**Query/Handler**:

```csharp
// File: src/Ignixa.Application.Operations/Features/Document/GenerateDocumentQuery.cs
public record GenerateDocumentQuery(
    int TenantId,
    string CompositionId,
    bool Persist,
    string? GraphDefinitionUrl
) : IRequest<GenerateDocumentResult>;

public record GenerateDocumentResult(
    BundleJsonNode DocumentBundle,
    string? BundleId
);

// File: src/Ignixa.Application.Operations/Features/Document/GenerateDocumentHandler.cs
public class GenerateDocumentHandler
    : IRequestHandler<GenerateDocumentQuery, GenerateDocumentResult>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly DocumentBundleBuilder _bundleBuilder;
    private readonly ILogger<GenerateDocumentHandler> _logger;

    public GenerateDocumentHandler(
        IFhirRepositoryFactory repositoryFactory,
        DocumentBundleBuilder bundleBuilder,
        ILogger<GenerateDocumentHandler> logger)
    {
        _repositoryFactory = repositoryFactory;
        _bundleBuilder = bundleBuilder;
        _logger = logger;
    }

    public async Task<GenerateDocumentResult> HandleAsync(
        GenerateDocumentQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Generating document for Composition {CompositionId}",
            request.CompositionId);

        var repository = await _repositoryFactory.GetRepositoryAsync(
            request.TenantId,
            cancellationToken);

        // Step 1: Get Composition resource
        var compositionKey = new ResourceKey(
            request.TenantId,
            "Composition",
            request.CompositionId);

        var composition = await repository.GetAsync(compositionKey, cancellationToken);

        if (composition == null)
        {
            throw new ResourceNotFoundException(
                $"Composition/{request.CompositionId} not found");
        }

        // Step 2: Build document Bundle
        var documentBundle = await _bundleBuilder.BuildDocumentBundleAsync(
            composition.Resource,
            request.GraphDefinitionUrl,
            repository,
            cancellationToken);

        // Step 3: Persist if requested
        string? bundleId = null;
        if (request.Persist)
        {
            bundleId = await PersistBundleAsync(
                documentBundle,
                repository,
                cancellationToken);

            _logger.LogInformation(
                "Persisted document bundle as Bundle/{BundleId}",
                bundleId);
        }

        return new GenerateDocumentResult(
            DocumentBundle: documentBundle,
            BundleId: bundleId);
    }

    private async Task<string> PersistBundleAsync(
        BundleJsonNode bundle,
        IFhirRepository repository,
        CancellationToken cancellationToken)
    {
        // Store Bundle resource
        var bundleId = bundle.Id ?? Guid.NewGuid().ToString("N");
        bundle.Id = bundleId;

        await repository.CreateAsync(
            "Bundle",
            bundleId,
            bundle,
            cancellationToken);

        return bundleId;
    }
}
```

**Document Bundle Builder**:

```csharp
// File: src/Ignixa.Application.Operations/Features/Document/DocumentBundleBuilder.cs
public class DocumentBundleBuilder
{
    private readonly ILogger<DocumentBundleBuilder> _logger;

    public DocumentBundleBuilder(ILogger<DocumentBundleBuilder> logger)
    {
        _logger = logger;
    }

    public async Task<BundleJsonNode> BuildDocumentBundleAsync(
        ResourceJsonNode composition,
        string? graphDefinitionUrl,
        IFhirRepository repository,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Building document bundle for Composition {Id}", composition.Id);

        // Create document Bundle
        var bundle = new BundleJsonNode
        {
            Type = "document",
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow
        };

        // First entry: Composition
        bundle.AddEntry(composition, fullUrl: $"urn:uuid:{composition.Id}");

        // Collect all referenced resources
        var referencedResourceKeys = new HashSet<string>();
        CollectReferences(composition, referencedResourceKeys);

        // Retrieve and add referenced resources
        foreach (var resourceKey in referencedResourceKeys)
        {
            var resource = await GetReferencedResourceAsync(
                resourceKey,
                repository,
                cancellationToken);

            if (resource != null)
            {
                bundle.AddEntry(resource, fullUrl: $"urn:uuid:{resource.Id}");

                // Recursively collect nested references
                CollectReferences(resource, referencedResourceKeys);
            }
        }

        _logger.LogDebug(
            "Built document bundle with {Count} entries",
            bundle.Entry?.Count ?? 0);

        return bundle;
    }

    private void CollectReferences(
        ResourceJsonNode resource,
        HashSet<string> referencedKeys)
    {
        // Extract all Reference elements from resource
        // Format: "resourceType/id"

        // TODO: Implement reference extraction using FHIRPath or JSON traversal
        // Look for all properties ending with "Reference" or matching Reference pattern

        // Example references from Composition:
        // - subject
        // - author[]
        // - section[].entry[]
    }

    private async Task<ResourceJsonNode?> GetReferencedResourceAsync(
        string reference,
        IFhirRepository repository,
        CancellationToken cancellationToken)
    {
        // Parse reference (e.g., "Patient/123")
        var parts = reference.Split('/');
        if (parts.Length != 2)
        {
            _logger.LogWarning("Invalid reference format: {Reference}", reference);
            return null;
        }

        var resourceType = parts[0];
        var resourceId = parts[1];

        // Retrieve resource
        var resourceKey = new ResourceKey(
            repository.TenantId,
            resourceType,
            resourceId);

        var wrapper = await repository.GetAsync(resourceKey, cancellationToken);
        return wrapper?.Resource;
    }
}
```

#### Testing Strategy

```csharp
[Fact]
public async Task GenerateDocument_WithValidComposition_ReturnsDocumentBundle()
{
    // Arrange
    var compositionId = "comp-123";
    var query = new GenerateDocumentQuery(1, compositionId, false, null);

    // Act
    var result = await _handler.HandleAsync(query, CancellationToken.None);

    // Assert
    result.DocumentBundle.Type.Should().Be("document");
    result.DocumentBundle.Entry.Should().NotBeEmpty();
    result.DocumentBundle.Entry[0].Resource.ResourceType.Should().Be("Composition");
}

[Fact]
public async Task GenerateDocument_WithPersist_StoresBundleResource()
{
    // Arrange
    var query = new GenerateDocumentQuery(1, "comp-123", persist: true, null);

    // Act
    var result = await _handler.HandleAsync(query, CancellationToken.None);

    // Assert
    result.BundleId.Should().NotBeNull();

    // Verify Bundle was stored
    var stored = await _repository.GetAsync(
        new ResourceKey(1, "Bundle", result.BundleId),
        CancellationToken.None);
    stored.Should().NotBeNull();
}
```

---

## Implementation Roadmap

### Phase Breakdown

| Phase | Operations | Effort | Priority | Completion Target |
|-------|-----------|--------|----------|-------------------|
| **Phase 1** | $docref | 30-40 hrs | P0 | Q1 2026 - Week 2 |
| **Phase 2** | $document | 30-40 hrs | P2 | Q1 2026 - Week 4 |
| **Phase 3** | $member-match | 40-50 hrs | P1 | Q1 2026 - Week 7 |
| **Phase 4** | $questionnaire-package | 40-55 hrs | P1 | Q2 2026 - Week 2 |
| **Phase 5** | $submit-attachment | 50-70 hrs | P1 | Q2 2026 - Week 6 |

**Total Effort**: 230-310 hours (29-39 weeks at 8 hours/week)

### Dependencies

**Infrastructure (Existing ✅)**:
- `IPackageFeature` registration
- Multi-tenant routing
- Medino handler pattern
- DurableTask framework
- Blob storage client

**New Infrastructure Needed**:
- Patient matching algorithm ($member-match)
- GraphDefinition support ($document)
- CQL/ELM evaluation ($questionnaire-package pre-population)

---

## Testing & Validation

### Unit Test Coverage

Each operation requires:
- Query/Command tests
- Handler logic tests
- Parameter validation tests
- Error handling tests
- Edge case tests

**Target**: 90%+ code coverage per operation

### Integration Tests

- Endpoint HTTP tests
- Multi-tenant isolation tests
- Async orchestration tests (for $submit-attachment)
- Persistence tests ($document with persist=true)

### Conformance Testing

- **Touchstone**: US Core conformance tests
- **Inferno**: ONC (g)(10) certification tests
- **Da Vinci Test Kit**: HRex, CDex, DTR conformance

---

## Security Considerations

### Authorization

All operations must enforce:
- Tenant isolation (only access own tenant data)
- Resource-level permissions (RBAC/ABAC)
- Patient consent (especially $member-match)

### Audit Logging

Log all operation invocations:
```csharp
_auditLogger.LogOperation(
    operation: "$member-match",
    tenantId: request.TenantId,
    userId: context.User.Identity.Name,
    parameters: new { PatientId = "***" }, // Redacted PHI
    outcome: "Success");
```

### Data Protection

- **$submit-attachment**: Encrypt attachments at rest
- **$member-match**: Rate limiting to prevent brute-force matching

---

## Performance Considerations

### Caching

- **$docref**: Cache DocumentReference search results (5 min TTL)
- **$questionnaire-package**: Cache built packages (1 hour TTL)
- **$document**: Cache generated bundles (10 min TTL)

### Async Processing Thresholds

| Operation | Sync Threshold | Async Method |
|-----------|----------------|--------------|
| $submit-attachment | < 10 MB | DurableTask |
| $document | < 100 references | Inline |

**Note**: Group/$export (existing) uses async DurableTask for all group exports.

### Pagination

- **$docref**: Implicit pagination (most recent first)

---

## Monitoring & Observability

### Metrics

Track per operation:
- Invocation count
- Success/failure rate
- Response time (p50, p95, p99)
- Payload size

### Alerts

- **$member-match**: Alert on > 10% no-match rate
- **$submit-attachment**: Alert on > 5% failures

---

## Migration Strategy

### Backward Compatibility

All operations are **additive** - no breaking changes to existing APIs.

### Rollout Plan

1. **Week 1-2**: Deploy $docref (P0 blocker)
2. **Week 3-4**: Deploy $document
3. **Week 5-8**: Deploy $member-match
4. **Week 9-12**: Deploy $questionnaire-package
5. **Week 13-18**: Deploy $submit-attachment

### Feature Flags

Enable operations per tenant:
```csharp
if (_featureManager.IsEnabledAsync("Operations.MemberMatch", tenantId))
{
    // Enable $member-match endpoint
}
```

---

## References

### FHIR Specifications

- [$docref](https://hl7.org/fhir/operation-documentreference-docref.html)
- [$member-match](https://www.hl7.org/fhir/us/davinci-hrex/OperationDefinition-member-match.html)
- [$submit-attachment](https://build.fhir.org/ig/HL7/davinci-ecdx/OperationDefinition-submit-attachment.html)
- [$questionnaire-package](https://www.hl7.org/fhir/us/davinci-dtr/OperationDefinition-questionnaire-package.html)
- [$document](https://hl7.org/fhir/composition-operation-document.html)

### Implementation Guides

- [US Core STU 6.1](https://hl7.org/fhir/us/core/)
- [IPA STU 1.0](https://hl7.org/fhir/uv/ipa/)
- [Da Vinci HRex STU 1.0](https://hl7.org/fhir/us/davinci-hrex/)
- [Da Vinci CDex STU 2.1](https://build.fhir.org/ig/HL7/davinci-ecdx/)
- [Da Vinci DTR STU 2.1](https://hl7.org/fhir/us/davinci-dtr/)

### Related ADRs

- [fhir-operations-support-analysis.md](./fhir-operations-support-analysis.md) - Gap analysis
- ADR-2531 - Terminology services implementation
- ADR-2526 - Bulk import operation

---

**Next Steps**:
1. Review and approve technical spec
2. Create epics and stories for each phase
3. Begin Phase 1 implementation ($docref)
4. Set up conformance testing infrastructure

**Document Status**: Proposed
**Last Updated**: 2025-11-18
**Owner**: Development Team
