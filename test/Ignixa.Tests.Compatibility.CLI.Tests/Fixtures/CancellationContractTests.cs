using System;
using System.Threading.Tasks;
using Xunit;

namespace CompatibilityContractFixtures;

public class CancellationContractTests
{
    public static TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task CancellationSqlServerJson()
    {
        Started.TrySetResult();
        await Finish.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
