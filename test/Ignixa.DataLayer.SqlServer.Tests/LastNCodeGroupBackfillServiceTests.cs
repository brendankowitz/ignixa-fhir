using Ignixa.DataLayer.SqlServer;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class LastNCodeGroupBackfillServiceTests
{
    [Theory]
    [InlineData(long.MinValue, long.MinValue, 1, long.MinValue)]
    [InlineData(long.MinValue, long.MinValue + 2, 2, long.MinValue + 1)]
    [InlineData(-1, long.MaxValue, int.MaxValue, 2_147_483_645)]
    [InlineData(long.MaxValue - 1, long.MaxValue, 3, long.MaxValue)]
    [InlineData(long.MaxValue, long.MaxValue, int.MaxValue, long.MaxValue)]
    public void GivenABigIntRange_WhenTheInclusiveBatchEndIsCalculated_ThenItIsCappedWithoutOverflow(
        long start,
        long highWater,
        int batchSize,
        long expectedEnd)
    {
        // Act
        long end = LastNCodeGroupBackfillService.CalculateBatchEnd(start, highWater, batchSize);

        // Assert
        end.ShouldBe(expectedEnd);
    }
}
