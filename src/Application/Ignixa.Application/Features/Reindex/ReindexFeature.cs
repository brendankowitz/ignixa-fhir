using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Reindex;

public sealed class ReindexFeature : IPackageFeature
{
    private static readonly string[] Operations = ["reindex"];

    public string PackageId => "ignixa.reindex";

    public IReadOnlyList<string> SystemOperations => Operations;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<string>? SupportedFhirVersions => null;
}
