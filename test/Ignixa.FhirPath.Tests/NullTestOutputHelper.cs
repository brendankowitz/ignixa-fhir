using Xunit.Abstractions;

namespace Ignixa.FhirPath.Tests;

internal sealed class NullTestOutputHelper : ITestOutputHelper
{
    public void WriteLine(string message)
    {
    }

    public void WriteLine(string format, params object[] args)
    {
    }
}
