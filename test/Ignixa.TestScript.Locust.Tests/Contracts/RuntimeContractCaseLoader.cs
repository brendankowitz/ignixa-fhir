using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Locust.Tests.Contracts;

/// <summary>
/// Loads the shared, reviewed runtime contract from <c>Contracts/runtime-cases.json</c>. The file is copied
/// next to the test assembly by the project's <c>Contracts\**\*</c> <c>CopyToOutputDirectory</c> item, and is
/// the identical source-tree file the Python <c>test_runtime_contract.py</c> loads. The contract is treated as
/// immutable: this loader only reads it and never writes or regenerates baselines.
/// </summary>
public static class RuntimeContractCases
{
    public static IReadOnlyList<RuntimeContractCase> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Contracts", "runtime-cases.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Shared runtime contract not found at '{path}'. Ensure the Contracts folder is copied to output.",
                path);
        }

        JsonNode root = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException("runtime-cases.json parsed to a null document.");

        JsonArray cases = root["cases"]!.AsArray();
        List<RuntimeContractCase> result = new(cases.Count);
        foreach (JsonNode? caseNode in cases)
        {
            result.Add(new RuntimeContractCase(caseNode!.AsObject()));
        }

        return result;
    }

    public static RuntimeContractCase ByName(string name)
    {
        foreach (RuntimeContractCase contractCase in Load())
        {
            if (contractCase.Name == name)
            {
                return contractCase;
            }
        }

        throw new InvalidOperationException($"Runtime contract has no case named '{name}'.");
    }
}
