using Ignixa.Abstractions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;

namespace Ignixa.Search.Sql.Tests.TestSupport;

internal sealed class FakeSearchOptionsBuilder(SearchOptions options, IReadOnlyList<ParameterTrace> outcomes) : ISearchOptionsBuilder
{
    /// <summary>Whether the last <see cref="Build"/> call was given a trace collector.</summary>
    public bool LastCallCollectedTraces { get; private set; }

    public SearchOptions Build(string? resourceType, IReadOnlyList<QueryParameter> parameters, ISchema? schemaProvider = null, IList<ParameterTrace>? outcomeCollector = null)
    {
        LastCallCollectedTraces = outcomeCollector is not null;

        if (outcomeCollector is not null)
        {
            foreach (var outcome in outcomes)
            {
                outcomeCollector.Add(outcome);
            }
        }

        return options;
    }
}
