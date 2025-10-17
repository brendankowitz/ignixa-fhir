namespace Ignixa.Domain.Models;

/// <summary>
/// Captures HTTP request metadata for audit and transaction replay purposes.
/// Based on legacy ResourceWrapper.Request design.
/// </summary>
public record ResourceRequest(
    string Method,           // PUT, POST, DELETE, etc.
    string Url,             // Patient/123, Patient, etc.
    string? IfMatch = null,
    string? IfNoneExist = null,
    string? IfModifiedSince = null);
