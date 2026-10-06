namespace Ignixa.PackageManagement.Tests.Acquisition;

internal sealed class ThrowingAcquisitionStream : MemoryStream
{
    internal bool Disposed { get; private set; }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        throw new IOException("secret response read failure");

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
