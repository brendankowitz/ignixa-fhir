using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexTargetCompletion(
    ReindexParameterDefinition Target,
    bool Success,
    long ResourcesIndexed,
    TimeSpan Duration,
    string? ErrorMessage);
