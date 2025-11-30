using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalRead;

/// <summary>
/// Result of conditional read operation.
/// </summary>
public record ConditionalReadResult(
    SearchEntryResult? Resource,  // Null if not found
    bool NotModified);  // true = 304 Not Modified, false = 200 OK or 404
