namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexFailedResource(string ResourceType, string Id, string Reason);
