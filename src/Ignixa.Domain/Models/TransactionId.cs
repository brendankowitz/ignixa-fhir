namespace Ignixa.Domain.Models;

/// <summary>
/// Unique identifier for a FHIR transaction.
/// Uses Unix timestamp in milliseconds for ordering and debugging.
/// </summary>
public readonly record struct TransactionId(long Value)
{
    /// <summary>
    /// Generates a new transaction ID based on current timestamp.
    /// </summary>
    public static TransactionId Generate() =>
        new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

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
