using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Ignixa.FhirPath.Tests.TestHelpers;

public static class FhirXmlToJsonConverter
{
    private static readonly XNamespace FhirNamespace = "http://hl7.org/fhir";
    private static readonly XNamespace XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    public static string ConvertXmlToJson(string xmlContent)
    {
        ArgumentNullException.ThrowIfNull(xmlContent);

        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            throw new ArgumentException("XML content cannot be empty or whitespace.", nameof(xmlContent));
        }

        try
        {
            var doc = XDocument.Parse(xmlContent);
            var root = doc.Root ?? throw new InvalidOperationException("XML document has no root element.");

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            writer.WriteString("resourceType", root.Name.LocalName);
            ConvertElement(root, writer);
            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new InvalidOperationException("Failed to convert FHIR XML to JSON. Ensure the XML is valid FHIR content.", ex);
        }
    }

    public static string LoadResourceAsJson(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be empty or whitespace.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"FHIR resource file not found: {filePath}", filePath);
        }

        var extension = Path.GetExtension(filePath).ToUpperInvariant();
        var content = File.ReadAllText(filePath);

        return extension switch
        {
            ".XML" => ConvertXmlToJson(content),
            ".JSON" => content,
            _ => throw new InvalidOperationException($"Unsupported file format: {extension}. Expected .xml or .json.")
        };
    }

    private static void ConvertElement(XElement element, Utf8JsonWriter writer)
    {
        // Handle extension/modifierExtension url attribute (FHIR XML uses attribute, JSON uses property)
        var urlAttr = element.Attribute("url");
        if (urlAttr != null && (element.Name.LocalName == "extension" || element.Name.LocalName == "modifierExtension"))
        {
            writer.WriteString("url", urlAttr.Value);
        }

        var children = element.Elements().Where(e => e.Name.Namespace == FhirNamespace).ToList();
        var groupedChildren = children.GroupBy(e => e.Name.LocalName).ToList();

        foreach (var group in groupedChildren)
        {
            var propertyName = group.Key;
            var elements = group.ToList();

            if (elements.Count == 1 && !ShouldBeArray(propertyName))
            {
                var child = elements[0];
                WriteProperty(propertyName, child, writer);
            }
            else
            {
                writer.WritePropertyName(propertyName);
                writer.WriteStartArray();
                foreach (var child in elements)
                {
                    WriteValue(child, writer);
                }
                writer.WriteEndArray();

                WritePrimitiveExtensionArray(propertyName, elements, writer);
            }
        }
    }

    private static void WriteProperty(string propertyName, XElement element, Utf8JsonWriter writer)
    {
        writer.WritePropertyName(propertyName);
        WriteValue(element, writer);

        WritePrimitiveExtension(propertyName, element, writer);
    }

    private static void WriteValue(XElement element, Utf8JsonWriter writer)
    {
        var valueAttr = element.Attribute("value");
        var hasChildren = element.Elements().Any(e => e.Name.Namespace == FhirNamespace);

        if (valueAttr is not null)
        {
            WritePrimitiveValue(valueAttr.Value, writer);
        }
        else if (element.Name.LocalName == "div" && element.Name.Namespace == FhirNamespace)
        {
            var xhtmlDiv = element.Elements().FirstOrDefault(e => e.Name.Namespace == XhtmlNamespace);
            if (xhtmlDiv is not null)
            {
                writer.WriteStringValue(xhtmlDiv.ToString(SaveOptions.DisableFormatting));
            }
            else
            {
                writer.WriteStringValue(string.Empty);
            }
        }
        else
        {
            writer.WriteStartObject();
            ConvertElement(element, writer);
            writer.WriteEndObject();
        }
    }

    private static void WritePrimitiveValue(string value, Utf8JsonWriter writer)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            writer.WriteBooleanValue(boolValue);
        }
        else if (int.TryParse(value, out var intValue))
        {
            writer.WriteNumberValue(intValue);
        }
        else if (decimal.TryParse(value, out var decimalValue))
        {
            writer.WriteNumberValue(decimalValue);
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }

    private static void WritePrimitiveExtension(string propertyName, XElement element, Utf8JsonWriter writer)
    {
        var hasId = element.Attribute("id") is not null;
        var hasExtension = element.Elements().Any(e => e.Name.LocalName == "extension" && e.Name.Namespace == FhirNamespace);

        if (!hasId && !hasExtension)
        {
            return;
        }

        writer.WritePropertyName($"_{propertyName}");
        writer.WriteStartObject();

        if (hasId)
        {
            writer.WriteString("id", element.Attribute("id")!.Value);
        }

        if (hasExtension)
        {
            var extensions = element.Elements().Where(e => e.Name.LocalName == "extension" && e.Name.Namespace == FhirNamespace).ToList();
            writer.WritePropertyName("extension");
            writer.WriteStartArray();
            foreach (var ext in extensions)
            {
                WriteValue(ext, writer);
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WritePrimitiveExtensionArray(string propertyName, List<XElement> elements, Utf8JsonWriter writer)
    {
        var hasAnyExtensions = elements.Any(e =>
            e.Attribute("id") is not null ||
            e.Elements().Any(child => child.Name.LocalName == "extension" && child.Name.Namespace == FhirNamespace));

        if (!hasAnyExtensions)
        {
            return;
        }

        writer.WritePropertyName($"_{propertyName}");
        writer.WriteStartArray();
        foreach (var element in elements)
        {
            var hasId = element.Attribute("id") is not null;
            var hasExtension = element.Elements().Any(e => e.Name.LocalName == "extension" && e.Name.Namespace == FhirNamespace);

            if (hasId || hasExtension)
            {
                writer.WriteStartObject();

                if (hasId)
                {
                    writer.WriteString("id", element.Attribute("id")!.Value);
                }

                if (hasExtension)
                {
                    var extensions = element.Elements().Where(e => e.Name.LocalName == "extension" && e.Name.Namespace == FhirNamespace).ToList();
                    writer.WritePropertyName("extension");
                    writer.WriteStartArray();
                    foreach (var ext in extensions)
                    {
                        WriteValue(ext, writer);
                    }
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }
        }
        writer.WriteEndArray();
    }

    private static bool ShouldBeArray(string propertyName)
    {
        return propertyName is "identifier" or "name" or "telecom" or "address" or "contact" or "communication" or "link" or
            "given" or "prefix" or "suffix" or "line" or "coding" or "contained" or "extension" or "modifierExtension";
    }
}
