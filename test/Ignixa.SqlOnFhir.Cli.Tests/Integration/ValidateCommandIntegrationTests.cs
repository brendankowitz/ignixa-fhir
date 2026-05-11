using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class ValidateCommandIntegrationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private static readonly string Views = Path.Combine(AppContext.BaseDirectory, "Fixtures", "views");

    public ValidateCommandIntegrationTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task GivenValidViewDefinition_WhenValidateSingle_ThenExitsZero()
    {
        var vdPath = Path.Combine(Views, "patient-demographics.json");
        var cmd    = ValidateCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", vdPath]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task GivenNonViewDefinitionJson_WhenValidateSingle_ThenExitsOne()
    {
        var badPath = Path.Combine(_tempDir, "bad.json");
        await File.WriteAllTextAsync(badPath, """{"resourceType":"Patient","id":"p1"}""");
        var cmd = ValidateCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", badPath]).InvokeAsync();

        try { Environment.ExitCode.ShouldBe(1); }
        finally { Environment.ExitCode = 0; }
    }

    [Fact]
    public async Task GivenValidViewsDir_WhenValidateDir_ThenExitsZero()
    {
        var cmd = ValidateCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", Views]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task GivenMixedDir_WhenValidateDir_ThenExitsOne()
    {
        File.Copy(Path.Combine(Views, "patient-demographics.json"), Path.Combine(_tempDir, "valid.json"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "invalid.json"), """{"resourceType":"Patient"}""");
        var cmd = ValidateCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", _tempDir]).InvokeAsync();

        try { Environment.ExitCode.ShouldBe(1); }
        finally { Environment.ExitCode = 0; }
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
