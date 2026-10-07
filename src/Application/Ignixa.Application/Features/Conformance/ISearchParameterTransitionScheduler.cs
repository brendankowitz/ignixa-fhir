namespace Ignixa.Application.Features.Conformance;

public interface ISearchParameterTransitionScheduler
{
    Task ScheduleAsync(long hideEventId, TimeSpan transitionGrace, CancellationToken cancellationToken);
}
