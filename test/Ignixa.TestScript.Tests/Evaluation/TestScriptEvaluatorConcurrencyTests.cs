using System.Collections.Concurrent;
using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Evaluation;

// The `ignixa-matrix serve` host builds one TestScriptEvaluator per HTTP request, all sharing the same
// ITestRequestProvider/IFixtureProvider/IFhirSchemaProvider instances, and Kestrel dispatches
// those requests concurrently. These tests reproduce that shape and look for cross-run
// contamination through the shared seam rather than merely asserting "no exception".
public class TestScriptEvaluatorConcurrencyTests
{
    private const int ConcurrentRunCount = 32;
    private const int SharedEvaluatorRunCount = 16;
    private const int DistinctDefinitionRunCount = 16;

    private readonly IFixtureProvider _fixtureProvider = new InlineFixtureProvider();
    private readonly IFhirSchemaProvider _schema = Substitute.For<IFhirSchemaProvider>();

    [Fact]
    public async Task GivenManyConcurrentExecutions_WhenRunningSameDefinition_ThenEveryRunPassesWithIsolatedState()
    {
        // Arrange
        var definition = BuildCreateThenReadDefinition("ConcurrencySpike-SharedDefinition");
        var provider = new CorrelatingStubRequestProvider();

        // Act
        var tasks = new Task<TestScriptReport>[ConcurrentRunCount];
        for (var i = 0; i < ConcurrentRunCount; i++)
        {
            var evaluator = new TestScriptEvaluator(provider, _fixtureProvider, _schema);
            tasks[i] = Task.Run(() => evaluator.ExecuteAsync(definition, CancellationToken.None));
        }

        var reports = await Task.WhenAll(tasks);

        // Assert
        reports.ShouldAllBe(r => r.OverallOutcome == TestScriptOutcome.Pass);
        provider.IssuedIdCount.ShouldBe(ConcurrentRunCount);
        provider.ConsumedCount.ShouldBe(ConcurrentRunCount);
        provider.ConsumptionFailures.ShouldBeEmpty();
        provider.TotalRequestCount.ShouldBe(ConcurrentRunCount * 2);
    }

    [Fact]
    public async Task GivenConcurrentExecutionsOfDistinctDefinitions_WhenRunning_ThenReportsMatchTheirOwnDefinition()
    {
        // Arrange
        var provider = new UrlRecordingStubRequestProvider();
        var definitions = Enumerable.Range(0, DistinctDefinitionRunCount)
            .Select(BuildMarkerSearchDefinition)
            .ToArray();

        // Act
        var tasks = new Task<TestScriptReport>[DistinctDefinitionRunCount];
        for (var i = 0; i < DistinctDefinitionRunCount; i++)
        {
            var evaluator = new TestScriptEvaluator(provider, _fixtureProvider, _schema);
            var definition = definitions[i];
            tasks[i] = Task.Run(() => evaluator.ExecuteAsync(definition, CancellationToken.None));
        }

        var reports = await Task.WhenAll(tasks);

        // Assert
        for (var i = 0; i < DistinctDefinitionRunCount; i++)
        {
            reports[i].TestScriptName.ShouldBe($"DistinctDefinition-{i}");
            reports[i].OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        }

        provider.RecordedUrls.Count.ShouldBe(DistinctDefinitionRunCount);
        for (var i = 0; i < DistinctDefinitionRunCount; i++)
        {
            provider.RecordedUrls.Count(u => u == $"Patient?identifier=marker-{i}").ShouldBe(1);
        }
    }

    [Fact]
    public async Task GivenSharedEvaluatorInstance_WhenExecutingConcurrently_ThenRunsRemainIsolated()
    {
        // Arrange
        var definition = BuildCreateThenReadDefinition("ConcurrencySpike-SharedEvaluator");
        var provider = new CorrelatingStubRequestProvider();
        var evaluator = new TestScriptEvaluator(provider, _fixtureProvider, _schema);

        // Act
        var tasks = new Task<TestScriptReport>[SharedEvaluatorRunCount];
        for (var i = 0; i < SharedEvaluatorRunCount; i++)
            tasks[i] = Task.Run(() => evaluator.ExecuteAsync(definition, CancellationToken.None));

        var reports = await Task.WhenAll(tasks);

        // Assert
        reports.ShouldAllBe(r => r.OverallOutcome == TestScriptOutcome.Pass);
        provider.IssuedIdCount.ShouldBe(SharedEvaluatorRunCount);
        provider.ConsumedCount.ShouldBe(SharedEvaluatorRunCount);
        provider.ConsumptionFailures.ShouldBeEmpty();
        provider.TotalRequestCount.ShouldBe(SharedEvaluatorRunCount * 2);
    }

    private static TestScriptDefinition BuildCreateThenReadDefinition(string name) => new()
    {
        Metadata = new TestScriptMetadata { Name = name },
        Variables =
        [
            new VariableDefinition { Name = "id", SourceId = "create-response", Extraction = new PathExtraction("id") }
        ],
        Setup =
        [
            new OperationExpression { Type = "create", Resource = "Patient", ResponseId = "create-response" }
        ],
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "ReadCreatedPatient",
                Actions =
                [
                    new OperationExpression { Type = "read", Resource = "Patient", Params = "/${id}" },
                    new AssertExpression { Criteria = new ResponseStatusCriteria("okay") },
                    new AssertExpression { Criteria = new ResourceTypeCriteria("Patient") }
                ]
            }
        ]
    };

    private static TestScriptDefinition BuildMarkerSearchDefinition(int index) => new()
    {
        Metadata = new TestScriptMetadata { Name = $"DistinctDefinition-{index}" },
        Variables =
        [
            new VariableDefinition { Name = "marker", DefaultValue = $"marker-{index}" }
        ],
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "SearchByMarker",
                Actions =
                [
                    new OperationExpression { Type = "search", Resource = "Patient", Params = "?identifier=${marker}" },
                    new AssertExpression { Criteria = new ResponseStatusCriteria("okay") }
                ]
            }
        ]
    };

    // Hand-written stub rather than NSubstitute: correlation bookkeeping (issuing an id on create,
    // then atomically consuming it on the matching read) needs the same atomicity guarantees the
    // production ITestRequestProvider seam would need under real concurrent load, which a
    // configured NSubstitute return value cannot express.
    private sealed class CorrelatingStubRequestProvider : ITestRequestProvider
    {
        private readonly ConcurrentDictionary<string, byte> _pendingIds = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _consumptionFailures = new();
        private long _idCounter;
        private int _totalRequestCount;
        private int _consumedCount;

        public int IssuedIdCount => (int)Interlocked.Read(ref _idCounter);
        public int TotalRequestCount => Volatile.Read(ref _totalRequestCount);
        public int ConsumedCount => Volatile.Read(ref _consumedCount);
        public IReadOnlyCollection<string> ConsumptionFailures => _consumptionFailures.ToArray();

        public Task<TestResponse> ExecuteAsync(TestRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _totalRequestCount);

            if (request.Method == HttpMethod.Post && request.Url == "Patient")
                return Task.FromResult(HandleCreate());

            if (request.Method == HttpMethod.Get && request.Url.StartsWith("Patient/", StringComparison.Ordinal))
                return Task.FromResult(HandleRead(request.Url["Patient/".Length..]));

            throw new InvalidOperationException($"Unexpected request in concurrency stub: {request.Method} {request.Url}");
        }

        private TestResponse HandleCreate()
        {
            var id = Interlocked.Increment(ref _idCounter).ToString(CultureInfo.InvariantCulture);
            _pendingIds[id] = 0;
            return new TestResponse
            {
                StatusCode = 201,
                Body = JsonSourceNodeFactory.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}""")
            };
        }

        private TestResponse HandleRead(string id)
        {
            if (_pendingIds.TryRemove(id, out _))
                Interlocked.Increment(ref _consumedCount);
            else
                _consumptionFailures.Enqueue(id);

            return new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}""")
            };
        }
    }

    private sealed class UrlRecordingStubRequestProvider : ITestRequestProvider
    {
        private readonly ConcurrentQueue<string> _recordedUrls = new();

        public IReadOnlyCollection<string> RecordedUrls => _recordedUrls.ToArray();

        public Task<TestResponse> ExecuteAsync(TestRequest request, CancellationToken cancellationToken)
        {
            _recordedUrls.Enqueue(request.Url);
            return Task.FromResult(new TestResponse { StatusCode = 200 });
        }
    }
}
