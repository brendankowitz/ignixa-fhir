using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests;

public class ConsoleEncodingTests
{
    [Fact]
    public void GivenConsoleOutput_WhenEnablingUtf8_ThenNeverThrowsAndUsesUtf8WhenApplied()
    {
        var original = Console.OutputEncoding;
        try
        {
            // Must never throw, regardless of whether a console is attached.
            var applied = Should.NotThrow(() => Program.TryEnableUtf8ConsoleOutput());

            // When a console was available the encoding is now UTF-8; when fully redirected the
            // assignment is swallowed and reported as not-applied — both are valid outcomes.
            if (applied)
            {
                Console.OutputEncoding.WebName.ShouldBe("utf-8");
            }
        }
        finally
        {
            try { Console.OutputEncoding = original; } catch { /* restore is best-effort */ }
        }
    }
}
