using System.Net.Sockets;
using Shouldly;

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class TlsFixtureLifetimeTests
{
    [Fact]
    public async Task GivenUnexpectedHandshakeFailure_WhenShutdownWinsBeforeThrow_ThenStillSurfacesFailure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new LoopbackHttpsRegistry
        {
            BeforeUnexpectedHandshakeFailure = () =>
            {
                observed.TrySetResult();
                return resume.Task;
            }
        };
        Task? disposing = null;
        try
        {
            await server.Ready;
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", server.BaseUri.Port, timeout.Token);
            await client.GetStream().WriteAsync("GET / HTTP/1.1\r\nHost: localhost\r\n\r\n"u8.ToArray(), timeout.Token);
            await observed.Task.WaitAsync(timeout.Token);
            server.Diagnostics.Count.ShouldBe(1);
            disposing = server.DisposeAsync().AsTask();
        }
        finally
        {
            resume.TrySetResult();
            disposing ??= server.DisposeAsync().AsTask();
        }
        var error = await Should.ThrowAsync<IOException>(() => disposing.WaitAsync(timeout.Token));
        error.Message.ShouldStartWith("Unexpected test TLS handshake failure:");
        error.InnerException.ShouldBeNull();
    }
}
