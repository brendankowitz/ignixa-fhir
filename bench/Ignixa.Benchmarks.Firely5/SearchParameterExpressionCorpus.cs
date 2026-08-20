using Hl7.FhirPath;
using Ignixa.FhirPath.Parser;
using Ignixa.Search.Generated;
using Ignixa.Search.Models;

namespace Ignixa.Benchmarks.Firely5;

/// <summary>
/// The real shipped R4 search parameter expression corpus, as used by the search indexer on every
/// write. Loaded from the generated definitions rather than hand-picked so the mix of expression
/// shapes - plain paths, choice types, <c>where()</c> filters, <c>as()</c> casts, <c>resolve()</c>,
/// unions across resource types - is the spec's mix and not a flattering selection.
/// </summary>
internal sealed class SearchParameterExpressionCorpus
{
    private SearchParameterExpressionCorpus(
        IReadOnlyList<string> allExpressions,
        IReadOnlyList<string> commonExpressions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> commonByResourceType,
        int ignixaCompileFailures,
        int firelyCompileFailures)
    {
        AllExpressions = allExpressions;
        CommonExpressions = commonExpressions;
        CommonByResourceType = commonByResourceType;
        IgnixaCompileFailures = ignixaCompileFailures;
        FirelyCompileFailures = firelyCompileFailures;
    }

    /// <summary>
    /// Every distinct non-empty expression in the R4 base search parameter set.
    /// </summary>
    public IReadOnlyList<string> AllExpressions { get; }

    /// <summary>
    /// The expressions both engines compile. Evaluation is only ever compared over this subset, so a
    /// timing difference is work-for-work rather than one engine quietly skipping what it cannot parse.
    /// </summary>
    public IReadOnlyList<string> CommonExpressions { get; }

    /// <summary>
    /// <see cref="CommonExpressions"/> keyed by the resource type whose index entries they produce.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CommonByResourceType { get; }

    public int IgnixaCompileFailures { get; }

    public int FirelyCompileFailures { get; }

    public static SearchParameterExpressionCorpus Load()
    {
        SearchParameterInfo[] parameters = R4SearchParameterDefinitions.GetBaseSearchParameters();

        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var byResourceType = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (SearchParameterInfo parameter in parameters)
        {
            var expression = parameter.Expression;
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            if (seen.Add(expression))
            {
                distinct.Add(expression);
            }

            foreach (var resourceType in parameter.BaseResourceTypes)
            {
                if (!byResourceType.TryGetValue(resourceType, out List<string>? expressions))
                {
                    expressions = [];
                    byResourceType.Add(resourceType, expressions);
                }

                expressions.Add(expression);
            }
        }

        var ignixaParser = new FhirPathParser();
        var firelyCompiler = new FhirPathCompiler();

        var compilesInIgnixa = new HashSet<string>(StringComparer.Ordinal);
        var compilesInFirely = new HashSet<string>(StringComparer.Ordinal);

        foreach (var expression in distinct)
        {
            if (TryCompileIgnixa(ignixaParser, expression))
            {
                compilesInIgnixa.Add(expression);
            }

            if (TryCompileFirely(firelyCompiler, expression))
            {
                compilesInFirely.Add(expression);
            }
        }

        var common = distinct
            .Where(e => compilesInIgnixa.Contains(e) && compilesInFirely.Contains(e))
            .ToArray();

        var commonSet = new HashSet<string>(common, StringComparer.Ordinal);

        var commonByResourceType = byResourceType.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Where(commonSet.Contains).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

        return new SearchParameterExpressionCorpus(
            distinct,
            common,
            commonByResourceType,
            distinct.Count - compilesInIgnixa.Count,
            distinct.Count - compilesInFirely.Count);
    }

    public static bool TryCompileIgnixa(FhirPathParser parser, string expression)
    {
        try
        {
            return parser.Parse(expression) is not null;
        }
        catch (Exception)
        {
            // A compile failure is a data point here, not an error to propagate: the corpus records
            // how many of the shipped expressions each engine rejects and excludes them from the
            // evaluation comparison.
            return false;
        }
    }

    public static bool TryCompileFirely(FhirPathCompiler compiler, string expression)
    {
        try
        {
            return compiler.Compile(expression) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
