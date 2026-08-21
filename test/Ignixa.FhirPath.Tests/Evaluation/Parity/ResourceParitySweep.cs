using System.Diagnostics;
using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal static class ResourceParitySweep
{
    public static ResourceParityReport Run()
    {
        var stopwatch = Stopwatch.StartNew();
        var divergences = new List<ParityDivergence>();
        var tally = new ParityOutcomeTally();
        int resources = 0;

        foreach (var version in GeneratedParityCorpus.Build())
        {
            var schema = version.Version.GetSchemaProvider();
            foreach (var resource in version.Resources)
            {
                Collect(
                    schema,
                    resource.Json,
                    $"{version.Version}/{resource.ResourceType}/generated",
                    resource.Expressions.Select(expression => (Expression: expression, Source: "SearchParameter")),
                    cultureName: "de-DE",
                    divergences,
                    tally);
                resources++;
            }
        }

        foreach (var resource in TargetedParityCorpus.Build())
        {
            Collect(resource, divergences, tally);
            resources++;
        }

        stopwatch.Stop();
        return new ResourceParityReport(
            resources,
            tally.Evaluations,
            tally.BothThrew,
            tally.BothEmpty,
            stopwatch.Elapsed,
            divergences);
    }

    public static ResourceParityReport Run(IReadOnlyList<TargetedParityResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var stopwatch = Stopwatch.StartNew();
        var divergences = new List<ParityDivergence>();
        var tally = new ParityOutcomeTally();

        foreach (var resource in resources)
        {
            Collect(resource, divergences, tally);
        }

        stopwatch.Stop();
        return new ResourceParityReport(
            resources.Count,
            tally.Evaluations,
            tally.BothThrew,
            tally.BothEmpty,
            stopwatch.Elapsed,
            divergences);
    }

    private static void Collect(
        TargetedParityResource resource,
        List<ParityDivergence> divergences,
        ParityOutcomeTally tally)
    {
        var expressions = resource.SearchParameterExpressions
            .Select(expression => (Expression: expression, Source: "SearchParameter"))
            .Concat(resource.ProbeExpressions.Select(expression => (Expression: expression, Source: "LanguageConstruct")))
            .Distinct();

        Collect(
            resource.Version.GetSchemaProvider(),
            resource.Json,
            $"{resource.Version}/{resource.Name}",
            expressions,
            resource.CultureName,
            divergences,
            tally);
    }

    private static void Collect(
        ISchema schema,
        string json,
        string resourceName,
        IEnumerable<(string Expression, string Source)> expressions,
        string? cultureName,
        List<ParityDivergence> divergences,
        ParityOutcomeTally tally)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            if (cultureName is not null)
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }

            var subject = ResourceJsonNode.Parse(json).ToElement(schema);
            foreach (var (expression, source) in expressions)
            {
                var firely = FirelyEngine.Evaluate(subject, schema, expression);
                var ignixa = IgnixaEngine.Evaluate(subject, schema, expression);
                tally.Observe(firely, ignixa);

                if (!firely.Matches(ignixa))
                {
                    divergences.Add(new ParityDivergence(expression, resourceName, source, firely, ignixa));
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
