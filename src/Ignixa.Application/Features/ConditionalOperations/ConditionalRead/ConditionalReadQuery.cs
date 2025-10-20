using System;
using Medino;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalRead;

/// <summary>
/// Query for conditional read operations (If-None-Match, If-Modified-Since).
/// FHIR R4 Section 3.1.0.1.1 + RFC 7232 (HTTP Conditional Requests).
/// </summary>
public record ConditionalReadQuery(
    int TenantId,
    string ResourceType,
    string ResourceId,
    string? IfNoneMatch = null,  // ETag value from If-None-Match header
    DateTimeOffset? IfModifiedSince = null)  // Date from If-Modified-Since header
    : IRequest<ConditionalReadResult>;
