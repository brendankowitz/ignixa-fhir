using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization;

namespace Ignixa.Application.BackgroundOperations.Export;

public sealed class ExportGroupResolver(
    IFhirRepositoryFactory repositoryFactory,
    IFhirBaseUriProvider baseUriProvider)
{
    public async Task<IReadOnlySet<string>> ResolvePatientIdsAsync(
        int tenantId,
        string groupId,
        IFhirSchemaProvider schema,
        CancellationToken cancellationToken)
    {
        var repository = await repositoryFactory.GetRepositoryAsync(tenantId, cancellationToken);
        var referenceParser = new ReferenceSearchValueParser(schema, baseUriProvider);
        var patients = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(groupId);

        while (pending.TryDequeue(out var currentGroupId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(currentGroupId))
            {
                continue;
            }

            var resource = await repository.GetAsync(new ResourceKey("Group", currentGroupId), cancellationToken)
                ?? throw new ResourceNotFoundException($"Group/{currentGroupId} not found");
            var group = JsonSourceNodeFactory.Parse(resource.ResourceBytes).ToElement(schema);
            var membershipPredicate = schema.Version is FhirVersion.R5 or FhirVersion.R6
                ? "type = 'person' and membership = 'enumerated'"
                : "type = 'person' and actual = true";
            if (!group.IsTrue(membershipPredicate))
            {
                throw new BadRequestException($"Group/{currentGroupId} must be an actual group of patients.");
            }

            foreach (var member in group.Select("member.where(inactive.empty() or inactive = false).entity"))
            {
                if (member.Scalar("reference") is not string reference)
                {
                    throw new BadRequestException($"Group/{currentGroupId} has a member without a resolvable reference.");
                }

                var parsed = referenceParser.Parse(reference);
                if (parsed.BaseUri is not null || parsed.ResourceType is not ("Patient" or "Group"))
                {
                    throw new BadRequestException($"Group/{currentGroupId} members must reference local Patients or Groups.");
                }

                if (parsed.ResourceType == "Group")
                {
                    pending.Enqueue(parsed.ResourceId);
                }
                else
                {
                    patients.Add(parsed.ResourceId);
                }
            }
        }

        return patients;
    }
}
