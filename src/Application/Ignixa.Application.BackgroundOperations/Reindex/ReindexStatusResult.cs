using System.Text.Json.Nodes;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexStatusResult(
    string JobId,
    string Status,
    DateTimeOffset QueuedTime,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    DateTimeOffset LastModified,
    bool IsStale,
    string? ErrorMessage,
    JsonNode? Progress,
    JsonNode? Result,
    ReindexJobDefinition? Definition = null);
