using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record OwnedReindexTarget(string JobId, ReindexParameterDefinition Target);
