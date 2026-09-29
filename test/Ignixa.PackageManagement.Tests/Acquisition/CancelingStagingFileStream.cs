using Microsoft.Win32.SafeHandles;

namespace Ignixa.PackageManagement.Tests.Acquisition;

internal sealed class CancelingStagingFileStream(SafeFileHandle handle, CancellationTokenSource caller)
    : FileStream(handle, FileAccess.Write, 65536, isAsync: false)
{
    internal bool FlushEntered { get; private set; }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        FlushEntered = true;
        await caller.CancelAsync();
        await base.FlushAsync(cancellationToken);
    }
}
