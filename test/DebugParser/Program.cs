using System.Text;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Bundle.Serialization;

// Set up console logging with Info level to reduce noise
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Trace);
    builder.AddConsole();
});

var logger = loggerFactory.CreateLogger<StreamingBundleParser>();
var parser = new StreamingBundleParser(logger);

// Generate bundle with 50 entries to test large bundle handling
var sb = new StringBuilder();
sb.AppendLine("{");
sb.AppendLine("  \"resourceType\": \"Bundle\",");
sb.AppendLine("  \"type\": \"transaction\",");
sb.AppendLine("  \"entry\": [");

for (int i = 0; i < 50; i++)
{
    if (i > 0) sb.AppendLine(",");
    sb.AppendLine("    {");
    sb.AppendLine("      \"resource\": {");
    sb.AppendLine("        \"resourceType\": \"Patient\",");
    sb.AppendLine($"        \"id\": \"patient-{i}\",");
    sb.AppendLine("        \"name\": [");
    sb.AppendLine("          {");
    sb.AppendLine("            \"use\": \"official\",");
    sb.AppendLine("            \"family\": \"Smith\",");
    sb.AppendLine("            \"given\": [\"John\", \"Jacob\"]");
    sb.AppendLine("          }");
    sb.AppendLine("        ]");
    sb.AppendLine("      },");
    sb.AppendLine("      \"request\": {");
    sb.AppendLine("        \"method\": \"PUT\",");
    sb.AppendLine($"        \"url\": \"Patient/patient-{i}\"");
    sb.AppendLine("      }");
    sb.Append("    }");
}

sb.AppendLine();
sb.AppendLine("  ]");
sb.AppendLine("}");

var bundleJson = sb.ToString();

Console.WriteLine($"Testing parser with 50 entries...");
Console.WriteLine($"JSON length: {bundleJson.Length} bytes\n");

var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));
var context = await parser.ParseStreamAsync(stream, CancellationToken.None);

Console.WriteLine($"=== Bundle Header ===");
Console.WriteLine($"ResourceType: {context.ResourceType}");
Console.WriteLine($"BundleType: {context.BundleType}");
Console.WriteLine($"Links: {context.Links.Count}");
Console.WriteLine($"ParsingIssues: {context.ParsingIssues.Count}");

Console.WriteLine($"\n=== Parsing Entries ===");
int count = 0;
try
{
    await foreach (var entry in context.Entries)
    {
        count++;
        if (count <= 5 || count > 45)
        {
            // Only print first 5 and last 5 to reduce output
            Console.WriteLine($"Entry {count}: ResourceType={entry.ResourceType}, Id={entry.ResourceId}, Verb={entry.HttpVerb}");
        }
        else if (count == 6)
        {
            Console.WriteLine("... (entries 6-45 omitted) ...");
        }
    }

    Console.WriteLine($"\n✅ SUCCESS: Parsed {count} entries");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ ERROR after {count} entries: {ex.GetType().Name}");
    Console.WriteLine($"Message: {ex.Message}");
}
