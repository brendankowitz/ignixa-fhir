using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class StagingLifetimeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GivenBufferedFileAndCanceledFlush_WhenDisposalAlsoFails_ThenPreservesPrimaryAndSafeSecondaryDiagnostic()
    {
        using var directory = new OwnedCacheDirectory();
        string path = Path.Combine(directory.Path, "owned-flush-fault");
        await File.WriteAllBytesAsync(path, []);
        using var caller = new CancellationTokenSource();
        // A private read-only OS handle deterministically denies the buffered disposal write.
        // This exercises real FileStream I/O at the staging lifetime boundary, not disk exhaustion.
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
#pragma warning disable CA2000 // The production lifetime helper owns the stream.
        var stream = new CancelingStagingFileStream(handle, caller);
#pragma warning restore CA2000
        Exception? failure = await Record.ExceptionAsync(() =>
            VerifiedPackageCache.WriteStagingAsync(stream, "buffered"u8.ToArray(), caller.Token));
        output.WriteLine($"Runtime={Environment.Version}; primary={failure?.GetType().Name}; HResult={failure?.HResult:X8}; flushEntered={stream.FlushEntered}");
        stream.FlushEntered.ShouldBeTrue();
        OperationCanceledException cancellation = Assert.IsAssignableFrom<OperationCanceledException>(failure);
        cancellation.CancellationToken.ShouldBe(caller.Token);
        cancellation.Data[PackageAcquisitionException.CacheCleanupFailureDataKey].ShouldBe(PackageAcquisitionError.CacheFailure);
        cancellation.ToString().ShouldNotContain(path);
        handle.IsClosed.ShouldBeTrue();
        (await File.ReadAllBytesAsync(path)).ShouldBeEmpty();
    }
}
