using Microsoft.Extensions.Logging;
using Polly;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// HTTP message handler that wraps Polly resilience policies for package downloads.
/// </summary>
public class ResilientHttpMessageHandler : DelegatingHandler
{
    private readonly IAsyncPolicy<HttpResponseMessage> _policy;
    private readonly ILogger<ResilientHttpMessageHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the ResilientHttpMessageHandler class.
    /// </summary>
    /// <param name="innerHandler">Inner HTTP message handler</param>
    /// <param name="logger">Logger for policy events</param>
    public ResilientHttpMessageHandler(
        HttpMessageHandler innerHandler,
        ILogger<ResilientHttpMessageHandler> logger)
        : base(innerHandler)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policy = PackageLoaderResiliencePolicies.CreateCombinedPolicy(logger);
    }

    /// <summary>
    /// Sends an HTTP request with resilience policies applied.
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var context = new Polly.Context { };
        return await _policy.ExecuteAsync(
            async (ctx, ct) =>
            {
                return await base.SendAsync(request, ct);
            },
            context,
            cancellationToken);
    }
}
