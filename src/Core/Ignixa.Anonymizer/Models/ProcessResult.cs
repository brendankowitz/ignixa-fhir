using Ignixa.Abstractions;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Models;

public class ProcessResult
{
    public bool IsRedacted => ProcessRecords.ContainsKey(AnonymizationOperations.Redact);

    public bool IsAbstracted => ProcessRecords.ContainsKey(AnonymizationOperations.Abstract);

    public bool IsCryptoHashed => ProcessRecords.ContainsKey(AnonymizationOperations.CryptoHash);

    public bool IsEncrypted => ProcessRecords.ContainsKey(AnonymizationOperations.Encrypt);

    public bool IsPerturbed => ProcessRecords.ContainsKey(AnonymizationOperations.Perturb);

    public bool IsSubstituted => ProcessRecords.ContainsKey(AnonymizationOperations.Substitute);

    public bool IsGeneralized => ProcessRecords.ContainsKey(AnonymizationOperations.Generalize);

    public Dictionary<string, HashSet<IElement>> ProcessRecords { get; } = [];

    public void AddProcessRecord(string operationName, IElement node)
    {
        if (!ProcessRecords.TryGetValue(operationName, out var set))
        {
            set = [];
            ProcessRecords[operationName] = set;
        }
        set.Add(node);
    }

    public void Update(ProcessResult? result)
    {
        if (result is null)
        {
            return;
        }

        foreach (var (key, value) in result.ProcessRecords)
        {
            if (!ProcessRecords.TryGetValue(key, out var existing))
            {
                ProcessRecords[key] = value;
            }
            else
            {
                existing.UnionWith(value);
            }
        }
    }
}
