using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.Models;

namespace Ignixa.Anonymizer.Processors;

public interface IAnonymizerProcessor
{
    ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null);
}
