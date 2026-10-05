namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;

/// <summary>
/// Manually advanced clock, so a test can cross the reference-data cache's negative-lookup TTL without
/// waiting for it. Duplicated from the unit test project's fixture of the same name rather than shared:
/// test projects referencing each other is worse than fifteen duplicated lines, and
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> is not referenced anywhere in this repository.
/// </summary>
public sealed class TestTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
    private DateTimeOffset _utcNow = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
}
