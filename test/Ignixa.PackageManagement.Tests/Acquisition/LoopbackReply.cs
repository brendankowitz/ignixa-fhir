namespace Ignixa.PackageManagement.Tests.Acquisition;

internal sealed record LoopbackReply(
    int Status, byte[] Body, bool Chunked = true, string Headers = "",
    int? BytesToSend = null, bool Truncate = false, bool Stall = false, TaskCompletionSource? BodySent = null);
