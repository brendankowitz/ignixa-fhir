namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

/// <param name="Id">The failed resource, or <c>null</c> when a whole range failed after its retries.</param>
public sealed record ReindexFailedResource(string ResourceType, string? Id, string Reason);
