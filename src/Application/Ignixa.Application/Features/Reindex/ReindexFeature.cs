using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Reindex;

public sealed class ReindexFeature(
    IReindexAvailability availability)
    : IPackageFeature, ICapabilityContextAwarePackageFeature
{
    private static readonly string[] Operations = ["reindex"];
    private readonly IReindexAvailability _availability =
        availability ?? throw new ArgumentNullException(nameof(availability));

    public string PackageId => "ignixa.reindex";

    public IReadOnlyList<string> SystemOperations => Operations;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<string>? SupportedFhirVersions => null;

    public async ValueTask<bool> IsAvailableAsync(
        CapabilityContext context,
        CancellationToken cancellationToken) =>
        (await _availability.GetAvailabilityAsync(cancellationToken)).Status ==
        ReindexAvailabilityStatus.Available;
}
