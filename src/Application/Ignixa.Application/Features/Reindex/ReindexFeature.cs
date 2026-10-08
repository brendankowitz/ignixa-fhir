using Ignixa.Domain.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Metadata.Segments;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Reindex;

public sealed class ReindexFeature(
    IOptions<ReindexOptions> options,
    IFhirRepositoryFactory repositoryFactory)
    : IPackageFeature, ICapabilityContextAwarePackageFeature
{
    private static readonly string[] Operations = ["reindex"];
    private readonly ReindexOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IFhirRepositoryFactory _repositoryFactory =
        repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));

    public string PackageId => "ignixa.reindex";

    public IReadOnlyList<string> SystemOperations => _options.Enabled ? Operations : [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<string>? SupportedFhirVersions => null;

    public async ValueTask<bool> IsAvailableAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        if (context.TenantId is not { } tenantId)
        {
            return true;
        }

        var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, cancellationToken);
        return repository is IReindexStore;
    }
}
