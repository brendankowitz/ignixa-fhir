using Ignixa.TestScript.Client;

namespace Ignixa.TestScript.Locust.Tests.Contracts;

/// <summary>
/// A deterministic <see cref="ITestRequestProvider"/> for the cross-language runtime contract. It never
/// performs any I/O: it records every outbound <see cref="TestRequest"/> in order and answers each with the
/// next queued <see cref="TestResponse"/>, mirroring the Python <c>FakeClient</c> response queue exactly.
/// A queued <see cref="Exception"/> is thrown instead of returned, modelling a transport failure the same way
/// the Python fake raises a queued exception.
/// </summary>
public sealed class QueuedTestRequestProvider : ITestRequestProvider
{
    private readonly Queue<object> _queue;
    private readonly List<TestRequest> _requests = [];

    public QueuedTestRequestProvider(IEnumerable<object> queuedResponses)
    {
        ArgumentNullException.ThrowIfNull(queuedResponses);
        _queue = new Queue<object>(queuedResponses);
    }

    /// <summary>The outbound requests captured in the exact order the evaluator issued them.</summary>
    public IReadOnlyList<TestRequest> Requests => _requests;

    public Task<TestResponse> ExecuteAsync(TestRequest request, CancellationToken cancellationToken)
    {
        _requests.Add(request);

        if (_queue.Count == 0)
        {
            throw new InvalidOperationException(
                $"No queued response for outbound request {request.Method.Method} {request.Url}; " +
                "the contract's 'responses' array must supply one response per outbound HTTP attempt.");
        }

        object next = _queue.Dequeue();
        return next switch
        {
            TestResponse response => Task.FromResult(response),
            Exception transportError => throw transportError,
            _ => throw new InvalidOperationException(
                $"Unsupported queued response of type '{next.GetType().Name}'."),
        };
    }
}
