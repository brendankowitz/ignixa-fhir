namespace Ignixa.Application.Features.Reindex;

public sealed record ReindexAvailability(
    ReindexAvailabilityStatus Status,
    int? UnsupportedTenantId = null)
{
    public static ReindexAvailability Available { get; } = new(ReindexAvailabilityStatus.Available);

    public static ReindexAvailability Disabled { get; } = new(ReindexAvailabilityStatus.Disabled);
}
