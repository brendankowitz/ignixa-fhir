namespace Ignixa.TestScript.Client;

public interface ITestRequestProvider
{
    Task<TestResponse> ExecuteAsync(TestRequest request, CancellationToken cancellationToken);
}
