using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

public class StrictInputPropagationTests
{
    private readonly PackageExtractor _extractor = new(NullLogger<PackageExtractor>.Instance);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenPendingRead_WhenCancelled_ThenPropagatesTokenAndLeavesInputOpen(bool atFinalDrain)
    {
        byte[] prefix = atFinalDrain ? StrictPackageFixture.Package() : [];
        using var input = new StrictPendingInputStream(prefix);
        using var cancellation = new CancellationTokenSource();
        Task<Exception?> extraction = Record.ExceptionAsync(async () =>
            await _extractor.ExtractStrictAsync(input, new(), cancellation.Token));
        await input.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10));
        extraction.IsCompleted.ShouldBeFalse();

        await cancellation.CancelAsync();
        var failure = (await extraction.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeAssignableTo<OperationCanceledException>();

        failure!.CancellationToken.ShouldBe(cancellation.Token);
        input.BytesRead.ShouldBe(prefix.Length);
        input.Disposed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenPendingRead_WhenIoFails_ThenPropagatesOriginalExceptionAndLeavesInputOpen(bool atFinalDrain)
    {
        byte[] prefix = atFinalDrain ? StrictPackageFixture.Package() : [];
        using var input = new StrictPendingInputStream(prefix);
        Task<Exception?> extraction = Record.ExceptionAsync(async () =>
            await _extractor.ExtractStrictAsync(input, new(), CancellationToken.None));
        await input.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10));
        extraction.IsCompleted.ShouldBeFalse();
        var original = new IOException("synthetic input failure");

        input.Fail(original);
        var failure = (await extraction.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeOfType<IOException>();

        failure.ShouldBeSameAs(original);
        input.BytesRead.ShouldBe(prefix.Length);
        input.Disposed.ShouldBeFalse();
    }
}
