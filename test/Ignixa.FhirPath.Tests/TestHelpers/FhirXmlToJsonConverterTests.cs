using System.Text.Json;

namespace Ignixa.FhirPath.Tests.TestHelpers;

public class FhirXmlToJsonConverterTests
{
    private static readonly string ProjectDirectory = GetProjectDirectory();
    private static readonly string TestDataPath = Path.Combine(ProjectDirectory, "TestData", "fhir-test-cases", "r4", "examples");

    private static string GetProjectDirectory()
    {
        var assemblyLocation = typeof(FhirXmlToJsonConverterTests).Assembly.Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation)!;

        var current = new DirectoryInfo(assemblyDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Ignixa.FhirPath.Tests.csproj")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find project directory");
    }

    [Fact]
    public void GivenPatientXml_WhenConvertingToJson_ThenReturnsValidJson()
    {
        var xmlPath = Path.Combine(TestDataPath, "patient-example.xml");
        var xmlContent = File.ReadAllText(xmlPath);

        var jsonContent = FhirXmlToJsonConverter.ConvertXmlToJson(xmlContent);

        jsonContent.ShouldNotBeNull();
        jsonContent.ShouldNotBeEmpty();

        using var doc = JsonDocument.Parse(jsonContent);
        doc.RootElement.GetProperty("resourceType").GetString().ShouldBe("Patient");
        doc.RootElement.GetProperty("id").GetString().ShouldBe("example");
    }

    [Fact]
    public void GivenXmlFile_WhenLoadingAsJson_ThenConvertsAndReturnsJson()
    {
        var xmlPath = Path.Combine(TestDataPath, "patient-example.xml");

        var jsonContent = FhirXmlToJsonConverter.LoadResourceAsJson(xmlPath);

        jsonContent.ShouldNotBeNull();
        jsonContent.ShouldNotBeEmpty();

        using var doc = JsonDocument.Parse(jsonContent);
        doc.RootElement.GetProperty("resourceType").GetString().ShouldBe("Patient");
    }

    [Fact]
    public void GivenJsonFile_WhenLoadingAsJson_ThenPassesThrough()
    {
        var jsonPath = Path.Combine(TestDataPath, "patient-example.json");
        var originalContent = File.ReadAllText(jsonPath);

        var jsonContent = FhirXmlToJsonConverter.LoadResourceAsJson(jsonPath);

        jsonContent.ShouldBe(originalContent);

        using var doc = JsonDocument.Parse(jsonContent);
        doc.RootElement.GetProperty("resourceType").GetString().ShouldBe("Patient");
    }

    [Fact]
    public void GivenConvertedXml_WhenComparingToOriginalJson_ThenHasSameStructure()
    {
        var xmlPath = Path.Combine(TestDataPath, "patient-example.xml");
        var jsonPath = Path.Combine(TestDataPath, "patient-example.json");

        var convertedJson = FhirXmlToJsonConverter.LoadResourceAsJson(xmlPath);
        var originalJson = FhirXmlToJsonConverter.LoadResourceAsJson(jsonPath);

        using var convertedDoc = JsonDocument.Parse(convertedJson);
        using var originalDoc = JsonDocument.Parse(originalJson);

        convertedDoc.RootElement.GetProperty("resourceType").GetString()
            .ShouldBe(originalDoc.RootElement.GetProperty("resourceType").GetString());
        convertedDoc.RootElement.GetProperty("id").GetString()
            .ShouldBe(originalDoc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public void GivenNullXmlContent_WhenConverting_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => FhirXmlToJsonConverter.ConvertXmlToJson(null!));
    }

    [Fact]
    public void GivenEmptyXmlContent_WhenConverting_ThenThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => FhirXmlToJsonConverter.ConvertXmlToJson(string.Empty));
    }

    [Fact]
    public void GivenWhitespaceXmlContent_WhenConverting_ThenThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => FhirXmlToJsonConverter.ConvertXmlToJson("   "));
    }

    [Fact]
    public void GivenInvalidXml_WhenConverting_ThenThrowsInvalidOperationException()
    {
        var invalidXml = "<Patient><invalid></Patient>";

        var exception = Should.Throw<InvalidOperationException>(() =>
            FhirXmlToJsonConverter.ConvertXmlToJson(invalidXml));

        exception.Message.ShouldContain("Failed to convert FHIR XML to JSON");
    }

    [Fact]
    public void GivenNullFilePath_WhenLoading_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => FhirXmlToJsonConverter.LoadResourceAsJson(null!));
    }

    [Fact]
    public void GivenEmptyFilePath_WhenLoading_ThenThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => FhirXmlToJsonConverter.LoadResourceAsJson(string.Empty));
    }

    [Fact]
    public void GivenNonExistentFile_WhenLoading_ThenThrowsFileNotFoundException()
    {
        var nonExistentPath = Path.Combine(TestDataPath, "non-existent-file.xml");

        Should.Throw<FileNotFoundException>(() => FhirXmlToJsonConverter.LoadResourceAsJson(nonExistentPath));
    }

    [Fact]
    public void GivenUnsupportedFileExtension_WhenLoading_ThenThrowsInvalidOperationException()
    {
        var tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, "<Patient />");

        try
        {
            var exception = Should.Throw<InvalidOperationException>(() =>
                FhirXmlToJsonConverter.LoadResourceAsJson(tempPath));

            exception.Message.ShouldContain("Unsupported file format");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

}
