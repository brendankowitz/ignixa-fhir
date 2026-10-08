using Ignixa.Search.Indexing;
using Medino;

namespace Ignixa.Application.Features.Reindex;

public sealed record ReindexSingleResourceCommand(
    string ResourceType,
    string ResourceId,
    bool Persist) : IRequest<ReindexSingleResourceResult>;

public abstract record ReindexSingleResourceResult;

public sealed record ReindexSingleResourceCompletedResult(
    IReadOnlyList<SearchIndexEntry> Indices,
    bool Conflicted) : ReindexSingleResourceResult;

public sealed record ReindexSingleResourceNotFoundResult : ReindexSingleResourceResult;

public sealed record ReindexSingleResourceDeletedResult : ReindexSingleResourceResult;

public sealed record ReindexSingleResourceProviderUnavailableResult : ReindexSingleResourceResult;
