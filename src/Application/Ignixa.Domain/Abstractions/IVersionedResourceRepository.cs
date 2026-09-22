using Ignixa.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Reads resources while honoring an explicit ResourceKey.VersionId.
/// Providers without this capability must not be used for vread, even if they support history lists.
/// </summary>
public interface IVersionedResourceRepository
{
    ValueTask<SearchEntryResult?> GetAsync(ResourceKey key, CancellationToken cancellationToken = default);
}
