namespace Ignixa.Application.Features.Conformance;

public interface ISearchParameterTransitionScheduler
{
    /// <summary>
    /// Starts the durable full-grace transition for <paramref name="hideEventId"/> unless one is already active.
    /// </summary>
    Task ScheduleAsync(long hideEventId, TimeSpan transitionGrace, CancellationToken cancellationToken);
}
