# ADR-2526: FHIR Bulk Data Import Operation ($import)

**Status**: Proposed
**Date**: 2025-10-19
**Effort Estimate**: 54-70 hours (6 phases)
**Dependencies**: ADR-2525 (Conditional Operations - completed)

---

## Context

### Problem Statement

FHIR servers need to support **bulk data import** for initial data loading and ongoing data synchronization. The FHIR Bulk Data Import specification defines an asynchronous pattern for importing large datasets in NDJSON format.

**Current Gap**:
- No bulk import capability in the new FHIR server
- Old codebase has comprehensive import implementation using JobManagement framework
- New codebase uses DurableTask framework for long-running operations (see ExportOrchestration)
- Need to design import operation that mirrors the successful export pattern

### Use Cases

1. **Initial Data Load**: Healthcare organization migrating from legacy system
   - 10 million Patient records
   - Late arrivals with historical data (backfill)
   - Allow negative version IDs for proper historical sequencing

2. **Incremental Sync**: Nightly updates from external system
   - Standard upsert behavior (create or update)
   - Error handling for invalid resources
   - Progress tracking and resumability

3. **Multi-Tenant Scenarios**:
   - Tenant 1 (Mayo Clinic): Import 5M patients
   - Tenant 2 (Cleveland Clinic): Import 3M patients
   - Isolation required (no cross-tenant contamination)

### FHIR Specification

**FHIR Bulk Data Import**: http://hl7.org/fhir/uv/bulkdata/import.html

**Key Requirements**:
- **Async Pattern**: POST returns 202 Accepted with Content-Location header
- **Status Endpoint**: GET polls job status (202 in progress, 200 complete)
- **Cancellation**: DELETE aborts orchestration
- **Input Format**: NDJSON files (newline-delimited JSON)
- **Import Modes**:
  - `InitialLoad`: Allow negative version IDs for late arrivals
  - `IncrementalLoad`: Standard upsert (default)
- **Error Handling**: Collect errors, upload to storage, include in response

---

## Architecture Design

### Pattern: Mirror ExportOrchestration

The export operation successfully uses **DurableTask + Minimal API + Job Store**. Import will follow the same pattern:

```
Client
  ↓ POST /tenant/1/$import
API Layer (ImportEndpoints.cs)
  ↓ StartOrchestrationAsync
DurableTask Orchestration (ImportOrchestration.cs)
  ↓ ScheduleTask
Activities
  ├─→ ValidateFileActivity (ETag checks)
  ├─→ DownloadAndParseActivity (streaming NDJSON)
  ├─→ ImportBatchActivity (create/update resources)
  └─→ CompleteJobActivity (finalize, upload errors)
  ↓
Job Store (ImportJobStore.cs)
  └─→ Update job status, error logs
```

### DurableTask Orchestration Workflow

```csharp
public class ImportOrchestration : TaskOrchestration<ImportOrchestrationOutput, ImportOrchestrationInput>
{
    public override async Task<ImportOrchestrationOutput> RunTask(
        OrchestrationContext context,
        ImportOrchestrationInput input)
    {
        // Step 1: Validate files (ETag checks, existence)
        await context.ScheduleTask<bool>(
            typeof(ValidateFileActivity),
            validateInput);

        // Step 2: Process each input file
        foreach (var inputFile in input.InputFiles)
        {
            // Download and parse NDJSON file (streaming)
            var parseOutput = await context.ScheduleTask<DownloadAndParseOutput>(
                typeof(DownloadAndParseActivity),
                parseInput);

            // Import resources in batches (100 at a time)
            foreach (var batch in parseOutput.Batches)
            {
                await context.ScheduleTask<ImportBatchOutput>(
                    typeof(ImportBatchActivity),
                    batchInput);
            }
        }

        // Step 3: Complete job (upload error logs)
        var result = await context.ScheduleTask<ImportOrchestrationOutput>(
            typeof(CompleteJobActivity),
            completeInput);

        return result;
    }
}
```

### Import Modes

#### 1. InitialLoad Mode

**Purpose**: Support late arrivals with historical data.

**Behavior**:
- Allows **negative version IDs** (e.g., Patient/123 version -5)
- Version -1 becomes version 1, version -2 becomes version 2, etc.
- Enables proper historical sequencing when backfilling data

**Example**:
```json
// NDJSON file (late arrival - older data discovered after initial load)
{"resourceType": "Patient", "id": "123", "meta": {"versionId": "-3"}, "name": [{"family": "Doe"}]}
{"resourceType": "Patient", "id": "123", "meta": {"versionId": "-2"}, "name": [{"family": "Doe", "given": ["John"]}]}
{"resourceType": "Patient", "id": "123", "meta": {"versionId": "-1"}, "name": [{"family": "Smith", "given": ["John"]}]}
```

**Storage**:
- Version -3 stored as version 1 (oldest)
- Version -2 stored as version 2
- Version -1 stored as version 3 (newest)

**Conflict Resolution**:
- If Patient/123 version 2 already exists, reject negative version -1 (409 Conflict)

#### 2. IncrementalLoad Mode (Default)

**Purpose**: Standard upsert for ongoing sync.

**Behavior**:
- Create new resource if ID doesn't exist
- Update existing resource if ID exists
- Version IDs auto-incremented by server
- Negative versions rejected (400 Bad Request)

**Example**:
```json
// NDJSON file (incremental updates)
{"resourceType": "Patient", "id": "456", "name": [{"family": "Johnson"}]}
{"resourceType": "Observation", "id": "789", "status": "final", "code": {...}}
```

**Storage**:
- Patient/456 created as version 1 (or updated to next version if exists)
- Observation/789 created as version 1 (or updated to next version if exists)

---

## Component Design

### 1. API Endpoints (Minimal API Pattern)

**File**: `src/Ignixa.Api/Features/Import/Api/ImportEndpoints.cs` (~300 lines)

```csharp
public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST /tenant/{tenantId}/$import - Start import job
        endpoints.MapPost("/tenant/{tenantId:int}/$import", StartImportAsync)
            .WithName("StartImport")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<OperationOutcome>(StatusCodes.Status400BadRequest, "application/fhir+json");

        // GET /tenant/{tenantId}/_import/{jobId} - Poll status
        endpoints.MapGet("/tenant/{tenantId:int}/_import/{jobId}", GetImportStatusAsync)
            .WithName("GetImportStatus")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<ImportStatusResponse>(StatusCodes.Status200OK, "application/fhir+json");

        // DELETE /tenant/{tenantId}/_import/{jobId} - Cancel job
        endpoints.MapDelete("/tenant/{tenantId:int}/_import/{jobId}", CancelImportAsync)
            .WithName("CancelImport")
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }

    private static async Task<IResult> StartImportAsync(
        HttpContext context,
        int tenantId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IImportJobStore jobStore,
        [FromServices] ILogger<ImportEndpoints> logger,
        CancellationToken cancellationToken)
    {
        // 1. Parse Parameters resource from request body
        var parametersJson = await new StreamReader(context.Request.Body).ReadToEndAsync(cancellationToken);
        var parameters = ParametersJsonNode.Parse(parametersJson);

        // 2. Extract import parameters
        var inputFormat = parameters.GetParameterValue<string>("inputFormat"); // "application/fhir+ndjson"
        var inputSource = parameters.GetParameterValue<string>("inputSource"); // Azure blob URL
        var mode = parameters.GetParameterValue<string>("mode") ?? "IncrementalLoad";
        var storageDetail = parameters.GetParameter("storageDetail"); // SAS token, etc.

        var inputFiles = parameters.GetParameters("input")
            .Select(p => new InputFileInfo
            {
                Type = p.GetParameterValue<string>("type"), // "Patient"
                Url = p.GetParameterValue<string>("url"),   // Blob URL
                ETag = p.GetParameterValue<string>("etag")  // For validation
            })
            .ToList();

        // 3. Validate request
        if (inputFormat != "application/fhir+ndjson")
        {
            return Results.BadRequest(CreateOperationOutcome("Invalid inputFormat. Only 'application/fhir+ndjson' is supported."));
        }

        if (mode != "InitialLoad" && mode != "IncrementalLoad")
        {
            return Results.BadRequest(CreateOperationOutcome($"Invalid mode '{mode}'. Must be 'InitialLoad' or 'IncrementalLoad'."));
        }

        // 4. Create job metadata
        var jobId = Guid.NewGuid().ToString("N");
        var job = new BulkImportJob
        {
            JobId = jobId,
            TenantId = tenantId,
            Status = "Queued",
            InputFormat = inputFormat,
            InputSource = inputSource,
            Mode = mode,
            InputFiles = inputFiles,
            CreateDate = DateTimeOffset.UtcNow,
            QueuedDate = DateTimeOffset.UtcNow
        };

        await jobStore.CreateJobAsync(job, cancellationToken);

        // 5. Start DurableTask orchestration
        var orchestrationInput = new ImportOrchestrationInput
        {
            JobId = jobId,
            TenantId = tenantId,
            InputFiles = inputFiles,
            Mode = mode,
            StorageDetail = storageDetail
        };

        var instanceId = await taskHubClient.CreateOrchestrationInstanceAsync(
            typeof(ImportOrchestration),
            orchestrationInput);

        logger.LogInformation(
            "Started import orchestration: JobId={JobId}, InstanceId={InstanceId}, TenantId={TenantId}",
            jobId, instanceId, tenantId);

        // 6. Return 202 Accepted with Content-Location header
        var statusUrl = $"/tenant/{tenantId}/_import/{jobId}";
        context.Response.Headers["Content-Location"] = statusUrl;

        return Results.Accepted(statusUrl);
    }

    private static async Task<IResult> GetImportStatusAsync(
        HttpContext context,
        int tenantId,
        string jobId,
        [FromServices] IImportJobStore jobStore,
        [FromServices] TaskHubClient taskHubClient,
        CancellationToken cancellationToken)
    {
        // 1. Get job metadata
        var job = await jobStore.GetJobAsync(tenantId, jobId, cancellationToken);
        if (job == null)
        {
            return Results.NotFound();
        }

        // 2. Get orchestration state from DurableTask
        var state = await taskHubClient.GetOrchestrationStateAsync(job.OrchestrationInstanceId);

        // 3. Return appropriate response based on status
        if (state.OrchestrationStatus == OrchestrationStatus.Running ||
            state.OrchestrationStatus == OrchestrationStatus.Pending)
        {
            // Still running - return 202 Accepted with progress
            var progress = new
            {
                transactionTime = job.QueuedDate,
                request = $"/tenant/{tenantId}/$import",
                requiresAccessToken = false,
                output = new object[0], // No output yet
                error = new object[0]   // No errors yet
            };

            return Results.Json(progress, statusCode: StatusCodes.Status202Accepted);
        }
        else if (state.OrchestrationStatus == OrchestrationStatus.Completed)
        {
            // Completed - return 200 OK with results
            var output = state.Output as ImportOrchestrationOutput;

            var response = new
            {
                transactionTime = job.QueuedDate,
                request = $"/tenant/{tenantId}/$import",
                requiresAccessToken = false,
                output = new[]
                {
                    new
                    {
                        type = "OperationOutcome",
                        count = output.TotalResources,
                        inputUrl = job.InputSource
                    }
                },
                error = output.ErrorFileUrl != null
                    ? new[]
                    {
                        new
                        {
                            type = "OperationOutcome",
                            url = output.ErrorFileUrl
                        }
                    }
                    : new object[0]
            };

            return Results.Ok(response);
        }
        else
        {
            // Failed/Terminated - return 200 OK with error
            var errorResponse = new
            {
                transactionTime = job.QueuedDate,
                request = $"/tenant/{tenantId}/$import",
                requiresAccessToken = false,
                output = new object[0],
                error = new[]
                {
                    new
                    {
                        type = "OperationOutcome",
                        url = job.ErrorFileUrl ?? ""
                    }
                }
            };

            return Results.Ok(errorResponse);
        }
    }

    private static async Task<IResult> CancelImportAsync(
        int tenantId,
        string jobId,
        [FromServices] IImportJobStore jobStore,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] ILogger<ImportEndpoints> logger,
        CancellationToken cancellationToken)
    {
        // 1. Get job metadata
        var job = await jobStore.GetJobAsync(tenantId, jobId, cancellationToken);
        if (job == null)
        {
            return Results.NotFound();
        }

        // 2. Terminate orchestration
        await taskHubClient.TerminateInstanceAsync(job.OrchestrationInstanceId, "User requested cancellation");

        // 3. Update job status
        job.Status = "Cancelled";
        job.EndDate = DateTimeOffset.UtcNow;
        await jobStore.UpdateJobAsync(job, cancellationToken);

        logger.LogInformation(
            "Cancelled import job: JobId={JobId}, TenantId={TenantId}",
            jobId, tenantId);

        return Results.NoContent();
    }

    private static OperationOutcome CreateOperationOutcome(string message)
    {
        // Create FHIR OperationOutcome for error responses
        return new OperationOutcome
        {
            Issue = new List<OperationOutcome.IssueComponent>
            {
                new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = OperationOutcome.IssueType.Invalid,
                    Diagnostics = message
                }
            }
        };
    }
}
```

**Registration** (`Program.cs`):
```csharp
app.MapImportEndpoints();
```

### 2. DurableTask Orchestration

**File**: `src/Ignixa.Api/Features/Import/Orchestrations/ImportOrchestration.cs` (~200 lines)

```csharp
using DurableTask.Core;
using Ignixa.Api.Features.Import.Models;

namespace Ignixa.Api.Features.Import.Orchestrations;

/// <summary>
/// DurableTask orchestration for FHIR bulk data import.
/// Coordinates file validation, download, parsing, and batch import.
/// </summary>
public class ImportOrchestration : TaskOrchestration<ImportOrchestrationOutput, ImportOrchestrationInput>
{
    public override async Task<ImportOrchestrationOutput> RunTask(
        OrchestrationContext context,
        ImportOrchestrationInput input)
    {
        var totalResources = 0;
        var totalErrors = 0;
        var errorLogEntries = new List<ImportErrorLogEntry>();

        try
        {
            // Step 1: Validate files (ETag checks, existence)
            var validateInput = new ValidateFileInput
            {
                JobId = input.JobId,
                TenantId = input.TenantId,
                InputFiles = input.InputFiles
            };

            var validationResult = await context.ScheduleTask<ValidateFileOutput>(
                typeof(ValidateFileActivity),
                validateInput);

            if (!validationResult.IsValid)
            {
                return new ImportOrchestrationOutput
                {
                    JobId = input.JobId,
                    Status = "Failed",
                    ErrorMessage = validationResult.ErrorMessage,
                    TotalResources = 0,
                    TotalErrors = 0
                };
            }

            // Step 2: Process each input file
            foreach (var inputFile in input.InputFiles)
            {
                // Download and parse NDJSON file (streaming)
                var parseInput = new DownloadAndParseInput
                {
                    JobId = input.JobId,
                    TenantId = input.TenantId,
                    FileUrl = inputFile.Url,
                    ResourceType = inputFile.Type,
                    BatchSize = 100 // Import 100 resources at a time
                };

                var parseOutput = await context.ScheduleTask<DownloadAndParseOutput>(
                    typeof(DownloadAndParseActivity),
                    parseInput);

                // Import each batch of resources
                foreach (var batch in parseOutput.Batches)
                {
                    var batchInput = new ImportBatchInput
                    {
                        JobId = input.JobId,
                        TenantId = input.TenantId,
                        ResourceType = inputFile.Type,
                        Resources = batch.Resources,
                        Mode = input.Mode
                    };

                    var batchOutput = await context.ScheduleTask<ImportBatchOutput>(
                        typeof(ImportBatchActivity),
                        batchInput);

                    totalResources += batchOutput.SuccessCount;
                    totalErrors += batchOutput.ErrorCount;

                    if (batchOutput.Errors.Any())
                    {
                        errorLogEntries.AddRange(batchOutput.Errors);
                    }
                }
            }

            // Step 3: Complete job (upload error logs if any)
            var completeInput = new CompleteJobInput
            {
                JobId = input.JobId,
                TenantId = input.TenantId,
                TotalResources = totalResources,
                TotalErrors = totalErrors,
                ErrorLogEntries = errorLogEntries,
                StorageDetail = input.StorageDetail
            };

            var completeOutput = await context.ScheduleTask<CompleteJobOutput>(
                typeof(CompleteJobActivity),
                completeInput);

            return new ImportOrchestrationOutput
            {
                JobId = input.JobId,
                Status = "Completed",
                TotalResources = totalResources,
                TotalErrors = totalErrors,
                ErrorFileUrl = completeOutput.ErrorFileUrl
            };
        }
        catch (Exception ex)
        {
            // Orchestration failed - log and return error
            return new ImportOrchestrationOutput
            {
                JobId = input.JobId,
                Status = "Failed",
                ErrorMessage = ex.Message,
                TotalResources = totalResources,
                TotalErrors = totalErrors
            };
        }
    }
}

/// <summary>
/// Input for import orchestration.
/// </summary>
public record ImportOrchestrationInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required List<InputFileInfo> InputFiles { get; init; }
    public required string Mode { get; init; } // "InitialLoad" or "IncrementalLoad"
    public ParametersJsonNode? StorageDetail { get; init; } // SAS tokens, etc.
}

/// <summary>
/// Output from import orchestration.
/// </summary>
public record ImportOrchestrationOutput
{
    public required string JobId { get; init; }
    public required string Status { get; init; } // "Completed", "Failed"
    public int TotalResources { get; init; }
    public int TotalErrors { get; init; }
    public string? ErrorFileUrl { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Input file information from FHIR Parameters resource.
/// </summary>
public record InputFileInfo
{
    public required string Type { get; init; }  // "Patient", "Observation", etc.
    public required string Url { get; init; }   // Azure blob URL
    public string? ETag { get; init; }          // For validation
}
```

### 3. Activities

#### 3.1 ValidateFileActivity

**File**: `src/Ignixa.Api/Features/Import/Activities/ValidateFileActivity.cs` (~100 lines)

```csharp
using DurableTask.Core;
using Ignixa.Api.Features.Import.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Features.Import.Activities;

/// <summary>
/// Validates import files (ETag checks, existence).
/// Runs before download to fail fast if files are invalid.
/// </summary>
public class ValidateFileActivity : AsyncTaskActivity<ValidateFileInput, ValidateFileOutput>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ValidateFileActivity> _logger;

    public ValidateFileActivity(
        IHttpClientFactory httpClientFactory,
        ILogger<ValidateFileActivity> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task<ValidateFileOutput> ExecuteAsync(
        TaskContext context,
        ValidateFileInput input)
    {
        _logger.LogInformation(
            "Validating {FileCount} input files for job {JobId}",
            input.InputFiles.Count,
            input.JobId);

        var httpClient = _httpClientFactory.CreateClient();

        foreach (var file in input.InputFiles)
        {
            try
            {
                // Send HEAD request to check file existence and ETag
                var request = new HttpRequestMessage(HttpMethod.Head, file.Url);
                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    return new ValidateFileOutput
                    {
                        IsValid = false,
                        ErrorMessage = $"File not found or inaccessible: {file.Url} (status: {response.StatusCode})"
                    };
                }

                // Check ETag if provided
                if (!string.IsNullOrEmpty(file.ETag))
                {
                    var actualETag = response.Headers.ETag?.Tag?.Trim('"');
                    if (actualETag != file.ETag)
                    {
                        return new ValidateFileOutput
                        {
                            IsValid = false,
                            ErrorMessage = $"ETag mismatch for {file.Url}. Expected: {file.ETag}, Actual: {actualETag}"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating file {Url}", file.Url);
                return new ValidateFileOutput
                {
                    IsValid = false,
                    ErrorMessage = $"Error validating file {file.Url}: {ex.Message}"
                };
            }
        }

        _logger.LogInformation("All files validated successfully for job {JobId}", input.JobId);

        return new ValidateFileOutput
        {
            IsValid = true
        };
    }
}

public record ValidateFileInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required List<InputFileInfo> InputFiles { get; init; }
}

public record ValidateFileOutput
{
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
}
```

#### 3.2 DownloadAndParseActivity

**File**: `src/Ignixa.Api/Features/Import/Activities/DownloadAndParseActivity.cs` (~250 lines)

```csharp
using DurableTask.Core;
using Ignixa.Api.Features.Import.Models;
using Ignixa.SourceNodeSerialization;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Ignixa.Api.Features.Import.Activities;

/// <summary>
/// Downloads NDJSON file from blob storage and parses into batches.
/// Uses streaming to avoid loading entire file into memory.
/// </summary>
public class DownloadAndParseActivity : AsyncTaskActivity<DownloadAndParseInput, DownloadAndParseOutput>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DownloadAndParseActivity> _logger;

    public DownloadAndParseActivity(
        IHttpClientFactory httpClientFactory,
        ILogger<DownloadAndParseActivity> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task<DownloadAndParseOutput> ExecuteAsync(
        TaskContext context,
        DownloadAndParseInput input)
    {
        _logger.LogInformation(
            "Downloading and parsing NDJSON file: {Url} (batch size: {BatchSize})",
            input.FileUrl,
            input.BatchSize);

        var batches = new List<ResourceBatch>();
        var httpClient = _httpClientFactory.CreateClient();

        try
        {
            // Stream download (don't load entire file into memory)
            using var response = await httpClient.GetAsync(input.FileUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var currentBatch = new List<string>();
            var lineNumber = 0;

            // Read line by line (NDJSON format)
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue; // Skip empty lines
                }

                // Add to current batch
                currentBatch.Add(line);

                // When batch is full, create ResourceBatch and start new batch
                if (currentBatch.Count >= input.BatchSize)
                {
                    batches.Add(new ResourceBatch
                    {
                        BatchNumber = batches.Count + 1,
                        Resources = currentBatch.ToList(),
                        StartLine = lineNumber - currentBatch.Count + 1,
                        EndLine = lineNumber
                    });

                    currentBatch.Clear();
                }
            }

            // Add remaining resources as final batch
            if (currentBatch.Any())
            {
                batches.Add(new ResourceBatch
                {
                    BatchNumber = batches.Count + 1,
                    Resources = currentBatch.ToList(),
                    StartLine = lineNumber - currentBatch.Count + 1,
                    EndLine = lineNumber
                });
            }

            _logger.LogInformation(
                "Parsed {LineCount} resources into {BatchCount} batches from {Url}",
                lineNumber,
                batches.Count,
                input.FileUrl);

            return new DownloadAndParseOutput
            {
                Batches = batches,
                TotalLines = lineNumber
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading/parsing file {Url}", input.FileUrl);
            throw new InvalidOperationException($"Failed to download/parse file {input.FileUrl}: {ex.Message}", ex);
        }
    }
}

public record DownloadAndParseInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required string FileUrl { get; init; }
    public required string ResourceType { get; init; }
    public required int BatchSize { get; init; } // Default: 100
}

public record DownloadAndParseOutput
{
    public required List<ResourceBatch> Batches { get; init; }
    public required int TotalLines { get; init; }
}

public record ResourceBatch
{
    public required int BatchNumber { get; init; }
    public required List<string> Resources { get; init; } // NDJSON lines
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
}
```

#### 3.3 ImportBatchActivity

**File**: `src/Ignixa.Api/Features/Import/Activities/ImportBatchActivity.cs` (~300 lines)

```csharp
using DurableTask.Core;
using Ignixa.Api.Features.Import.Models;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Abstractions;
using Ignixa.SourceNodeSerialization;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Features.Import.Activities;

/// <summary>
/// Imports a batch of resources using CreateOrUpdateResourceHandler.
/// Handles both InitialLoad (negative versions) and IncrementalLoad (upsert) modes.
/// </summary>
public class ImportBatchActivity : AsyncTaskActivity<ImportBatchInput, ImportBatchOutput>
{
    private readonly IMediator _mediator;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ILogger<ImportBatchActivity> _logger;

    public ImportBatchActivity(
        IMediator mediator,
        IFhirRepositoryFactory repositoryFactory,
        ILogger<ImportBatchActivity> logger)
    {
        _mediator = mediator;
        _repositoryFactory = repositoryFactory;
        _logger = logger;
    }

    protected override async Task<ImportBatchOutput> ExecuteAsync(
        TaskContext context,
        ImportBatchInput input)
    {
        _logger.LogInformation(
            "Importing batch of {ResourceCount} {ResourceType} resources (mode: {Mode})",
            input.Resources.Count,
            input.ResourceType,
            input.Mode);

        var successCount = 0;
        var errorCount = 0;
        var errors = new List<ImportErrorLogEntry>();

        foreach (var resourceJson in input.Resources)
        {
            try
            {
                // Parse JSON to ResourceJsonNode
                var jsonNode = JsonSourceNodeFactory.Parse(resourceJson);

                if (jsonNode.ResourceType != input.ResourceType)
                {
                    errorCount++;
                    errors.Add(new ImportErrorLogEntry
                    {
                        ResourceType = input.ResourceType,
                        ResourceId = jsonNode.Id ?? "unknown",
                        ErrorCode = "InvalidResourceType",
                        ErrorMessage = $"Expected {input.ResourceType}, got {jsonNode.ResourceType}",
                        ResourceJson = resourceJson
                    });
                    continue;
                }

                // Handle import mode
                if (input.Mode == "InitialLoad")
                {
                    // InitialLoad: Allow negative version IDs
                    var result = await ImportResourceInitialLoadAsync(
                        input.TenantId,
                        input.ResourceType,
                        jsonNode,
                        resourceJson);

                    if (result.IsSuccess)
                    {
                        successCount++;
                    }
                    else
                    {
                        errorCount++;
                        errors.Add(result.Error);
                    }
                }
                else
                {
                    // IncrementalLoad: Standard upsert
                    var result = await ImportResourceIncrementalAsync(
                        input.TenantId,
                        input.ResourceType,
                        jsonNode,
                        resourceJson);

                    if (result.IsSuccess)
                    {
                        successCount++;
                    }
                    else
                    {
                        errorCount++;
                        errors.Add(result.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error importing resource");
                errorCount++;
                errors.Add(new ImportErrorLogEntry
                {
                    ResourceType = input.ResourceType,
                    ResourceId = "unknown",
                    ErrorCode = "UnexpectedError",
                    ErrorMessage = ex.Message,
                    ResourceJson = resourceJson
                });
            }
        }

        _logger.LogInformation(
            "Batch import completed: {SuccessCount} success, {ErrorCount} errors",
            successCount,
            errorCount);

        return new ImportBatchOutput
        {
            SuccessCount = successCount,
            ErrorCount = errorCount,
            Errors = errors
        };
    }

    /// <summary>
    /// Imports resource in InitialLoad mode (allows negative version IDs).
    /// </summary>
    private async Task<ImportResult> ImportResourceInitialLoadAsync(
        int tenantId,
        string resourceType,
        ResourceJsonNode jsonNode,
        string resourceJson)
    {
        try
        {
            var resourceId = jsonNode.Id;
            if (string.IsNullOrEmpty(resourceId))
            {
                return ImportResult.Failure(new ImportErrorLogEntry
                {
                    ResourceType = resourceType,
                    ResourceId = "unknown",
                    ErrorCode = "MissingId",
                    ErrorMessage = "Resource must have an ID in InitialLoad mode",
                    ResourceJson = resourceJson
                });
            }

            // Extract version ID (may be negative)
            var versionIdString = jsonNode.Meta?.VersionId;
            if (string.IsNullOrEmpty(versionIdString))
            {
                return ImportResult.Failure(new ImportErrorLogEntry
                {
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    ErrorCode = "MissingVersionId",
                    ErrorMessage = "Resource must have meta.versionId in InitialLoad mode",
                    ResourceJson = resourceJson
                });
            }

            if (!int.TryParse(versionIdString, out var versionId))
            {
                return ImportResult.Failure(new ImportErrorLogEntry
                {
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    ErrorCode = "InvalidVersionId",
                    ErrorMessage = $"Invalid version ID: {versionIdString}. Must be integer.",
                    ResourceJson = resourceJson
                });
            }

            // Negative versions allowed in InitialLoad mode
            // Version -1 becomes 1, -2 becomes 2, etc.
            var actualVersion = versionId < 0 ? Math.Abs(versionId) : versionId;

            // Check if this version already exists (conflict)
            var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, CancellationToken.None);
            var resourceKey = new ResourceKey(tenantId, resourceType, resourceId, actualVersion);
            var existing = await repository.GetAsync(resourceKey, CancellationToken.None);

            if (existing != null)
            {
                return ImportResult.Failure(new ImportErrorLogEntry
                {
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    ErrorCode = "VersionConflict",
                    ErrorMessage = $"Version {actualVersion} already exists for {resourceType}/{resourceId}",
                    ResourceJson = resourceJson
                });
            }

            // Create resource with specific version ID
            // TODO: Extend CreateOrUpdateResourceHandler to support explicit version IDs
            // For now, use standard create (this will auto-increment version)
            var command = new CreateOrUpdateResourceCommand(
                ResourceType: resourceType,
                Id: resourceId,
                JsonNode: jsonNode,
                Coordinator: null);

            await _mediator.SendAsync(command, CancellationToken.None);

            return ImportResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing resource in InitialLoad mode");
            return ImportResult.Failure(new ImportErrorLogEntry
            {
                ResourceType = resourceType,
                ResourceId = jsonNode.Id ?? "unknown",
                ErrorCode = "ImportError",
                ErrorMessage = ex.Message,
                ResourceJson = resourceJson
            });
        }
    }

    /// <summary>
    /// Imports resource in IncrementalLoad mode (standard upsert).
    /// </summary>
    private async Task<ImportResult> ImportResourceIncrementalAsync(
        int tenantId,
        string resourceType,
        ResourceJsonNode jsonNode,
        string resourceJson)
    {
        try
        {
            var resourceId = jsonNode.Id;
            if (string.IsNullOrEmpty(resourceId))
            {
                // Generate ID if missing
                resourceId = Guid.NewGuid().ToString("N");
            }

            // Reject negative version IDs in IncrementalLoad mode
            var versionIdString = jsonNode.Meta?.VersionId;
            if (!string.IsNullOrEmpty(versionIdString) && int.TryParse(versionIdString, out var versionId) && versionId < 0)
            {
                return ImportResult.Failure(new ImportErrorLogEntry
                {
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    ErrorCode = "NegativeVersionNotAllowed",
                    ErrorMessage = $"Negative version IDs not allowed in IncrementalLoad mode (versionId: {versionId})",
                    ResourceJson = resourceJson
                });
            }

            // Standard create or update
            var command = new CreateOrUpdateResourceCommand(
                ResourceType: resourceType,
                Id: resourceId,
                JsonNode: jsonNode,
                Coordinator: null);

            await _mediator.SendAsync(command, CancellationToken.None);

            return ImportResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing resource in IncrementalLoad mode");
            return ImportResult.Failure(new ImportErrorLogEntry
            {
                ResourceType = resourceType,
                ResourceId = jsonNode.Id ?? "unknown",
                ErrorCode = "ImportError",
                ErrorMessage = ex.Message,
                ResourceJson = resourceJson
            });
        }
    }

    private record ImportResult(bool IsSuccess, ImportErrorLogEntry? Error)
    {
        public static ImportResult Success() => new(true, null);
        public static ImportResult Failure(ImportErrorLogEntry error) => new(false, error);
    }
}

public record ImportBatchInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required string ResourceType { get; init; }
    public required List<string> Resources { get; init; } // NDJSON lines
    public required string Mode { get; init; } // "InitialLoad" or "IncrementalLoad"
}

public record ImportBatchOutput
{
    public required int SuccessCount { get; init; }
    public required int ErrorCount { get; init; }
    public required List<ImportErrorLogEntry> Errors { get; init; }
}

public record ImportErrorLogEntry
{
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public required string ResourceJson { get; init; }
}
```

#### 3.4 CompleteJobActivity

**File**: `src/Ignixa.Api/Features/Import/Activities/CompleteJobActivity.cs` (~150 lines)

```csharp
using DurableTask.Core;
using Ignixa.Api.Features.Import.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Ignixa.Api.Features.Import.Activities;

/// <summary>
/// Completes import job by uploading error logs (if any) and updating job status.
/// </summary>
public class CompleteJobActivity : AsyncTaskActivity<CompleteJobInput, CompleteJobOutput>
{
    private readonly IImportJobStore _jobStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CompleteJobActivity> _logger;

    public CompleteJobActivity(
        IImportJobStore jobStore,
        IHttpClientFactory httpClientFactory,
        ILogger<CompleteJobActivity> logger)
    {
        _jobStore = jobStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task<CompleteJobOutput> ExecuteAsync(
        TaskContext context,
        CompleteJobInput input)
    {
        _logger.LogInformation(
            "Completing import job {JobId}: {TotalResources} resources, {TotalErrors} errors",
            input.JobId,
            input.TotalResources,
            input.TotalErrors);

        string? errorFileUrl = null;

        // Upload error logs if there are any errors
        if (input.ErrorLogEntries.Any())
        {
            errorFileUrl = await UploadErrorLogAsync(input);
        }

        // Update job status
        var job = await _jobStore.GetJobAsync(input.TenantId, input.JobId, CancellationToken.None);
        if (job != null)
        {
            job.Status = "Completed";
            job.EndDate = DateTimeOffset.UtcNow;
            job.TotalResources = input.TotalResources;
            job.TotalErrors = input.TotalErrors;
            job.ErrorFileUrl = errorFileUrl;

            await _jobStore.UpdateJobAsync(job, CancellationToken.None);
        }

        _logger.LogInformation("Import job {JobId} completed", input.JobId);

        return new CompleteJobOutput
        {
            ErrorFileUrl = errorFileUrl
        };
    }

    /// <summary>
    /// Uploads error log to blob storage as NDJSON.
    /// </summary>
    private async Task<string> UploadErrorLogAsync(CompleteJobInput input)
    {
        try
        {
            // Convert errors to NDJSON format (one OperationOutcome per line)
            var ndjsonLines = new StringBuilder();

            foreach (var error in input.ErrorLogEntries)
            {
                var operationOutcome = new
                {
                    resourceType = "OperationOutcome",
                    issue = new[]
                    {
                        new
                        {
                            severity = "error",
                            code = error.ErrorCode,
                            diagnostics = error.ErrorMessage,
                            expression = new[] { $"{error.ResourceType}/{error.ResourceId}" }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(operationOutcome);
                ndjsonLines.AppendLine(json);
            }

            // Upload to blob storage (using storage detail from input)
            // For prototype: Save to local file system
            var errorFileName = $"import-errors-{input.JobId}.ndjson";
            var errorFilePath = Path.Combine("export-output", errorFileName);
            Directory.CreateDirectory("export-output");

            await File.WriteAllTextAsync(errorFilePath, ndjsonLines.ToString());

            var errorFileUrl = $"/export-output/{errorFileName}";

            _logger.LogInformation(
                "Uploaded error log for job {JobId}: {ErrorCount} errors to {Url}",
                input.JobId,
                input.ErrorLogEntries.Count,
                errorFileUrl);

            return errorFileUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading error log for job {JobId}", input.JobId);
            throw;
        }
    }
}

public record CompleteJobInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required int TotalResources { get; init; }
    public required int TotalErrors { get; init; }
    public required List<ImportErrorLogEntry> ErrorLogEntries { get; init; }
    public ParametersJsonNode? StorageDetail { get; init; }
}

public record CompleteJobOutput
{
    public string? ErrorFileUrl { get; init; }
}
```

### 4. Job Store

**Interface**: `src/Ignixa.Api/Features/Import/Abstractions/IImportJobStore.cs` (~50 lines)

```csharp
using Ignixa.Api.Features.Import.Models;

namespace Ignixa.Api.Features.Import.Abstractions;

/// <summary>
/// Stores import job metadata (status, progress, errors).
/// </summary>
public interface IImportJobStore
{
    /// <summary>
    /// Creates a new import job.
    /// </summary>
    Task CreateJobAsync(BulkImportJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Gets import job by ID.
    /// </summary>
    Task<BulkImportJob?> GetJobAsync(int tenantId, string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates import job metadata.
    /// </summary>
    Task UpdateJobAsync(BulkImportJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all import jobs for a tenant (for admin UI).
    /// </summary>
    Task<List<BulkImportJob>> ListJobsAsync(int tenantId, CancellationToken cancellationToken);
}
```

**Model**: `src/Ignixa.Api/Features/Import/Models/BulkImportJob.cs` (~100 lines)

```csharp
namespace Ignixa.Api.Features.Import.Models;

/// <summary>
/// Import job metadata stored in job store.
/// </summary>
public class BulkImportJob
{
    public required string JobId { get; set; }
    public required int TenantId { get; set; }
    public required string Status { get; set; } // "Queued", "Running", "Completed", "Failed", "Cancelled"
    public required string InputFormat { get; set; } // "application/fhir+ndjson"
    public required string InputSource { get; set; } // Azure blob URL
    public required string Mode { get; set; } // "InitialLoad" or "IncrementalLoad"
    public required List<InputFileInfo> InputFiles { get; set; }

    public string? OrchestrationInstanceId { get; set; }

    public DateTimeOffset CreateDate { get; set; }
    public DateTimeOffset QueuedDate { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }

    public int TotalResources { get; set; }
    public int TotalErrors { get; set; }

    public string? ErrorFileUrl { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**Implementation**: `src/Ignixa.Api/Features/Import/Infrastructure/ImportJobStore.cs` (~200 lines)

```csharp
using Ignixa.Api.Features.Import.Abstractions;
using Ignixa.Api.Features.Import.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Ignixa.Api.Features.Import.Infrastructure;

/// <summary>
/// FileSystem-based import job store (prototype).
/// Production: Use SQL or CosmosDB.
/// </summary>
public class ImportJobStore : IImportJobStore
{
    private readonly string _basePath;
    private readonly ILogger<ImportJobStore> _logger;
    private readonly ConcurrentDictionary<string, BulkImportJob> _cache;

    public ImportJobStore(ILogger<ImportJobStore> logger)
    {
        _basePath = Path.Combine("data", "import-jobs");
        _logger = logger;
        _cache = new ConcurrentDictionary<string, BulkImportJob>();

        Directory.CreateDirectory(_basePath);
    }

    public async Task CreateJobAsync(BulkImportJob job, CancellationToken cancellationToken)
    {
        var jobPath = GetJobPath(job.TenantId, job.JobId);
        var directory = Path.GetDirectoryName(jobPath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(job, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jobPath, json, cancellationToken);

        _cache[GetCacheKey(job.TenantId, job.JobId)] = job;

        _logger.LogInformation(
            "Created import job: JobId={JobId}, TenantId={TenantId}",
            job.JobId,
            job.TenantId);
    }

    public async Task<BulkImportJob?> GetJobAsync(int tenantId, string jobId, CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(tenantId, jobId);

        // Check cache first
        if (_cache.TryGetValue(cacheKey, out var cachedJob))
        {
            return cachedJob;
        }

        // Read from disk
        var jobPath = GetJobPath(tenantId, jobId);
        if (!File.Exists(jobPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(jobPath, cancellationToken);
        var job = JsonSerializer.Deserialize<BulkImportJob>(json);

        if (job != null)
        {
            _cache[cacheKey] = job;
        }

        return job;
    }

    public async Task UpdateJobAsync(BulkImportJob job, CancellationToken cancellationToken)
    {
        var jobPath = GetJobPath(job.TenantId, job.JobId);

        var json = JsonSerializer.Serialize(job, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jobPath, json, cancellationToken);

        _cache[GetCacheKey(job.TenantId, job.JobId)] = job;

        _logger.LogInformation(
            "Updated import job: JobId={JobId}, Status={Status}",
            job.JobId,
            job.Status);
    }

    public async Task<List<BulkImportJob>> ListJobsAsync(int tenantId, CancellationToken cancellationToken)
    {
        var tenantPath = Path.Combine(_basePath, tenantId.ToString());
        if (!Directory.Exists(tenantPath))
        {
            return new List<BulkImportJob>();
        }

        var jobFiles = Directory.GetFiles(tenantPath, "*.json");
        var jobs = new List<BulkImportJob>();

        foreach (var file in jobFiles)
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken);
            var job = JsonSerializer.Deserialize<BulkImportJob>(json);
            if (job != null)
            {
                jobs.Add(job);
            }
        }

        return jobs.OrderByDescending(j => j.CreateDate).ToList();
    }

    private string GetJobPath(int tenantId, string jobId) =>
        Path.Combine(_basePath, tenantId.ToString(), $"{jobId}.json");

    private static string GetCacheKey(int tenantId, string jobId) =>
        $"{tenantId}:{jobId}";
}
```

---

## Error Handling Strategy

### Error Categories

| Category | Handling | Example |
|----------|----------|---------|
| **Validation Errors** | Fail fast, return 400 | Invalid input format, missing parameters |
| **File Errors** | Fail job, return 200 with error | File not found, ETag mismatch |
| **Resource Errors** | Log error, continue processing | Invalid resource, schema validation failure |
| **System Errors** | Retry activity, fail orchestration if retries exhausted | Network timeout, database connection failure |

### Error Log Format

**NDJSON file** with one OperationOutcome per line:

```json
{"resourceType":"OperationOutcome","issue":[{"severity":"error","code":"InvalidResourceType","diagnostics":"Expected Patient, got Observation","expression":["Patient/123"]}]}
{"resourceType":"OperationOutcome","issue":[{"severity":"error","code":"MissingId","diagnostics":"Resource must have an ID in InitialLoad mode","expression":["Patient/unknown"]}]}
```

---

## Implementation Phases

### Phase 1: Core Orchestration (16-20 hours)

**Goal**: Basic import workflow working end-to-end.

**Tasks**:
1. Create ImportOrchestration.cs (~200 lines) - DurableTask orchestration
2. Create DownloadAndParseActivity.cs (~250 lines) - Streaming NDJSON parser
3. Create ImportBatchActivity.cs (~300 lines) - Batch resource import (IncrementalLoad only)
4. Create unit tests (~400 lines)

**Deliverable**: Import 1000 Patient resources from local NDJSON file, no error handling.

### Phase 2: Job Store and Status API (8-10 hours)

**Goal**: Job tracking and status polling.

**Tasks**:
1. Create IImportJobStore.cs (~50 lines) - Job store interface
2. Create BulkImportJob.cs (~100 lines) - Job model
3. Create ImportJobStore.cs (~200 lines) - FileSystem implementation
4. Create ImportEndpoints.cs (~300 lines) - Minimal API endpoints
5. Create unit tests (~300 lines)

**Deliverable**: POST /$import returns 202, GET /_import/{id} returns status.

### Phase 3: Validation and Error Handling (10-12 hours)

**Goal**: Robust error handling and recovery.

**Tasks**:
1. Create ValidateFileActivity.cs (~100 lines) - ETag checks
2. Create CompleteJobActivity.cs (~150 lines) - Error log upload
3. Add error collection to ImportBatchActivity (~100 lines changes)
4. Create error log tests (~200 lines)

**Deliverable**: Import job with 10% invalid resources completes successfully, error log uploaded.

### Phase 4: Multi-Tenant Support (6-8 hours)

**Goal**: Tenant isolation and routing.

**Tasks**:
1. Add tenant validation to ImportEndpoints.cs (~50 lines changes)
2. Add tenant-scoped job queries to ImportJobStore.cs (~50 lines changes)
3. Update orchestration to use IFhirRepositoryFactory (~100 lines changes)
4. Create multi-tenant tests (~200 lines)

**Deliverable**: Tenant 1 and Tenant 2 can run import jobs independently.

### Phase 5: Import Modes (InitialLoad) (8-10 hours)

**Goal**: Support negative version IDs for late arrivals.

**Tasks**:
1. Add InitialLoad mode to ImportBatchActivity (~150 lines changes)
2. Extend CreateOrUpdateResourceHandler to accept explicit version IDs (~200 lines changes)
3. Add version conflict detection (~100 lines changes)
4. Create InitialLoad tests (~300 lines)

**Deliverable**: Import late arrivals with negative versions (Patient/123 version -5 → version 5).

### Phase 6: Progress Tracking and E2E Testing (6-10 hours)

**Goal**: Production readiness.

**Tasks**:
1. Add progress tracking to ImportOrchestration (~100 lines changes)
2. Add cancellation support to ImportEndpoints (~50 lines changes)
3. Create E2E tests (~500 lines) - Full import workflow
4. Update ADR with learnings

**Deliverable**: Import 1M Patient resources with progress tracking, cancellation, and error recovery.

---

## Example Requests and Responses

### Example 1: Start Import Job (IncrementalLoad)

**Request**:
```http
POST /tenant/1/$import HTTP/1.1
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "inputFormat",
      "valueString": "application/fhir+ndjson"
    },
    {
      "name": "inputSource",
      "valueUri": "https://mystorage.blob.core.windows.net/import"
    },
    {
      "name": "mode",
      "valueString": "IncrementalLoad"
    },
    {
      "name": "input",
      "part": [
        {
          "name": "type",
          "valueString": "Patient"
        },
        {
          "name": "url",
          "valueUrl": "https://mystorage.blob.core.windows.net/import/patients.ndjson"
        },
        {
          "name": "etag",
          "valueString": "0x8D8C9F5E5D5E5D5"
        }
      ]
    },
    {
      "name": "input",
      "part": [
        {
          "name": "type",
          "valueString": "Observation"
        },
        {
          "name": "url",
          "valueUrl": "https://mystorage.blob.core.windows.net/import/observations.ndjson"
        }
      ]
    }
  ]
}
```

**Response**:
```http
HTTP/1.1 202 Accepted
Content-Location: /tenant/1/_import/a1b2c3d4e5f6
```

### Example 2: Poll Import Status (In Progress)

**Request**:
```http
GET /tenant/1/_import/a1b2c3d4e5f6 HTTP/1.1
```

**Response**:
```http
HTTP/1.1 202 Accepted
Content-Type: application/json

{
  "transactionTime": "2025-10-19T12:00:00Z",
  "request": "/tenant/1/$import",
  "requiresAccessToken": false,
  "output": [],
  "error": []
}
```

### Example 3: Poll Import Status (Completed)

**Request**:
```http
GET /tenant/1/_import/a1b2c3d4e5f6 HTTP/1.1
```

**Response**:
```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "transactionTime": "2025-10-19T12:00:00Z",
  "request": "/tenant/1/$import",
  "requiresAccessToken": false,
  "output": [
    {
      "type": "OperationOutcome",
      "count": 15234,
      "inputUrl": "https://mystorage.blob.core.windows.net/import"
    }
  ],
  "error": [
    {
      "type": "OperationOutcome",
      "url": "/export-output/import-errors-a1b2c3d4e5f6.ndjson"
    }
  ]
}
```

### Example 4: Cancel Import Job

**Request**:
```http
DELETE /tenant/1/_import/a1b2c3d4e5f6 HTTP/1.1
```

**Response**:
```http
HTTP/1.1 204 No Content
```

---

## Consequences and Tradeoffs

### Benefits

1. **Mirrors Successful Export Pattern**: Reuses proven DurableTask architecture
2. **Streaming NDJSON**: Avoids loading entire files into memory (scalable to GB files)
3. **Batch Processing**: Imports 100 resources at a time (tunable)
4. **Error Recovery**: Collects errors, uploads error log, allows retries
5. **Multi-Tenant Isolation**: Jobs scoped to tenant from day 1
6. **Import Modes**: Supports both initial load (negative versions) and incremental sync

### Tradeoffs

| Aspect | Decision | Alternative | Rationale |
|--------|----------|-------------|-----------|
| **Orchestration** | DurableTask | Background queue (Hangfire) | Mirrors export, durable execution, built-in retry |
| **Batch Size** | 100 resources | 1000 resources | Balance throughput vs memory |
| **Error Handling** | Log and continue | Fail fast | Partial success better than all-or-nothing |
| **Job Store** | FileSystem (prototype) | SQL/CosmosDB | Simple for prototype, plan SQL migration |
| **Version Handling** | Explicit version IDs | Auto-increment only | Supports late arrivals in InitialLoad mode |

### Limitations

1. **Explicit Version IDs**: Requires extending CreateOrUpdateResourceHandler to accept version IDs (Phase 5)
2. **No Progress Percentage**: DurableTask doesn't provide built-in progress tracking (Phase 6 adds custom tracking)
3. **No Parallel Batches**: Activities run sequentially (future: parallel batch processing)

### Future Enhancements

1. **Parallel Batch Processing**: Process multiple batches concurrently (10x throughput)
2. **SQL Job Store**: Migrate from FileSystem to SQL for production
3. **Azure Blob Integration**: Direct integration with Azure Blob Storage (SAS tokens)
4. **Schema Validation**: Validate resources against FHIR schema before import
5. **Bulk History**: Create multiple versions per resource in single import

---

## References

### Old Codebase

- `src-old/Microsoft.Health.Fhir.Shared.Api/Controllers/ImportController.cs` - MVC controller pattern
- `src-old/Microsoft.Health.Fhir.Core/Features/Operations/Import/Models/ImportRequest.cs` - Input parameters
- `src-old/Microsoft.Health.Fhir.SqlServer/Features/Operations/Import/ImportOrchestratorJob.cs` - Job orchestration

### New Codebase Patterns

- `src/Ignixa.Api/Features/Export/Orchestrations/ExportOrchestration.cs` - DurableTask orchestration
- `src/Ignixa.Api/Features/Export/Api/ExportEndpoints.cs` - Minimal API endpoints
- `src/Ignixa.Application/Features/Resource/CreateOrUpdateResourceHandler.cs` - Resource creation

### FHIR Specification

- **Bulk Data Import**: http://hl7.org/fhir/uv/bulkdata/import.html
- **NDJSON Format**: http://ndjson.org/
- **Async Pattern**: https://hl7.org/fhir/async.html

### DurableTask Framework

- **GitHub**: https://github.com/Azure/durabletask
- **Orchestration Pattern**: https://docs.microsoft.com/azure/azure-functions/durable/durable-functions-orchestrations

---

## Next Steps

1. ✅ **ADR Created** - Document architecture design
2. **Phase 1** - Implement core orchestration (16-20 hours)
3. **Phase 2** - Add job store and status API (8-10 hours)
4. **Phase 3** - Add validation and error handling (10-12 hours)
5. **Phase 4** - Add multi-tenant support (6-8 hours)
6. **Phase 5** - Add InitialLoad mode (8-10 hours)
7. **Phase 6** - Add progress tracking and E2E tests (6-10 hours)

**Total Effort**: 54-70 hours (6-9 weeks at 8 hours/week)

---

## Status Updates

**2025-10-19**: ADR created, design approved, ready for Phase 1 implementation.
