using System.Xml.Linq;

namespace Ignixa.FhirPath.Tests.TestHelpers;

public static class FhirPathTestSuiteParser
{
    public static IReadOnlyList<FhirPathTestCase> ParseTestSuite(string xmlFilePath)
    {
        ArgumentNullException.ThrowIfNull(xmlFilePath);

        if (!File.Exists(xmlFilePath))
        {
            throw new FileNotFoundException($"Test suite file not found: {xmlFilePath}", xmlFilePath);
        }

        var doc = XDocument.Load(xmlFilePath);
        var root = doc.Root ?? throw new InvalidOperationException("XML document has no root element.");

        var testCases = new List<FhirPathTestCase>();

        foreach (var group in root.Elements("group"))
        {
            var groupName = group.Attribute("name")?.Value ?? "unnamed";

            foreach (var test in group.Elements("test"))
            {
                var testCase = ParseTestElement(test, groupName);
                testCases.Add(testCase);
            }
        }

        return testCases;
    }

    private static FhirPathTestCase ParseTestElement(XElement test, string groupName)
    {
        var name = test.Attribute("name")?.Value ?? "unnamed";
        var inputFile = test.Attribute("inputfile")?.Value;
        var description = test.Attribute("description")?.Value;
        var mode = test.Attribute("mode")?.Value;

        var orderedAttr = test.Attribute("ordered")?.Value;
        var ordered = orderedAttr is null || bool.Parse(orderedAttr);

        var predicateAttr = test.Attribute("predicate")?.Value;
        var predicate = predicateAttr is not null && bool.Parse(predicateAttr);

        var expressionElement = test.Element("expression");
        if (expressionElement is null)
        {
            throw new InvalidOperationException($"Test '{name}' is missing <expression> element.");
        }

        var expression = expressionElement.Value;
        var invalidAttr = expressionElement.Attribute("invalid")?.Value;
        var isInvalidTest = invalidAttr is not null;

        var expectedOutputs = test.Elements("output")
            .Select(output =>
            {
                var type = output.Attribute("type")?.Value ?? "unknown";
                var value = output.Value;

                // Strip FHIRPath literal syntax from expected values
                // The test XML uses @T03:00:00 syntax, but FHIRPath evaluation should return 03:00:00
                if (type is "date" or "dateTime" or "time" or "instant")
                {
                    value = value.TrimStart('@');
                    if (type == "time" && value.StartsWith('T'))
                        value = value.Substring(1);
                }

                return new ExpectedOutput(type, value);
            })
            .ToList();

        return new FhirPathTestCase(
            Name: name,
            GroupName: groupName,
            Expression: expression,
            InputFile: inputFile,
            ExpectedOutputs: expectedOutputs,
            IsInvalidTest: isInvalidTest,
            InvalidType: invalidAttr,
            Ordered: ordered,
            Predicate: predicate,
            Description: description,
            Mode: mode
        );
    }
}
