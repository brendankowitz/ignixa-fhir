namespace Ignixa.PackageManagement.Tests.Acquisition;

internal sealed record LoopbackReply(int Status, byte[] Body, bool Chunked = true, string Headers = "");
