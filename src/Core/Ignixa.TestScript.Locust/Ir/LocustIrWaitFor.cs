namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// Polling configuration used by a compiled operation to wait for an asynchronous response.
/// </summary>
/// <param name="PollingStatusCode">The HTTP status code that indicates the operation is still in progress.</param>
/// <param name="MaxAttempts">The maximum number of polling attempts before giving up.</param>
/// <param name="IntervalMs">The delay, in milliseconds, between polling attempts.</param>
public sealed record LocustIrWaitFor(int PollingStatusCode, int MaxAttempts, int IntervalMs);
