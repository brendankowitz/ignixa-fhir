namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
