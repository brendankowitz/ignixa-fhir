# Ignixa.Sidecar.Contracts

Shared gRPC service contracts for Ignixa FHIR Server sidecar integration. This package enables **application-level** distribution via internal NuGet feeds for building custom sidecar services that extend Ignixa with enterprise capabilities: auditing, authorization, metrics, and logging.

## Purpose

The sidecar pattern allows you to deploy external services alongside Ignixa that handle cross-cutting concerns without modifying the core application. This enables:

- **Compliance**: Custom audit logging to enterprise SIEM systems
- **Authorization**: Integration with Azure RBAC, OPA, or custom auth engines
- **Observability**: Centralized metrics and logging to monitoring platforms
- **Multi-tenancy**: Tenant-specific routing of audit/metrics/logs

## Services

This library defines 4 gRPC services via Protocol Buffer (proto3) contracts:

### 1. AuditService (`ignixa_audit.proto`)

**Purpose**: Security audit logging for FHIR operations

**Pattern**: Fire-and-forget (does not block requests)

**Use Cases**:
- Compliance tracking (HIPAA, GDPR, SOC2)
- Security event forwarding to Azure Event Hub
- SIEM integration (Splunk, Elasticsearch, QRadar)
- Custom audit trails

**Key Messages**:
- `AuditEventRequest` - Who did what to which resource, when, and with what result
- `AuditEventResponse` - Success/failure with optional event ID for tracing

**RPCs**:
- `LogAuditEvent(AuditEventRequest)` - Single audit event
- `LogAuditEventBatch(stream AuditEventRequest)` - Client streaming for batches

**Example Fields**:
```protobuf
message AuditEventRequest {
  google.protobuf.Timestamp timestamp = 1;
  string tenant_id = 2;
  string user_id = 3;                // Azure AD oid
  string resource_type = 5;          // "Patient"
  string resource_id = 6;            // "123"
  string operation = 7;              // "Read", "Create", "Update", etc.
  int32 http_status_code = 8;
  bool success = 9;
  string ip_address = 10;
  string correlation_id = 11;
}
```

---

### 2. RbacService (`ignixa_rbac.proto`)

**Purpose**: Role-Based Access Control authorization

**Pattern**: Fail-fast (returns 503 if sidecar unavailable)

**Use Cases**:
- Azure RBAC data plane authorization
- Open Policy Agent (OPA) integration
- SMART on FHIR patient-level access control
- Custom authorization engines with complex business rules

**Key Messages**:
- `AccessCheckRequest` - User, action, and resource context
- `AccessCheckResponse` - Authorized/denied with optional decision ID for caching

**RPCs**:
- `CheckAccess(AccessCheckRequest)` - Single authorization check
- `CheckAccessBatch(stream AccessCheckRequest)` - Bidirectional streaming for bundles

**Example Fields**:
```protobuf
message AccessCheckRequest {
  string user_id = 1;
  string tenant_id = 2;
  string data_action = 3;            // "Microsoft.HealthcareApis/fhir/READ"
  string resource_type = 4;          // "Patient"
  string resource_id = 5;            // "123"
  map<string, string> user_claims = 7; // Roles, scopes, groups
}
```

---

### 3. MetricsService (`ignixa_metrics.proto`)

**Purpose**: FHIR operation metrics collection

**Pattern**: Fire-and-forget (does not block requests)

**Use Cases**:
- Billing and cost tracking (Request Units calculation)
- Application Insights / OpenTelemetry integration
- Prometheus / Grafana dashboards
- SLA monitoring and alerting

**Key Messages**:
- `FhirMetricsRequest` - Operation metadata, performance, and resource usage
- `FhirMetricsResponse` - Success/failure with optional computed billing metrics

**RPCs**:
- `RecordMetric(FhirMetricsRequest)` - Single metric
- `RecordMetricBatch(stream FhirMetricsRequest)` - Client streaming for batches

**Example Fields**:
```protobuf
message FhirMetricsRequest {
  google.protobuf.Timestamp timestamp = 1;
  string tenant_id = 4;
  string resource_type = 5;
  string fhir_operation = 9;         // "read", "search", "create"
  int32 http_status_code = 10;
  bool success = 11;
  int64 request_size_bytes = 12;
  int64 response_size_bytes = 13;
  int64 duration_milliseconds = 14;
  int32 resource_count = 15;         // For search results
}
```

---

### 4. LoggingService (`ignixa_logging.proto`)

**Purpose**: Structured log forwarding

**Pattern**: Fire-and-forget (does not block logging)

**Use Cases**:
- Centralized logging (Elasticsearch, Splunk, Datadog)
- Azure Application Insights custom telemetry
- Prometheus Loki log aggregation
- Custom log enrichment and filtering

**Key Messages**:
- `LogEntryRequest` - Structured log with scopes, exceptions, and trace context
- `LogEntryResponse` - Success/failure with entries processed count

**RPCs**:
- `LogEntry(LogEntryRequest)` - Single log entry
- `LogEntryBatch(LogBatchRequest)` - Batch multiple entries
- `StreamLogEntries(stream LogEntryRequest)` - Client streaming for high throughput

**Example Fields**:
```protobuf
message LogEntryRequest {
  google.protobuf.Timestamp timestamp = 1;
  LogLevel level = 2;                // Trace, Debug, Information, Warning, Error, Critical
  string category = 3;               // "Ignixa.Api.Endpoints.FhirEndpoints"
  string message = 6;
  ExceptionInfo exception = 7;
  string trace_id = 10;              // W3C Trace Context
  map<string, string> state = 12;    // Structured log state
  repeated ScopeInfo scopes = 13;    // Nested scopes
}
```

---

## Error Handling Patterns

| Service | Pattern | Behavior |
|---------|---------|----------|
| **Audit** | Fire-and-forget | Logs locally if sidecar fails, continues processing request |
| **RBAC** | Fail-fast | Returns 503 Service Unavailable if sidecar fails or times out |
| **Metrics** | Fire-and-forget | Logs locally if sidecar fails, continues processing request |
| **Logging** | Fire-and-forget | Falls back to local logging if sidecar fails |

---

## Configuration

Ignixa uses a single configuration toggle to enable sidecar integration:

```json
{
  "Sidecar": {
    "Enabled": false,
    "AuditServiceUrl": "http://127.0.0.1:50051",
    "RbacServiceUrl": "http://127.0.0.1:50052",
    "MetricsServiceUrl": "http://127.0.0.1:50053",
    "LoggingServiceUrl": "http://127.0.0.1:50054",
    "TimeoutSeconds": 5,
    "EnableRetry": false,
    "MinimumLogLevel": "Information",
    "LogBatchSize": 100,
    "LogFlushIntervalMs": 1000
  }
}
```

When `Enabled = true`, Ignixa routes all audit/auth/metrics/logging to the sidecar services.

### Security Note: TLS in Production

⚠️ **Important**: The examples above use `http://` for local development simplicity.

**Production deployments MUST use `https://` with valid TLS certificates** to protect sensitive data in transit:
- Audit events contain user identities and resource access patterns
- Authorization decisions include user claims and role information
- Metrics may contain tenant-identifying information
- Logs can include sensitive application data

Example production configuration:
```json
{
  "Sidecar": {
    "Enabled": true,
    "AuditServiceUrl": "https://audit-sidecar.internal:50051",
    "RbacServiceUrl": "https://rbac-sidecar.internal:50052",
    "MetricsServiceUrl": "https://metrics-sidecar.internal:50053",
    "LoggingServiceUrl": "https://logging-sidecar.internal:50054"
  }
}
```

---

## Building

```bash
dotnet build
```

This automatically generates C# gRPC client and server stubs from the `.proto` files.

---

## Implementing a Sidecar Service

### Option 1: C# (.NET)

Add this package reference to your sidecar project:

```xml
<ItemGroup>
  <PackageReference Include="Ignixa.Sidecar.Contracts" Version="1.0.0" />
</ItemGroup>
```

Implement the service interface:

```csharp
using Grpc.Core;
using Ignixa.Sidecar.Metrics;

public class MetricsGrpcService : MetricsService.MetricsServiceBase
{
    private readonly ILogger<MetricsGrpcService> _logger;

    public MetricsGrpcService(ILogger<MetricsGrpcService> logger)
    {
        _logger = logger;
    }

    public override async Task<FhirMetricsResponse> RecordMetric(
        FhirMetricsRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "Received metrics: Tenant={TenantId}, Operation={Operation}, Duration={Duration}ms",
            request.TenantId,
            request.FhirOperation,
            request.DurationMilliseconds);

        // TODO: Send to Azure Monitor, Application Insights, Prometheus, etc.

        return new FhirMetricsResponse
        {
            Success = true,
            RequestUnits = CalculateRequestUnits(request) // Optional billing RUs
        };
    }

    private double CalculateRequestUnits(FhirMetricsRequest request)
    {
        // Example: Simple RU calculation based on operation and resource count
        return request.FhirOperation switch
        {
            "search" => 1.0 + (request.ResourceCount * 0.1),
            "read" => 1.0,
            "create" => 5.0,
            "update" => 5.0,
            _ => 1.0
        };
    }
}
```

Configure the gRPC server in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<MetricsGrpcService>();

app.Run("http://0.0.0.0:50053"); // Match MetricsServiceUrl
```

### Option 2: Python (grpcio)

1. Copy `.proto` files from this package to your Python project
2. Generate Python stubs:

```bash
python -m grpc_tools.protoc \
  --proto_path=. \
  --python_out=. \
  --grpc_python_out=. \
  ignixa_metrics.proto
```

3. Implement the service:

```python
import grpc
from concurrent import futures
import ignixa_metrics_pb2
import ignixa_metrics_pb2_grpc

class MetricsService(ignixa_metrics_pb2_grpc.MetricsServiceServicer):
    def RecordMetric(self, request, context):
        print(f"Tenant: {request.tenant_id}, Operation: {request.fhir_operation}")

        # TODO: Send to monitoring system

        return ignixa_metrics_pb2.FhirMetricsResponse(
            success=True,
            request_units=self.calculate_request_units(request)
        )

    def calculate_request_units(self, request):
        # Example RU calculation
        operation_costs = {
            "search": 1.0 + (request.resource_count * 0.1),
            "read": 1.0,
            "create": 5.0,
            "update": 5.0
        }
        return operation_costs.get(request.fhir_operation, 1.0)

server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
ignixa_metrics_pb2_grpc.add_MetricsServiceServicer_to_server(MetricsService(), server)
server.add_insecure_port('[::]:50053')
server.start()
server.wait_for_termination()
```

### Option 3: Go

1. Copy `.proto` files to your Go project
2. Generate Go stubs:

```bash
protoc --go_out=. --go-grpc_out=. ignixa_metrics.proto
```

3. Implement the service:

```go
package main

import (
    "context"
    "log"
    "net"
    pb "path/to/generated/ignixa_metrics"
    "google.golang.org/grpc"
)

type metricsServer struct {
    pb.UnimplementedMetricsServiceServer
}

func (s *metricsServer) RecordMetric(ctx context.Context, req *pb.FhirMetricsRequest) (*pb.FhirMetricsResponse, error) {
    log.Printf("Tenant: %s, Operation: %s, Duration: %dms",
        req.TenantId, req.FhirOperation, req.DurationMilliseconds)

    // TODO: Send to monitoring system

    return &pb.FhirMetricsResponse{
        Success: true,
        RequestUnits: calculateRequestUnits(req),
    }, nil
}

func calculateRequestUnits(req *pb.FhirMetricsRequest) float64 {
    // Example RU calculation
    switch req.FhirOperation {
    case "search":
        return 1.0 + (float64(req.ResourceCount) * 0.1)
    case "read":
        return 1.0
    case "create", "update":
        return 5.0
    default:
        return 1.0
    }
}

func main() {
    lis, err := net.Listen("tcp", ":50053")
    if err != nil {
        log.Fatalf("Failed to listen: %v", err)
    }

    s := grpc.NewServer()
    pb.RegisterMetricsServiceServer(s, &metricsServer{})

    log.Printf("Metrics sidecar listening on :50053")
    if err := s.Serve(lis); err != nil {
        log.Fatalf("Failed to serve: %v", err)
    }
}
```

---

## Testing Your Sidecar

Use [grpcurl](https://github.com/fullstorydev/grpcurl) to test your sidecar manually:

```bash
# List services
grpcurl -plaintext localhost:50053 list

# Invoke RecordMetric
grpcurl -plaintext -d '{
  "timestamp": {"seconds": 1234567890},
  "tenant_id": "1",
  "resource_type": "Patient",
  "fhir_operation": "search",
  "http_status_code": 200,
  "success": true,
  "duration_milliseconds": 150
}' localhost:50053 ignixa.sidecar.metrics.MetricsService/RecordMetric
```

---

## Container Deployment

Example Docker Compose configuration:

```yaml
version: '3.8'
services:
  ignixa:
    image: ignixa-fhir:latest
    environment:
      - Sidecar__Enabled=true
      - Sidecar__AuditServiceUrl=http://audit-sidecar:50051
      - Sidecar__RbacServiceUrl=http://rbac-sidecar:50052
      - Sidecar__MetricsServiceUrl=http://metrics-sidecar:50053
      - Sidecar__LoggingServiceUrl=http://logging-sidecar:50054
    depends_on:
      - audit-sidecar
      - rbac-sidecar
      - metrics-sidecar
      - logging-sidecar

  audit-sidecar:
    image: my-audit-sidecar:latest
    ports:
      - "50051:50051"

  rbac-sidecar:
    image: my-rbac-sidecar:latest
    ports:
      - "50052:50052"

  metrics-sidecar:
    image: my-metrics-sidecar:latest
    ports:
      - "50053:50053"

  logging-sidecar:
    image: my-logging-sidecar:latest
    ports:
      - "50054:50054"
```

---

## Kubernetes Deployment

Example sidecar pattern in Kubernetes:

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: ignixa-with-sidecars
spec:
  containers:
  - name: ignixa
    image: ignixa-fhir:latest
    env:
    - name: Sidecar__Enabled
      value: "true"
    - name: Sidecar__MetricsServiceUrl
      value: "http://localhost:50053"
    ports:
    - containerPort: 8080

  - name: metrics-sidecar
    image: my-metrics-sidecar:latest
    ports:
    - containerPort: 50053
```

---

## Proto File Versioning

This package uses semantic versioning:
- **Major**: Breaking changes to proto contracts (field removals, type changes)
- **Minor**: Backward-compatible additions (new fields, new RPCs)
- **Patch**: Documentation updates, bug fixes

**Current Version**: 1.0.0

**Compatibility**: All proto contracts use `proto3` and follow gRPC best practices for backward compatibility.

---

## Advanced Scenarios

### Multi-Tenant Routing

Route audit events to tenant-specific Event Hubs:

```csharp
public override async Task<AuditEventResponse> LogAuditEvent(
    AuditEventRequest request, ServerCallContext context)
{
    var eventHubClient = GetEventHubForTenant(request.TenantId);
    await eventHubClient.SendAsync(ConvertToAuditEvent(request));

    return new AuditEventResponse { Success = true };
}
```

### Caching Authorization Decisions

Cache RBAC decisions to reduce latency:

```csharp
public override async Task<AccessCheckResponse> CheckAccess(
    AccessCheckRequest request, ServerCallContext context)
{
    var cacheKey = $"{request.UserId}:{request.DataAction}:{request.ResourceType}";

    if (_cache.TryGetValue(cacheKey, out bool authorized))
    {
        return new AccessCheckResponse { Authorized = authorized };
    }

    var result = await _authEngine.CheckAccessAsync(request);
    _cache.Set(cacheKey, result.Authorized, TimeSpan.FromMinutes(5));

    return result;
}
```

### Enriching Metrics with Cost Data

Calculate and return Request Units for billing:

```csharp
public override async Task<FhirMetricsResponse> RecordMetric(
    FhirMetricsRequest request, ServerCallContext context)
{
    var requestUnits = CalculateRequestUnits(request);

    await _billingService.RecordUsageAsync(
        request.TenantId, requestUnits, DateTime.UtcNow);

    return new FhirMetricsResponse
    {
        Success = true,
        RequestUnits = requestUnits,
        ComputedProperties =
        {
            { "estimated_cost", (requestUnits * 0.001).ToString("F4") },
            { "billing_period", DateTime.UtcNow.ToString("yyyy-MM") }
        }
    };
}
```

---

## Support

This package is distributed as an **application-level package** for internal use. For questions:
- Check proto file documentation (comprehensive inline comments)
- Review example implementations above
- See Ignixa FHIR Server documentation for integration patterns

---

## License

MIT License - See repository root for full license text.
