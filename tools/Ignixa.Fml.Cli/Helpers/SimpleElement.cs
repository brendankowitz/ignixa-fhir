using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using System.Text.Json.Nodes;

namespace Ignixa.Fml.Cli.Helpers;

/// <summary>
/// Simple IElement wrapper for ResourceJsonNode for CLI purposes.
/// </summary>
internal class SimpleElement : IElement
{
    private readonly ResourceJsonNode _node;
    private readonly string _name;

    public SimpleElement(ResourceJsonNode node, string name = "root")
    {
        _node = node;
        _name = name;
    }

    public string Name => _name;

    public string InstanceType => _node.ResourceType ?? "Resource";

    public object? Value => null;

    public string Location => _name;

    public IType? Type => null;

    public IReadOnlyList<IElement> Children(string? name = null)
    {
        var result = new List<IElement>();
        
        if (name == null)
        {
            // Return all children
            foreach (var prop in _node.MutableNode)
            {
                if (prop.Value != null)
                {
                    result.Add(new JsonNodeElement(prop.Key, prop.Value));
                }
            }
        }
        else
        {
            // Return children with specific name
            if (_node.MutableNode.TryGetPropertyValue(name, out var value) && value != null)
            {
                if (value is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        if (item != null)
                        {
                            result.Add(new JsonNodeElement(name, item));
                        }
                    }
                }
                else
                {
                    result.Add(new JsonNodeElement(name, value));
                }
            }
        }

        return result;
    }

    public T? Meta<T>() where T : class => null;
}

/// <summary>
/// Simple IElement wrapper for JsonNode for CLI purposes.
/// </summary>
internal class JsonNodeElement : IElement
{
    private readonly JsonNode _node;
    private readonly string _name;

    public JsonNodeElement(string name, JsonNode node)
    {
        _name = name;
        _node = node;
    }

    public string Name => _name;

    public string InstanceType => _node switch
    {
        JsonObject obj when obj.TryGetPropertyValue("resourceType", out var rt) => rt?.ToString() ?? "object",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue => "value",
        _ => "unknown"
    };

    public object? Value
    {
        get
        {
            if (_node is JsonValue jsonValue)
            {
                return jsonValue.GetValue<object>();
            }
            return null;
        }
    }

    public string Location => _name;

    public IType? Type => null;

    public IReadOnlyList<IElement> Children(string? name = null)
    {
        var result = new List<IElement>();

        if (_node is JsonObject obj)
        {
            if (name == null)
            {
                foreach (var prop in obj)
                {
                    if (prop.Value != null)
                    {
                        result.Add(new JsonNodeElement(prop.Key, prop.Value));
                    }
                }
            }
            else
            {
                if (obj.TryGetPropertyValue(name, out var value) && value != null)
                {
                    if (value is JsonArray array)
                    {
                        foreach (var item in array)
                        {
                            if (item != null)
                            {
                                result.Add(new JsonNodeElement(name, item));
                            }
                        }
                    }
                    else
                    {
                        result.Add(new JsonNodeElement(name, value));
                    }
                }
            }
        }
        else if (_node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                var item = array[i];
                if (item != null)
                {
                    result.Add(new JsonNodeElement($"[{i}]", item));
                }
            }
        }

        return result;
    }

    public T? Meta<T>() where T : class => null;
}
