namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Configuration options for sidecar service integration.
/// </summary>
public class SidecarOptions
{
    public const string SectionName = "Sidecar";

    /// <summary>
    /// Master toggle for sidecar integration. Default: false (use local implementations).
    /// When true, all services (audit, auth, metrics) use gRPC sidecar providers.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// gRPC endpoint for audit sidecar service.
    /// Default: http://127.0.0.1:50051 (localhost only).
    /// Production: Use https:// with valid TLS certificates to protect sensitive audit data.
    /// </summary>
    public string AuditServiceUrl { get; set; } = "http://127.0.0.1:50051";

    /// <summary>
    /// gRPC endpoint for RBAC authorization sidecar service.
    /// Default: http://127.0.0.1:50052 (localhost only).
    /// Production: Use https:// with valid TLS certificates to protect authorization decisions.
    /// </summary>
    public string RbacServiceUrl { get; set; } = "http://127.0.0.1:50052";

    /// <summary>
    /// gRPC endpoint for metrics sidecar service.
    /// Default: http://127.0.0.1:50053 (localhost only).
    /// Production: Use https:// with valid TLS certificates to protect metrics data.
    /// </summary>
    public string MetricsServiceUrl { get; set; } = "http://127.0.0.1:50053";

    /// <summary>
    /// gRPC endpoint for logging sidecar service.
    /// Default: http://127.0.0.1:50054 (localhost only).
    /// Production: Use https:// with valid TLS certificates to protect log data.
    /// </summary>
    public string LoggingServiceUrl { get; set; } = "http://127.0.0.1:50054";

    /// <summary>
    /// gRPC call timeout in seconds. Default: 5.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Whether to enable automatic retry on transient failures. Default: false.
    /// Phase 1: Not implemented. Future: Use Polly retry policies.
    /// </summary>
    public bool EnableRetry { get; set; }

    /// <summary>
    /// Minimum log level to send to sidecar. Default: Information.
    /// Logs below this level are filtered out to reduce sidecar traffic.
    /// </summary>
    public string MinimumLogLevel { get; set; } = "Information";

    /// <summary>
    /// Maximum number of log entries to batch before sending. Default: 100.
    /// </summary>
    public int LogBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time to wait before flushing buffered logs (milliseconds). Default: 1000.
    /// </summary>
    public int LogFlushIntervalMs { get; set; } = 1000;
}
