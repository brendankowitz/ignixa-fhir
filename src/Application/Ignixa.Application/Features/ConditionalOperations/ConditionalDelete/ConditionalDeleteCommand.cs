using Medino;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalDelete;

/// <summary>
/// Command for conditional delete operation.
/// Supports both single mode (no _count) and multiple mode (with _count).
/// </summary>
public record ConditionalDeleteCommand(
    int TenantId,
    string ResourceType,
    string SearchCriteria,  // Query string parameters (e.g., "identifier=system|value&_count=10")
    int? Count = null,  // _count parameter value (null = single mode)
    string? RequestId = null) : IRequest<ConditionalDeleteResult>;
