# Investigation: Subscription Engine

**Feature**: subscriptions
**Status**: Complete
**Created**: 2025-11-04
**Original ADR**: 2600

---

## Executive Summary

FHIR Subscriptions enable proactive event notifications from a server to clients. This investigation examined:

1. **FHIR Spec Requirements** - What subscription features must be implemented
2. **Old Codebase Implementation** - Microsoft's reference implementation in `ThirdParty/Subscriptions/`
3. **Ignixa Architecture** - How to integrate subscriptions into current design
4. **fhir-candle Reference** - An alternative modern implementation for inspiration

The findings show **subscriptions are a mature feature with proven implementation patterns** ready for integration into Ignixa.

---

## FHIR Subscriptions Specification (R4/R5)

### Core Concepts

FHIR Subscriptions consist of three key resources:

#### 1. **SubscriptionTopic** (R5 Only)
- **Purpose**: Defines what events trigger notifications
- **Examples**:
  - "Patient admission events"
  - "Observations matching criteria: code=glucose AND status=final"
  - "Resource create/update/delete for Patient type"
- **Includes**: Resource type filters, status filters, event triggers, required permissions
- **R4 Status**: **Does not exist in R4** - R4 subscriptions use simple criteria strings instead (see below)
- **R5 Status**: New resource type in R5 for discoverable, reusable topic definitions

#### 2. **Subscription** (R4/R5)
- **Purpose**: Client request for notifications on a specific topic or criteria
- **R4 Properties** (criteria-based):
  - `status` (1..1) - Subscription state (requested, active, error, off)
  - `reason` (1..1) - Why this subscription was created
  - `criteria` (1..1) - Search parameters defining matching resources (e.g., "Observation?code=glucose&status=final")
  - `channel` (1..1) - Delivery mechanism configuration
    - `type` (1..1) - rest-hook, websocket, email, sms, message
    - `endpoint` - Where to send notifications (URL, email, etc.)
    - `header` - Authorization headers, custom headers
  - `end` (0..1) - When subscription expires
  - `heartbeatTimeout` (0..1) - Keepalive interval (seconds)
  - `timeout` (0..1) - Delivery timeout (seconds)

- **R5 Properties** (topic-based, extends R4):
  - All R4 properties above (backwards compatible)
  - `topic` (1..1) - Canonical URI to SubscriptionTopic resource (e.g., "http://example.org/fhir/SubscriptionTopic/patient-admission")
  - `filterBy` (0..*) - Additional filters within the topic's constraints
  - Deprecated: `criteria` field (replaced by `topic`)

- **Status**: requested → active → error → off
- **Lifecycle**: Create → Validate → Handshake → Active → Notifications → Expire/Deactivate → Delete

#### 3. **SubscriptionStatus** (R5 Only)
- **Purpose**: Metadata about a notification delivery (new in R5)
- **R4 Alternative**: Servers may use custom extensions or omit status metadata
- **Included in Bundles** as `Bundle.entry[0].resource = SubscriptionStatus` (first entry, always)
- **Notification Types**:
  - `handshake` - Connection establishment verification
  - `heartbeat` - Keepalive with no events (subscriber still active)
  - `event-notification` - Actual resource events triggered subscription
  - `query-status` - Response to $status operation
  - `query-event` - Response to $events operation
- **Key Properties**:
  - `type` (1..1) - Notification type (above)
  - `subscription` (1..1) - Reference to Subscription resource
  - `status` (0..1) - Current subscription state at time of notification
  - `eventsSinceSubscriptionStart` (0..1) - Total events sent
  - `notificationEvent` (0..*) - Details of events that triggered notification

### Channel Types (Supported by FHIR Spec)

| Channel Type | Mechanism | Use Case | Guarantees |
|--------------|-----------|----------|-----------|
| **rest-hook** | HTTP POST to client URL | Webhooks, integrations | Best-effort (no retry) |
| **websocket** | WebSocket upgrade | Real-time push | Best-effort |
| **email** | SMTP to email address | Alerts, summaries | Best-effort |
| **sms** | Text message | Critical alerts | Best-effort |
| **message** | FHIR Bundle via messaging | FHIR-native integrations | Best-effort |

> **Note**: All channels are "best-effort" per spec unless server explicitly provides retry/guaranteed delivery.

### Conformance Requirements

A FHIR server claiming subscription support MUST:

1. **Subscription Resource Operations**
   - `POST /{tenant}/Subscription` - Create subscription
   - `GET /{tenant}/Subscription/{id}` - Read subscription
   - `PUT /{tenant}/Subscription/{id}` - Update subscription (e.g., change status)
   - `DELETE /{tenant}/Subscription/{id}` - Delete subscription
   - `GET /{tenant}/Subscription?...` - Search subscriptions

2. **Status Operation** (R5 RECOMMENDED, R4 Optional)
   - `GET /{tenant}/Subscription/{id}/$status` - Check current status and event count
   - Returns SubscriptionStatus resource with live status info
   - **Note**: R5 recommends this operation; R4 servers may omit it

3. **SubscriptionTopic Operations** (R5 Only)
   - `GET /{tenant}/SubscriptionTopic` - List available topics
   - `GET /{tenant}/SubscriptionTopic/{id}` - Describe topic
   - **Note**: R4 servers do not support topics as resources

4. **Notification Bundles** (R5)
   - Delivery format: `Bundle` resource with `Bundle.type = subscription-notification`
   - **R5**: `Bundle.entry[0]` = SubscriptionStatus (metadata, always present)
   - **R5**: `Bundle.entry[1..N]` = Matching resources
   - **R5**: **NO response elements** - Subscription-notification bundles are content-delivery bundles, not transaction/batch bundles
   - **R4**: Alternative: May use custom extensions or wrapper format for SubscriptionStatus

5. **Authorization**
   - Subscriptions inherit the creating user's permissions
   - Notifications only include resources the subscriber can read
   - Server must validate filter criteria against user's authorizations

### Payload Content Options

Subscriptions can specify notification content (R4 & R5):
- `empty` - SubscriptionStatus only (no resources)
- `id-only` - Resource IDs only (Bundle entries with minimal metadata)
- `full-resource` - Complete resource in each notification (default)

---

## Microsoft Reference Implementation Analysis

Located in: `ThirdParty/Subscriptions/Microsoft.Health.Fhir.*`

### Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│            Subscription API Layer                        │
│  (Create/Read/Update/Delete Subscription resources)    │
└──────────────────────┬──────────────────────────────────┘
                       │
         ┌─────────────┴──────────────┐
         ▼                            ▼
  ┌──────────────────┐       ┌──────────────────┐
  │ Validator        │       │ UpdateHandler    │
  │ - Schema check   │       │ - Persist to DB  │
  │ - Topic match    │       │ - Send handshake │
  │ - Auth check     │       │                  │
  └──────────────────┘       └──────────────────┘

         ┌──────────────────────────────────────┐
         │  ISubscriptionManager                │
         │  (Persistence Layer)                 │
         │  - Load active subscriptions         │
         │  - Update status                     │
         │  - Track errors                      │
         └──────────────────────────────────────┘
                       │
         ┌─────────────┴──────────────┐
         ▼                            ▼
    ┌─────────────────┐       ┌──────────────┐
    │ Event Detection │       │ Job Queue    │
    │ (POST Create/   │       │ (DurableTask)│
    │  Upsert hooks)  │       │              │
    └─────────────────┘       └──────────────┘
                       │
         ┌─────────────┴──────────────┐
         ▼                            ▼
  ┌──────────────────┐    ┌────────────────────┐
  │ Processing Job   │    │ Watchdog (SQL)     │
  │ - Match filters  │    │ - Lease mgmt       │
  │ - Build bundle   │    │ - Rebalance        │
  │ - Send to channel│    │ - Error recovery   │
  └──────────────────┘    └────────────────────┘
                       │
         ┌─────────────┴──────────────┐
         ▼                            ▼
    ┌──────────────┐         ┌──────────────────┐
    │RestHook      │         │DataLake/Storage │
    │Channel       │         │Channels          │
    │(HTTP POST)   │         │(Blob/Event Grid) │
    └──────────────┘         └──────────────────┘
                       │
                       ▼
              ┌──────────────────┐
              │Client Endpoint   │
              │(Receives Bundle) │
              └──────────────────┘
```

### Key Components

#### 1. **Channels** (`Channels/`)

**Interface**: `ISubscriptionChannel.cs`
```csharp
public interface ISubscriptionChannel
{
    Task PublishAsync(
        IReadOnlyCollection<ResourceWrapper> resources,
        SubscriptionInfo subscriptionInfo,
        DateTimeOffset transactionTime,
        CancellationToken cancellationToken);

    Task PublishHandShakeAsync(SubscriptionInfo subscriptionInfo, CancellationToken cancellationToken);
    Task PublishHeartBeatAsync(SubscriptionInfo subscriptionInfo, CancellationToken cancellationToken);
}
```

**Implementations**:
- `RestHookChannel.cs` - HTTP POST to webhook URL
  - Uses HttpClient to deliver Bundle via HTTP
  - Constructs Bundle with SubscriptionStatus + matching resources
  - No built-in retry (best-effort)

- `StorageChannel.cs` - Writes to Azure Blob Storage / Event Grid
  - For audit trails, backpressure scenarios
  - Decouples from client endpoint failures

- `DataLakeChannel.cs` - Azure Data Lake integration
  - Bulk notification aggregation
  - Analytics-focused delivery

**Pattern**: Attribute-based registration
```csharp
[ChannelType(SubscriptionChannelType.RestHook)]
public sealed class RestHookChannel : ISubscriptionChannel
```

#### 2. **Models** (`Models/`)

| Class | Purpose |
|-------|---------|
| `SubscriptionInfo` | In-memory representation of Subscription resource |
| `ChannelInfo` | Channel configuration (type, endpoint, headers) |
| `SubscriptionStatus` | Notification metadata |
| `SubscriptionJobDefinition` | Job payload for processing |
| `ISubscriptionModelConverter` | Converts FHIR Resource → SubscriptionInfo |

**Example: SubscriptionInfo**
```csharp
public class SubscriptionInfo
{
    public string FilterCriteria { get; set; }  // e.g., "Patient?name=John"
    public ChannelInfo Channel { get; set; }     // Delivery config
    public Uri Topic { get; set; }               // Topic URI (R5)
    public string ResourceId { get; set; }       // Subscription resource ID
    public SubscriptionStatus Status { get; set; }
}
```

#### 3. **Persistence** (`Persistence/`)

**Interface**: `ISubscriptionManager.cs`
```csharp
public interface ISubscriptionManager
{
    Task<IReadOnlyCollection<SubscriptionInfo>> GetActiveSubscriptionsAsync(CancellationToken ct);
    Task SyncSubscriptionsAsync(CancellationToken ct);
    Task MarkAsError(SubscriptionInfo info, CancellationToken ct);
}
```

**Responsibilities**:
- Load active subscriptions from database
- Cache subscriptions in memory (with sync interval)
- Track delivery errors / update status
- Handle subscription lifecycle

#### 4. **Operations** (`Operations/`)

**SubscriptionProcessingJob**:
- Triggered by watchdog when resources match subscription filters
- Fetches matching resources from `IFhirDataStore`
- Invokes appropriate channel to deliver bundle
- Uses `SubscriptionChannelFactory` to resolve channel implementation

**SubscriptionsOrchestratorJob** (DurableTask):
- Orchestrates complex multi-step subscription workflows
- Handles retries with exponential backoff
- Coordinates channel fallbacks

#### 5. **Validation** (`Validation/`)

**SubscriptionValidator**:
- Called via `IPipelineBehavior<CreateResourceRequest>` hook
- Validates subscription input before persistence
- Checks: topic exists, channel type supported, filter criteria valid
- Sends handshake to endpoint to verify connectivity

**CreateOrUpdateSubscriptionBehavior** (Pipeline):
- MediatR behavior that intercepts Create/Update requests
- Pipes Subscription resources through validator
- Ensures only validated subscriptions are persisted

#### 6. **Watchdogs** (SQL Server layer, `ThirdParty/Subscriptions/Microsoft.Health.Fhir.SqlServer/`)

**SubscriptionProcessorWatchdog**:
- Background service that monitors transaction log
- Detects new resources matching subscription filters
- Creates `SubscriptionProcessingJob` items in job queue
- Implements distributed lease management for scale-out

**Other Watchdogs**:
- `CleanupEventLogWatchdog` - Removes expired subscription events
- `DefragWatchdog` - Database maintenance
- `TransactionWatchdog` - Tracks transaction IDs for rebalancing

### Integration Points with Ignixa

**1. Pipeline Behaviors (Application Layer)**
- Microsoft uses `IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>` to intercept creates/updates
- Ignixa can mirror this pattern in `Ignixa.Application.Infrastructure.Behaviors`
- Hook point: After resource validation, before persistence

**2. Event Detection (Storage Layer)**
- Microsoft uses SQL Server watchdogs monitoring transaction log
- Ignixa could use: Database triggers, event sourcing, or transaction log tailing
- Simpler approach: Intercept in handlers directly

**3. Persistence (Domain Models)**
- Need to design `Subscription` resource models in `Ignixa.Domain`
- Include validation rules, status machine, channel configuration
- Follow Ignixa's `ResourceJsonNode` pattern (not external POCO models)

**4. Background Processing (DurableTask)**
- Ignixa already has `Ignixa.Application.BackgroundOperations` using DurableTask
- Can add `SubscriptionProcessingJob` alongside existing Export/Import jobs
- Reuse orchestration patterns from existing jobs

**5. API Endpoints (API Layer)**
- Add `SubscriptionEndpoints.cs` to `src/Ignixa.Api/Infrastructure/`
- Extend `FhirEndpoints.cs` to include Subscription CRUD routes
- Follow Ignixa's pattern: tenant-aware routes, authorization via context

**6. Search Parameters (Search Layer)**
- Subscription resources need search parameters (status, channel-type, criteria, etc.)
- Add to `Ignixa.Search` definition files
- Extend `SearchResourcesHandler` to support Subscription queries

---

## R4 vs R5: Critical Differences

This section clarifies the fundamental differences between R4 and R5 subscription models, as understanding these is critical for implementation planning.

### R4 Subscription Model (Current Standard)

| Aspect | R4 | Notes |
|--------|----|----|
| **Topic Definition** | No SubscriptionTopic resource exists | Topics defined via configuration or implicit (server-specific) |
| **Subscription Criteria** | `Subscription.criteria` string field | Format: Search parameters (e.g., "Observation?code=glucose&status=final") |
| **Subscription Topic Reference** | Uses criteria string directly | Not a reference to another resource |
| **Status Notification** | No SubscriptionStatus resource | Status metadata may be included via custom extensions |
| **Channel Configuration** | `Subscription.channel` with type/endpoint | Limited standardization of channel behavior |
| **Notification Delivery** | No standard SubscriptionStatus in bundle | Server decides how to include status info (if at all) |
| **Discovery** | Topics are implicit or config-based | Clients must know topic criteria in advance |

### R5 Subscription Model (New - Recommended)

| Aspect | R5 | Notes |
|--------|----|----|
| **Topic Definition** | **New SubscriptionTopic resource** | Discoverable, versioned, queryable topics with defined filters |
| **Subscription Criteria** | `Subscription.topic` canonical URI | References SubscriptionTopic resource (e.g., "http://example.org/SubscriptionTopic/patient-admission") |
| **Subscription Filters** | `Subscription.filterBy` array | Additional filters within topic's constraints |
| **Status Notification** | **New SubscriptionStatus resource** | Standardized metadata about notifications |
| **Channel Configuration** | `Subscription.channel` (extended) | More detailed configuration options |
| **Notification Delivery** | SubscriptionStatus always first entry | Standard Bundle format: `[SubscriptionStatus, ...resources]` |
| **Discovery** | SubscriptionTopic resources are discoverable | Clients can query `/SubscriptionTopic` to find available topics |

### Migration Path: R4 to R5

**Important**: R4 subscriptions do NOT have SubscriptionTopic. If implementing subscriptions:

1. **Option A: Pure R4 (MVP)**
   - Use `Subscription.criteria` field only
   - Define topics via configuration
   - No SubscriptionStatus resource
   - Simpler to implement, limited extensibility
   - **Recommended for Phase 1-3**

2. **Option B: R5 with R4 Compatibility**
   - Create SubscriptionTopic resources for organization
   - Subscriptions can use `topic` (R5) or `criteria` (R4 compat)
   - Always include SubscriptionStatus in bundles (even for R4 clients)
   - More complex but future-proof
   - **Recommended for Phase 4+**

3. **Option C: R4 Backports (Not Recommended)**
   - Some implementations add SubscriptionTopic to R4 via extensions
   - Non-standard, creates compatibility issues
   - Avoid unless required by specific use case

### Implementation Strategy for Ignixa

Since Ignixa supports R4/R4B/R5:

**Tenant Configuration Approach**:
```
R4/R4B Tenants:
  - Support: Subscription.criteria (search parameter string)
  - Topics: Configuration-based (not as resources)
  - Notifications: Minimal SubscriptionStatus (via extension if needed)
  - No: SubscriptionTopic resources

R5 Tenants:
  - Support: Both Subscription.criteria (legacy) and Subscription.topic (preferred)
  - Topics: SubscriptionTopic resources (queryable)
  - Notifications: Standard SubscriptionStatus as first Bundle entry
  - Yes: SubscriptionTopic resources available
```

**Code Strategy - Multi-Version Adapter Pattern**:

Since Ignixa is **inherently multi-FHIR-version**, subscriptions must use a core engine with version-specific adapters:

```
┌──────────────────────────────────────────────────────────┐
│          Subscription Core Engine                         │
│  (Version-agnostic business logic)                       │
│  - Criteria/Topic parsing                               │
│  - Filter matching                                       │
│  - Channel resolution                                   │
│  - Job queueing                                         │
└──────────────────────────────────────────────────────────┘
             ↑           ↑            ↑
      ┌──────┴────┬──────┴────┬──────┴────┐
      │           │           │           │
      ▼           ▼           ▼           ▼
 ┌────────┐  ┌────────┐  ┌────────┐  ┌────────┐
 │ R4 Adp │  │ R4B Adp│  │ R5 Adp │  │Bundle  │
 │        │  │        │  │        │  │Builder │
 │- criteria│ │-criteria│ │- topic │  │        │
 │- status │ │- status │ │-topics │  │Per-ver │
 │- ext   │ │- ext    │ │-status │  │format  │
 └────────┘  └────────┘  └────────┘  └────────┘
```

**Implementation Details**:

1. **Core Engine** (version-independent):
   ```csharp
   interface ISubscriptionMatcher
   {
       Task<bool> MatchesAsync(ResourceJsonNode resource, SubscriptionInfo subscription, CancellationToken ct);
   }

   interface ISubscriptionChannelFactory
   {
       ISubscriptionChannel Create(SubscriptionChannelType channelType);
   }

   class SubscriptionProcessingJob : IJob
   {
       // Logic for all versions - uses adapters for version-specific details
   }
   ```

2. **Version Adapters** (e.g., `R4SubscriptionAdapter`, `R5SubscriptionAdapter`):
   ```csharp
   interface ISubscriptionAdapter
   {
       // Convert FHIR Subscription resource to internal SubscriptionInfo
       Task<SubscriptionInfo> ConvertFromResourceAsync(ResourceJsonNode subscription, CancellationToken ct);

       // Validate subscription input before persistence
       Task<ValidationResult> ValidateAsync(ResourceJsonNode subscription, CancellationToken ct);

       // Parse criteria/topic into matchable form
       Task<ISubscriptionCriteria> ParseCriteriaAsync(ResourceJsonNode subscription, CancellationToken ct);
   }

   class R4SubscriptionAdapter : ISubscriptionAdapter
   {
       // R4-specific: criteria string parsing, no SubscriptionStatus in bundles
   }

   class R5SubscriptionAdapter : ISubscriptionAdapter
   {
       // R5-specific: topic URI resolution, SubscriptionStatus as entry[0]
   }
   ```

3. **Bundle Builder** (version-aware):
   ```csharp
   interface ISubscriptionNotificationBundleBuilder
   {
       // Build notification bundle per FHIR version
       Task<ResourceJsonNode> BuildAsync(
           SubscriptionInfo subscription,
           IReadOnlyCollection<ResourceJsonNode> matchedResources,
           FhirVersion version,
           CancellationToken ct);
   }
   ```

4. **Request Flow** (multi-version):
   ```
   POST /tenant/1/Subscription (client sends R4 or R5 format)
   ↓
   CreateSubscriptionEndpoint
   ↓
   Determine tenant FHIR version from context (TenantResolutionMiddleware)
   ↓
   Get appropriate adapter: ISubscriptionAdapter adapter = _adapterFactory.GetAdapter(fhirVersion);
   ↓
   adapter.ValidateAsync(subscription) → Validate via version-specific rules
   ↓
   adapter.ConvertFromResourceAsync(subscription) → Internal SubscriptionInfo
   ↓
   CreateSubscriptionHandler → Persist (stored as-is, no conversion)
   ↓
   Return 201 Created (to client, no transformation)

   --- Later, when resource matches ---

   POST /tenant/1/Patient (R4 or R5 format)
   ↓
   CreateResourceHandler
   ↓
   SubscriptionNotificationBehavior
   ↓
   _subscriptionMatcher.MatchesAsync(patient, subscription) → Core engine (version-agnostic)
   ↓
   Queue: SubscriptionProcessingJob(subscriptionId, resourceId, tenantId)
   ↓

   --- Background processing ---

   SubscriptionProcessingJob.ExecuteAsync()
   ↓
   Load subscription resource (stored as-is)
   ↓
   Load tenant FHIR version (via TenantContext)
   ↓
   Get bundle builder for version: ISubscriptionNotificationBundleBuilder builder = _builderFactory.GetBuilder(version);
   ↓
   builder.BuildAsync(subscription, matchedResources, version) → Build correct notification format
   ↓
   RestHookChannel.PublishAsync(bundle) → Deliver per version spec
   ```

**Key Architectural Points**:
- **Core Engine**: Single implementation, no version logic
- **Version Adapters**: Handle spec differences (criteria vs topic, SubscriptionStatus format, etc.)
- **Tenant-Driven**: FHIR version comes from tenant configuration, not from subscription request
- **Transparent Conversion**: Subscription stored as-is; conversion happens at validation and notification time
- **Reuse Pattern**: Same pattern used for other multi-version features (Patch, Bundle, etc.)

**Key Decision**: Build version-agnostic core with pluggable adapters, not separate implementations per version.

---

## Ignixa Current Architecture

### Layer Organization

```
src/Ignixa.Api/
├── Infrastructure/
│   ├── FhirEndpoints.cs          ← FHIR routes (GET/POST/PUT/DELETE)
│   ├── HistoryEndpoints.cs       ← _history operations
│   ├── OperationEndpoints.cs     ← Custom operations ($validate, etc.)
│   └── PatchEndpoints.cs         ← PATCH support
├── Features/
│   └── Health/                   ← Application health checks
└── Middleware/
    └── TenantResolutionMiddleware ← Multi-tenancy enforcement

src/Ignixa.Application/
├── Features/
│   ├── Resource/
│   │   ├── GetResourceQuery.cs
│   │   ├── GetResourceHandler.cs
│   │   ├── CreateOrUpdateResourceCommand.cs  ← Creation hook point
│   │   ├── CreateOrUpdateResourceHandler.cs
│   │   ├── DeleteResourceCommand.cs          ← Deletion hook point
│   │   ├── SearchResourcesQuery.cs
│   │   └── SearchResourcesHandler.cs
│   ├── Bundle/                   ← Complex multi-resource ops
│   ├── Patch/                    ← PATCH semantics
│   ├── History/                  ← _history operations
│   └── ConditionalOperations/    ← Conditional create/update
└── Infrastructure/
    ├── Behaviors/                ← Pipeline behaviors (hooks)
    │   ├── AuthorizationBehavior.cs
    │   └── LoggingBehavior.cs
    └── [Subscription behavior would go here]

src/Ignixa.Application.BackgroundOperations/
├── Export/                       ← Durable Task orchestrations
├── Import/
└── [Subscriptions would follow same pattern]

src/Ignixa.Domain/
├── Models/                       ← Domain models (no dependencies)
├── Abstractions/                 ← Interfaces (IFhirDataStore, etc.)
└── Exceptions/

src/Ignixa.DataLayer.*/
├── Implementation per storage    ← SQL EF, FileSystem, etc.
└── Subscription persistence here
```

### Key Architectural Decisions for Ignixa

#### 1. **Resource Representation**
- **Current**: All FHIR resources as `ResourceJsonNode` (JSON-based, not POCOs)
- **Subscription**: Follow same pattern - no separate SubscriptionInfo class
- **Advantage**: Supports multiple FHIR versions (R4, R4B, R5) without changing code

#### 2. **Event Detection**
- **Current**: Direct handler intercepts (no event sourcing)
- **Option A** (Simpler): Hook in `CreateOrUpdateResourceHandler.cs` after resource persisted
  - Check: Is this a resource matching any active subscription?
  - If yes: Queue `SubscriptionProcessingJob`

- **Option B** (Advanced): Event sourcing with event log
  - Decouple notification processing from main request
  - Better for high-volume scenarios

#### 3. **Filter Evaluation**
- **Current**: Search parameters via `SearchParameterIndexing`
- **Subscription filters**: Likely need FHIRPath expressions or simple criteria strings
- **Option**: Reuse existing search parsing infrastructure for common filters (Patient?name=X)
- **Complex filters**: Delegate to DurableTask job with full FHIRPath evaluation

#### 4. **Job Queue Integration**
- **Current**: Uses DurableTask for orchestrations (Export/Import)
- **Subscriptions**: Same pattern - `SubscriptionProcessingJob : IJob`
- **Watchdog equivalent**: Ignixa could use Timer-based background service or DurableTask retry loop

#### 5. **Multi-Tenancy**
- **Current**: All resources tenant-aware via `ResourceJsonNode.TenantId`
- **Subscriptions**: Must also track tenant context
- **Behavior**: Subscription in tenant A only matches resources in tenant A
- **Simplification**: Subscriptions don't cross tenant boundaries

#### 6. **Authorization**
- **Current**: `TenantResolutionMiddleware` enforces tenant isolation
- **Subscriptions**: When delivering notifications, must respect subscriber's original permissions
- **Implementation**: Store subscriber's context, re-validate before including each resource in bundle
- **Gotcha**: Don't leak resources the subscriber shouldn't see due to permission changes

---

## fhir-candle: Modern Reference Implementation

Located in: `ThirdParty/fhir-candle/`

**Purpose**: Study alternative, modern approach to subscriptions.

### Architecture Highlights

- **Language**: C# (.NET 8)
- **Storage**: Abstracted (SQL, FileSystem, in-memory)
- **Real-time**: WebSocket support for live subscriptions
- **Multi-version**: R4, R4B, R5 support
- **Lightweight**: Minimal dependencies, focused on spec compliance

### Key Differences from Microsoft Implementation

| Aspect | Microsoft | fhir-candle |
|--------|-----------|------------|
| Event Detection | SQL watchdog lease | Event sink pattern |
| Storage | SQL Server required | Pluggable |
| Processing | DurableTask + Watchdog | Simple background service |
| Channel Retry | None (best-effort) | Server-configurable retry |
| Topic Definition | Config-based | FHIR SubscriptionTopic resources |

### Lessons for Ignixa

1. **Event Sink Pattern**: Simple event handler that gets called on resource create/update
   - Easier to understand than SQL watchdogs
   - Fits Ignixa's direct-hook architecture better

2. **Minimal Retry Logic**: Built-in exponential backoff
   - Don't need full DurableTask orchestration for simple delivery

3. **WebSocket Support**: Real-time push to browsers
   - Not needed initially, but architecture should support it

4. **Resource-based Topics**: SubscriptionTopic as queryable resources (R5)
   - More discoverable than config-based approach
   - Better UX for API consumers

---

## Implementation Roadmap for Ignixa

### Multi-Version Adapter Strategy (Critical)

**Before Phase 1 starts**, establish the multi-version adapter pattern:

**Architecture Setup**:
```
Core Infrastructure:
  1. ISubscriptionAdapter interface (version-agnostic protocol)
     - R4SubscriptionAdapter (criteria-based)
     - R4BSubscriptionAdapter (criteria-based, like R4)
     - R5SubscriptionAdapter (topic-based)

  2. ISubscriptionMatcher (version-agnostic, core engine)
     - Single implementation for all versions
     - Uses internal SubscriptionInfo (shared model)

  3. ISubscriptionNotificationBundleBuilder
     - R4BundleBuilder (custom extensions for status)
     - R5BundleBuilder (SubscriptionStatus as entry[0])

  4. SubscriptionAdapterFactory
     - Resolves adapter based on tenant FHIR version
     - Called in endpoints and background jobs
```

**Registration Pattern**:
```csharp
// In Program.cs, register adapters per version
services.Add<ISubscriptionAdapter>(c => c.GetRequiredService<IFhirVersionProvider>().Version switch
{
    FhirVersion.R4 => new R4SubscriptionAdapter(...),
    FhirVersion.R4B => new R4BSubscriptionAdapter(...),
    FhirVersion.R5 => new R5SubscriptionAdapter(...),
    _ => throw new NotSupportedException()
});

services.Add<ISubscriptionNotificationBundleBuilder>(c => c.GetRequiredService<IFhirVersionProvider>().Version switch
{
    FhirVersion.R4 => new R4BundleBuilder(...),
    FhirVersion.R4B => new R4BundleBuilder(...),
    FhirVersion.R5 => new R5BundleBuilder(...),
    _ => throw new NotSupportedException()
});
```

**Decision**: Adapters are **tenant-scoped** (not global). FHIR version comes from tenant configuration, retrieved via `IFhirVersionProvider` in request context.

---

### Phase 1: Foundation + Multi-Version Support (Weeks 1-2)

**Goal**: Core subscription CRUD + validation with version adapters

**Tasks**:
1. Create version adapter infrastructure (above)
2. Add `Subscription` resource type to `Ignixa.Specification` (R4 + R5)
3. Create domain models in `Ignixa.Domain`:
   - `SubscriptionInfo` (shared, version-agnostic internal model)
   - `ISubscriptionCriteria` (marker for criteria vs topic)
4. Add search parameters for Subscription resource
5. Create endpoints in `SubscriptionEndpoints.cs`:
   - `POST /tenant/{id}/Subscription` - Create (uses adapter for validation)
   - `GET /tenant/{id}/Subscription/{id}` - Read
   - `PUT /tenant/{id}/Subscription/{id}` - Update status (uses adapter for version rules)
   - `DELETE /tenant/{id}/Subscription/{id}` - Delete
   - `GET /tenant/{id}/Subscription` - Search

**Multi-Version Specifics**:
- R4 clients send `Subscription.criteria` field → R4Adapter validates and stores as-is
- R5 clients send `Subscription.topic` field → R5Adapter validates and stores as-is
- Both stored in database exactly as received (no normalization)
- Validation uses version-specific rules

**Tests**:
- R4 Subscription CRUD with criteria validation
- R5 Subscription CRUD with topic validation
- Authorization checks (version-agnostic)
- Multi-tenant isolation
- Cross-version tenant separation (R4 tenant ≠ R5 tenant)

**NOT Included**:
- Actual notifications
- Job processing
- Any background services

### Phase 2: Event Routing (Weeks 3-4)

**Goal**: Detect matching resources, queue notifications

**Tasks**:
1. Add `ISubscriptionMatcher` interface to evaluate subscription filters
2. Create pipeline behavior: `SubscriptionNotificationBehavior`
   - Hooks into `CreateOrUpdateResourceHandler`
   - After resource persisted: find matching subscriptions
   - Queue `SubscriptionProcessingJob` for each match
3. Add `SubscriptionJobDefinition` model to app
4. Register job in DurableTask infrastructure

**Tests**:
- Subscription filter matching
- Job queueing behavior
- Multi-subscription matching

**NOT Included**:
- Actual channel delivery
- Job execution
- Background processing

### Phase 3: Channel Delivery (Weeks 5-6)

**Goal**: Implement at least REST-hook channel

**Tasks**:
1. Create `ISubscriptionChannel` interface
2. Implement `RestHookChannel`: HTTP POST delivery
   - Build Bundle with SubscriptionStatus + matching resources
   - Send via HttpClient
   - Handle handshake/heartbeat
3. Add `SubscriptionChannelFactory` for resolution
4. Implement `SubscriptionProcessingJob.ExecuteAsync()`
   - Called by DurableTask runtime
   - Invokes channel delivery

**Tests**:
- REST-hook delivery
- Bundle construction
- Error handling

**Future Channels**:
- WebSocket (real-time)
- Email (batched)
- Storage/EventGrid (audit)

### Phase 4: Background Processing (Weeks 7-8)

**Goal**: Complete end-to-end subscriptions

**Tasks**:
1. Add background job processor (similar to Export/Import orchestration)
2. Implement `SubscriptionProcessor` DurableTask orchestration
   - Poll job queue
   - Execute `SubscriptionProcessingJob`
   - Handle retries / errors
3. Add monitoring/heartbeat logic
4. Implement `SubscriptionManager` for status tracking

**Tests**:
- Full notification flow (create sub → create resource → delivery)
- Error recovery
- Heartbeat/keepalive

**Operational Requirements**:
- Job queue monitoring
- Notification delivery metrics
- Error logging / alerting

### Phase 5: Advanced Features (Future)

**Optional enhancements**:
1. **SubscriptionTopic Resources** - Discoverable topic definitions
2. **WebSocket Channel** - Real-time push to browsers
3. **Advanced Filters** - FHIRPath expression evaluation
4. **Retry Policies** - Configurable backoff strategies
5. **Audit Logging** - Track all notifications, delivery attempts
6. **Dashboard** - Subscription status UI

---

## Technical Decisions

### Decision 1: Event Detection Model

**Options**:
- **A**: Pipeline behavior (hook in handlers) - ✅ **Selected**
- **B**: Event sourcing with separate event log
- **C**: Database triggers (SQL-specific, not portable)

**Rationale**:
- Fits Ignixa's existing architecture
- Simple to understand and debug
- Resource availability (already in handler context)
- No additional database tables needed
- Works across all storage backends

### Decision 2: Filter Evaluation

**Options**:
- **A**: Use existing search parameter indexing - Partial ✅ **Selected**
- **B**: FHIRPath expressions (complex, slow)
- **C**: Simple criteria string matching (e.g., "Patient?name=John")

**Rationale**:
- Start simple: Support criteria-based filters like "Patient?name=John"
- Leverage Ignixa.Search parsing infrastructure
- FHIRPath can be added in Phase 5 if needed
- Most common use cases don't require complex expressions

### Decision 3: Job Queue Backend

**Options**:
- **A**: DurableTask (already in Ignixa) - ✅ **Selected**
- **B**: Azure Service Bus (requires Azure)
- **C**: Simple in-memory queue (no persistence)

**Rationale**:
- Consistent with Export/Import pattern
- Handles distributed processing
- Persists jobs (survives restarts)
- Multi-worker support (lease-based)

### Decision 4: Subscription Persistence

**Options**:
- **A**: Store as regular FHIR resources (like Patient, etc.) - ✅ **Selected**
- **B**: Dedicated Subscription table (faster, but less flexible)

**Rationale**:
- Consistent with other FHIR resources
- Reuses existing CRUD/search infrastructure
- Supports auditing via _history
- Simpler code (no special-case logic)

### Decision 5: Tenant Isolation

**Options**:
- **A**: Subscriptions are tenant-scoped (per tenant) - ✅ **Selected**
- **B**: Cross-tenant subscriptions (complex, rare)

**Rationale**:
- Simplifies security model
- Matches current Ignixa architecture
- Most real-world use cases are single-tenant
- Can be extended in future if needed

---

## Integration Points Checklist

### Domain Layer
- [ ] Add Subscription resource type definition
- [ ] Add SubscriptionStatus model (for notifications)
- [ ] Define subscription status machine (requested → active → error → off)
- [ ] Add ISubscriptionMatcher interface

### Application Layer
- [ ] Create CreateSubscriptionCommand/Handler
- [ ] Create UpdateSubscriptionCommand/Handler
- [ ] Create DeleteSubscriptionCommand/Handler
- [ ] Create SearchSubscriptionsQuery/Handler
- [ ] Add SubscriptionNotificationBehavior (pipeline hook)
- [ ] Add SubscriptionProcessingJob (DurableTask job)
- [ ] Create ISubscriptionChannel interface + RestHookChannel
- [ ] Create SubscriptionChannelFactory

### API Layer
- [ ] Create SubscriptionEndpoints.cs
- [ ] Add routes: Create, Read, Update, Delete, Search
- [ ] Add authorization checks (subscription creator context)
- [ ] Extend FhirEndpoints.cs to include Subscription routes

### Search Layer
- [ ] Add subscription search parameters (status, channel-type, criteria, topic, etc.)
- [ ] Ensure Subscription type is searchable

### DataLayer
- [ ] Ensure Subscription resources persist like other resources
- [ ] No additional tables needed (stores as JSON like others)

### BackgroundOperations
- [ ] Add SubscriptionProcessingJob
- [ ] Create SubscriptionProcessor orchestration
- [ ] Add job queue integration

---

## Risk Assessment

### High Risk

**Risk**: Notification delivery consistency
- **Issue**: Best-effort delivery (per spec) may lose notifications
- **Mitigation**: Document limitations, add retry logic in job processing
- **Testing**: Simulate network failures, verify job retry behavior

**Risk**: Performance impact on main request path
- **Issue**: Subscription matching on every create/update
- **Mitigation**: Optimize matcher (index active subscriptions), run async if needed
- **Testing**: Benchmark with/without subscriptions

### Medium Risk

**Risk**: Authorization leaks in notifications
- **Issue**: Subscriber receives resources they can no longer read
- **Mitigation**: Re-validate subscriber permissions when building bundle
- **Testing**: Test permission revocation scenarios

**Risk**: Filter expression complexity
- **Issue**: User creates subscription with invalid/complex criteria
- **Mitigation**: Validate on creation, provide clear error messages
- **Testing**: Test various filter syntax, error messages

### Low Risk

**Risk**: Multi-tenancy complexity
- **Issue**: Cross-tenant data leaks
- **Mitigation**: All subscriptions scoped by tenant context
- **Testing**: Multi-tenant tests with resource isolation

**Risk**: Storage size growth
- **Issue**: Subscriptions + historical notifications accumulate
- **Mitigation**: Implement cleanup jobs, configure retention policies
- **Future**: Add data cleanup as operational task

---

## Example: REST-Hook Delivery Flow

### R4 Example (Criteria-Based)

```
1. User Creates Subscription (R4)
   POST /tenant/1/Subscription
   {
     "resourceType": "Subscription",
     "status": "requested",
     "reason": "Monitor new patients",
     "criteria": "Patient",
     "channel": {
       "type": "rest-hook",
       "endpoint": "https://client.example.com/webhook",
       "header": ["Authorization: Bearer token123"]
     }
   }
   ↓
   SubscriptionEndpoints handles POST
   ↓
   CreateSubscriptionCommand via MediatR
   ↓
   SubscriptionValidator checks:
     - Topic exists
     - Endpoint is reachable (handshake)
     - User authorized
   ↓
   CreateSubscriptionHandler persists Subscription resource
   ↓
   Return 201 Created

2. User Creates Patient (matches subscription filter)
   POST /tenant/1/Patient
   {
     "resourceType": "Patient",
     "name": [{"given": ["John"]}]
   }
   ↓
   CreateResourceHandler persists Patient
   ↓
   SubscriptionNotificationBehavior (pipeline hook)
     - Query: Which subscriptions match criteria "Patient"?
     - Find: Subscription 123 (criteria=Patient, matches)
     - Queue: SubscriptionProcessingJob(subscription=123, resource=Patient/xyz)
   ↓
   Return 201 Created to client

3. Background Job Processing (async, DurableTask)
   SubscriptionProcessor checks job queue
   ↓
   Finds: SubscriptionProcessingJob(subscription=123, resource=Patient/xyz)
   ↓
   Calls SubscriptionProcessingJob.ExecuteAsync()
     - Load Patient/xyz from storage
     - Build Bundle (R4 style - custom extension for status):
       - Bundle.type = "subscription-notification"
       - entry[0] = Patient (matched resource)
       - (Alternative R5: Add SubscriptionStatus as entry[0])
     - Get RestHookChannel
     - Invoke PublishAsync(bundle)
   ↓
   RestHookChannel:
     - POST to https://client.example.com/webhook
     - Sends Bundle as JSON with Authorization header
     - Returns on success

4. Client Receives Notification
   POST https://client.example.com/webhook (from server)
   Body: Subscription-notification Bundle
   Headers: Authorization: Bearer token123
   ↓
   Client processes Bundle and acknowledges with 200 OK
   ↓
   Server marks job complete

5. Subscription Expiration
   Server background job checks for expired subscriptions (end < now)
   ↓
   Transitions Subscription.status from "active" to "off"
   ↓
   Optionally sends final notification to client

6. Heartbeat (periodic keepalive)
   SubscriptionProcessor queues: HeartbeatJob(subscription=123) every 30 seconds
   ↓
   RestHookChannel.PublishHeartBeatAsync()
     - Sends light notification indicating subscription is still active
     - Prevents client timeout
     - R5: SubscriptionStatus.type = 'heartbeat'
```

---

## References

### FHIR Specification
- **R4**: https://www.hl7.org/fhir/subscriptions.html
- **R5**: https://hl7.org/fhir/R5/subscriptions.html
- **SubscriptionTopic (R5)**: https://hl7.org/fhir/R5/subscriptiontopic.html

### Reference Implementations
- **Microsoft FHIR Server**: `ThirdParty/Subscriptions/Microsoft.Health.Fhir.*`
- **fhir-candle**: `ThirdParty/fhir-candle/`
- **HAPI FHIR**: https://hapifhir.io/hapi-fhir/docs/server_jpa/subscriptions.html

### Related Ignixa ADRs
- `ADR-2500-master-roadmap.md` - Overall phase structure
- `ADR-2523-multi-tenancy.md` - Tenant isolation patterns
- Existing background operations (Export/Import)

---

## Questions for Clarification

1. **R4 vs R5**: Should we support both R4 (legacy) and R5 (new) subscription formats?
   - R5 has SubscriptionTopic as proper resource
   - R4 uses criteria string (less structured)
   - **Recommendation**: Start with R4 for MVP, add R5 in future

2. **Heartbeat Frequency**: How often should subscriptions send keepalive?
   - Default: 30 seconds (configurable)
   - Tradeoff: More frequent = better responsiveness, higher load

3. **Filter Complexity**: What filter syntax is required?
   - Simple: "Patient?name=John" (search parameters)
   - Complex: FHIRPath expressions
   - **Recommendation**: Start simple, add FHIRPath in Phase 5 if needed

4. **Notification Content**: Should we support payload content options?
   - `empty` - Status only
   - `id-only` - Just resource IDs
   - `full-resource` - Complete resource
   - **Recommendation**: Start with full-resource, add content options in Phase 2

5. **Channel Security**: How to secure REST-hook endpoints?
   - OAuth2 token in request header?
   - HMAC signature?
   - Client certificate?
   - **Recommendation**: Support bearer token header initially

---

## Conclusion

FHIR Subscriptions are a well-specified, mature feature with proven implementations. The Microsoft reference implementation provides a solid pattern, while fhir-candle shows simpler alternatives.

Ignixa is well-positioned to implement subscriptions by:

1. **Leveraging existing DurableTask infrastructure** (already used for Export/Import)
2. **Following current architecture patterns** (pipeline behaviors, handlers, endpoints)
3. **Using JSON-based resource representation** (fits R4/R4B/R5 multi-version support)
4. **Respecting multi-tenancy** (all subscriptions scoped by tenant)

**Start with REST-hook channel and simple filter matching, expand to advanced features in future phases.**

The investigation code and patterns are ready for implementation when Phase 23 begins.
