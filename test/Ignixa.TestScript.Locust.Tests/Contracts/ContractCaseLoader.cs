using System.Text.Json;

namespace Ignixa.TestScript.Locust.Tests.Contracts;

/// <summary>
/// A single shared FHIRPath contract case, deserialized from <c>Contracts/fhirpath-cases.json</c>.
/// </summary>
/// <param name="Name">Stable, human-readable case identifier.</param>
/// <param name="Expression">The FHIRPath expression under test.</param>
/// <param name="Shape">Either <c>boolean</c> (predicate adapter) or <c>scalar</c> (single-value adapter).</param>
/// <param name="ExpectedBoolean">Expected boolean result, meaningful only when <see cref="Shape"/> is <c>boolean</c>.</param>
/// <param name="ExpectedScalar">Expected scalar string (or <see langword="null"/>), meaningful only when <see cref="Shape"/> is <c>scalar</c>.</param>
/// <param name="ResourceJson">The raw JSON of the FHIR resource to evaluate against.</param>
public sealed record ContractCase(
    string Name,
    string Expression,
    string Shape,
    bool ExpectedBoolean,
    string? ExpectedScalar,
    string ResourceJson);

/// <summary>
/// Loads the shared FHIRPath contract file that both the C# reference tests and the Python runtime
/// tests consume. The file is copied next to the test assembly by the project's
/// <c>Contracts\**\*</c> <c>CopyToOutputDirectory</c> item, matching the existing project pattern.
/// </summary>
public static class ContractCaseLoader
{
    /// <summary>The output-relative path to the shared contract, identical to the source-tree path Python loads.</summary>
    public const string ContractRelativePath = "Contracts/fhirpath-cases.json";

    public static IReadOnlyList<ContractCase> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Contracts", "fhirpath-cases.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Shared FHIRPath contract not found at '{path}'. Ensure the Contracts folder is copied to output.",
                path);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement casesElement = document.RootElement;

        List<ContractCase> cases = new(casesElement.GetArrayLength());
        foreach (JsonElement caseElement in casesElement.EnumerateArray())
        {
            string name = caseElement.GetProperty("name").GetString()!;
            string expression = caseElement.GetProperty("expression").GetString()!;
            string shape = caseElement.GetProperty("shape").GetString()!;
            string resourceJson = caseElement.GetProperty("resource").GetRawText();

            JsonElement expected = caseElement.GetProperty("expected");
            bool expectedBoolean = false;
            string? expectedScalar = null;

            if (shape == "boolean")
            {
                expectedBoolean = expected.GetBoolean();
            }
            else if (shape == "scalar")
            {
                expectedScalar = expected.ValueKind == JsonValueKind.Null ? null : expected.GetString();
            }
            else
            {
                throw new InvalidOperationException($"Unknown contract shape '{shape}' for case '{name}'.");
            }

            cases.Add(new ContractCase(name, expression, shape, expectedBoolean, expectedScalar, resourceJson));
        }

        return cases;
    }
}
