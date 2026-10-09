using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record StartReindexInput(
    string JobId,
    long TargetEventId,
    IReadOnlyList<ReindexParameterDefinition> Targets,
    IReadOnlyList<int> TenantIds);
