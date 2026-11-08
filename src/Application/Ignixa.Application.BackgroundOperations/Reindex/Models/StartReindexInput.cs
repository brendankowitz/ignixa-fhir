using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record StartReindexInput(
    string JobId,
    IReadOnlyList<ReindexParameterDefinition> Targets,
    IReadOnlyList<int> TenantIds);
