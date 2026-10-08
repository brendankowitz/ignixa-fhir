namespace Ignixa.Application.Features.Reindex;

public interface IReindexAvailability
{
    Task<ReindexAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);
}
