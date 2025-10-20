namespace Ignixa.Application.Features.ConditionalOperations.ConditionalDelete;

/// <summary>
/// Result of a conditional delete operation.
/// </summary>
public record ConditionalDeleteResult(
    int DeletedCount,  // Number of resources deleted
    int TotalMatches,  // Total number of matches found
    bool IsPartialDelete,  // True if totalMatches > deletedCount
    IReadOnlyList<string> DeletedIds);  // IDs of deleted resources (for verbose OperationOutcome)
