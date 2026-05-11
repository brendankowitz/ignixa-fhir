using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests.Integration;

[Collection("IntegrationTests")]
public class PreviewCommandIntegrationTests
{
    private static readonly string Views = Path.Combine(AppContext.BaseDirectory, "Fixtures", "views");
    private static readonly string Fhir  = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fhir");

    [Fact]
    public async Task GivenViewFileWithNoInput_WhenPreview_ThenExitsZero()
    {
        var vdPath = Path.Combine(Views, "patient-demographics.json");
        var cmd    = PreviewCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", vdPath]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task GivenViewFileWithInput_WhenPreview_ThenExitsZero()
    {
        var vdPath = Path.Combine(Views, "patient-demographics.json");
        var inPath = Path.Combine(Fhir,  "Patient.ndjson");
        var cmd    = PreviewCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", vdPath, "--input", inPath, "--rows", "2"]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task GivenViewWithVariable_WhenPreviewWithVar_ThenSampleUsesProvidedValue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var vdPath = Path.Combine(tempDir, "patient-variable.json");
        var inPath = Path.Combine(Fhir, "Patient.ndjson");
        await File.WriteAllTextAsync(vdPath,
            """
            {
              "resourceType": "ViewDefinition",
              "resource": "Patient",
              "constant": [{ "name": "myTag", "valueString": "default" }],
              "select": [{
                "column": [
                  { "name": "id", "path": "id" },
                  { "name": "tag", "path": "%myTag" }
                ]
              }]
            }
            """);
        var cmd = PreviewCommand.Create(new R4CoreSchemaProvider(), "r4");
        var originalOut = Console.Out;
        using var output = new StringWriter();
        Environment.ExitCode = 0;

        try
        {
            Console.SetOut(output);
            var parseResult = cmd.Parse(["--views", vdPath, "--input", inPath, "--var", "myTag=from-cli"]);
            parseResult.Errors.ShouldBeEmpty();
            await parseResult.InvokeAsync();

            Environment.ExitCode.ShouldBe(0);
            output.ToString().ShouldContain("from-cli");
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.ExitCode = 0;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GivenViewsDir_WhenPreviewDir_ThenExitsZero()
    {
        var cmd = PreviewCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", Views]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }
}
