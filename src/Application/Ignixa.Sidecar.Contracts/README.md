# Ignixa.Sidecar.Contracts

Shared gRPC service contracts for Ignixa sidecar integration.

## Services

This library defines 3 gRPC services:

### 1. MetricsService (`ignixa_metrics.proto`)
**Purpose**: Generic FHIR operation metrics collection

**Used for**:
- Billing (Monitor Request Units)
- Telemetry (Application Insights enrichment)

**Key Message**: `FhirMetricsRequest` - Contains all operation metadata (tenant, resource, performance, etc.)

### 2. AuditService (`ignixa_audit.proto`)
**Purpose**: Security audit logging

**Used for**:
- Compliance tracking
- Event Hub audit logs

**Key Message**: `AuditEventRequest` - Security event details (user, operation, result)

### 3. RbacService (`ignixa_rbac.proto`)
**Purpose**: RBAC authorization

**Used for**:
- Data action permission checks
- Azure AD integration

**Key Message**: `AccessCheckRequest` - Authorization request (user, action, resource)

## Building

```bash
dotnet build
```

This generates gRPC client and server stubs automatically.

## Usage

### Server (Sidecar)
```csharp
public class MetricsGrpcService : MetricsService.MetricsServiceBase
{
    public override async Task<FhirMetricsResponse> RecordMetric(
        FhirMetricsRequest request, ServerCallContext context)
    {
        // Implementation
    }
}
```

### Client (Ignixa)
```csharp
builder.Services.AddGrpcClient<MetricsService.MetricsServiceClient>(o =>
{
    o.Address = new Uri("http://127.0.0.1:50051");
});
```
