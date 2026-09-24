using System.Threading;

namespace Ignixa.Domain.Models;

/// <summary>
/// Unique identifier for a FHIR transaction.
/// Uses a monotonically increasing, millisecond-based value for ordering and debugging.
/// </summary>
public readonly record struct TransactionId(long Value)
{
    // Two Generate() calls landing in the same clock millisecond used to return the same value.
    // FileBasedFhirRepository names its NDJSON files tx-{TransactionId}.ndjson, so a colliding ID
    // meant the second write silently overwrote the first resource's file while its metadata
    // sidecar kept pointing at the (now wrong) content -- observed as GetAsync throwing "Resource
    // not found in NDJSON file" for a resource that was, in fact, written successfully. Bumping the
    // value forward past the last one generated in this process keeps it monotonic and unique
    // regardless of clock resolution or how fast callers generate IDs back to back.
    private static long _lastGenerated;

    /// <summary>
    /// Generates a new transaction ID based on current timestamp.
    /// </summary>
    public static TransactionId Generate()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        long previous, next;
        do
        {
            previous = Volatile.Read(ref _lastGenerated);
            next = timestamp > previous ? timestamp : previous + 1;
        }
        while (Interlocked.CompareExchange(ref _lastGenerated, next, previous) != previous);

        return new TransactionId(next);
    }

    /// <summary>
    /// Parses a transaction ID from a string.
    /// </summary>
    public static TransactionId Parse(string value) =>
        new(long.Parse(value));

    /// <summary>
    /// Tries to parse a transaction ID from a string.
    /// </summary>
    public static bool TryParse(string value, out TransactionId transactionId)
    {
        if (long.TryParse(value, out var longValue))
        {
            transactionId = new TransactionId(longValue);
            return true;
        }

        transactionId = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
