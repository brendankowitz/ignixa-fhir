namespace Ignixa.DataLayer.SqlServer.Tests.Fixtures;

/// <summary>
/// Manually advanced clock for tests that assert on elapsed time. Hand-rolled rather than taken from
/// <c>Microsoft.Extensions.TimeProvider.Testing</c>: that package is not referenced anywhere in this
/// repository, and only <see cref="GetUtcNow"/> is exercised here.
/// </summary>
public sealed class TestTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
    private DateTimeOffset _utcNow = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
}
