using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexStatusResult(
    string JobId,
    ReindexJobStatus Status,
    DateTimeOffset QueuedTime,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    DateTimeOffset LastModified,
    bool IsStale,
    string? ErrorMessage,
    ReindexProgress? Progress,
    ReindexJobDefinition? Definition = null);
