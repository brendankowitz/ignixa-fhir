using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Locust.Compilation;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Locust.Tests.Compilation;

public class LocustFixtureCompilerTests
{
    private static readonly IFhirSchemaProvider s_schema = FhirVersion.R4.GetSchemaProvider();

    private static FixtureDefinition LiteralFixture(
        string id = "literal-fixture", bool autocreate = false, bool autodelete = false) => new()
    {
        Id = id,
        Resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"literal-1"}"""),
        Autocreate = autocreate,
        Autodelete = autodelete
    };

    private static FixtureDefinition FhirFakesFixture(
        string id = "fakes-fixture", bool autocreate = false, bool autodelete = false) => new()
    {
        Id = id,
        Resource = ResourceJsonNode.Parse("""
            {
                "resourceType": "Basic",
                "extension": [
                    { "url": "http://ignixa.io/testscript/fhirfakes", "valueCode": "Patient" }
                ]
            }
            """),
        Autocreate = autocreate,
        Autodelete = autodelete
    };

    private static ResourceJsonNode PatientWithId(string id) =>
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}""");

    [Fact]
    public void GivenNullSchema_WhenConstructedWithPublicConstructor_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new LocustFixtureCompiler(null!));
    }

    [Fact]
    public void GivenNullGeneratedProvider_WhenConstructedWithInternalConstructor_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new LocustFixtureCompiler(s_schema, null!, new InlineFixtureProvider()));
    }

    [Fact]
    public void GivenNullInlineProvider_WhenConstructedWithInternalConstructor_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new LocustFixtureCompiler(s_schema, new NullFixtureProvider(), null!));
    }

    [Fact]
    public async Task GivenNullFixture_WhenCompiling_ThenThrowsArgumentNullException()
    {
        var compiler = new LocustFixtureCompiler(s_schema);

        await Should.ThrowAsync<ArgumentNullException>(
            () => compiler.CompileAsync(null!, 1, "fixtures.json:fixture:x", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenBlankSource_WhenCompiling_ThenThrowsArgumentException(string source)
    {
        var compiler = new LocustFixtureCompiler(s_schema);

        await Should.ThrowAsync<ArgumentException>(
            () => compiler.CompileAsync(LiteralFixture(), 1, source, CancellationToken.None));
    }

    [Fact]
    public async Task GivenPreCancelledToken_WhenCompiling_ThenThrowsOperationCanceledException()
    {
        var compiler = new LocustFixtureCompiler(s_schema);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => compiler.CompileAsync(LiteralFixture(), 1, "fixtures.json:fixture:x", cts.Token));
    }

    [Fact]
    public async Task GivenLiteralFixture_WhenVariantCountIsZero_ThenEmitsSingleClonedVariantPreservingFlags()
    {
        var fixture = LiteralFixture(id: "patient-fixture", autocreate: true, autodelete: true);
        var compiler = new LocustFixtureCompiler(s_schema, new NullFixtureProvider(), new InlineFixtureProvider());

        var (result, diagnostic) = await compiler.CompileAsync(fixture, 0, "fixtures.json:fixture:patient-fixture", CancellationToken.None);

        diagnostic.ShouldBeNull();
        result.ShouldNotBeNull();
        result.Id.ShouldBe("patient-fixture");
        result.Autocreate.ShouldBeTrue();
        result.Autodelete.ShouldBeTrue();
        var variant = result.Variants.ShouldHaveSingleItem();
        variant["resourceType"]!.GetValue<string>().ShouldBe("Patient");
        variant["id"]!.GetValue<string>().ShouldBe("literal-1");

        // Mutating the original fixture resource after compilation must not affect the compiled
        // variant, proving the IR owns an independently-parsed payload rather than aliasing it.
        fixture.Resource!.Id = "mutated-after-compile";
        variant["id"]!.GetValue<string>().ShouldBe("literal-1");
    }

    [Fact]
    public async Task GivenGeneratedFixtureDetected_WhenVariantCountIsZero_ThenReturnsLocust007Error()
    {
        var provider = new SequenceFixtureProvider([PatientWithId("v1")]);
        var compiler = new LocustFixtureCompiler(s_schema, provider, new InlineFixtureProvider());

        var (result, diagnostic) = await compiler.CompileAsync(
            FhirFakesFixture(), 0, "fixtures.json:fixture:fakes-fixture", CancellationToken.None);

        result.ShouldBeNull();
        diagnostic.ShouldNotBeNull();
        diagnostic.Code.ShouldBe("LOCUST007");
        diagnostic.Severity.ShouldBe(LocustDiagnosticSeverity.Error);
        diagnostic.Source.ShouldBe("fixtures.json:fixture:fakes-fixture");
        diagnostic.Message.ShouldBe("fhirfakes fixtures require --fixture-variants greater than zero.");
    }

    [Fact]
    public async Task GivenGeneratedFixture_WhenVariantCountIsThree_ThenInvokesGeneratedProviderThreeTimesAndYieldsDistinctVariants()
    {
        var provider = new SequenceFixtureProvider([PatientWithId("v1"), PatientWithId("v2"), PatientWithId("v3")]);
        var compiler = new LocustFixtureCompiler(s_schema, provider, new InlineFixtureProvider());

        var (result, diagnostic) = await compiler.CompileAsync(
            FhirFakesFixture(id: "fakes-fixture"), 3, "fixtures.json:fixture:fakes-fixture", CancellationToken.None);

        diagnostic.ShouldBeNull();
        result.ShouldNotBeNull();
        provider.CallCount.ShouldBe(3);
        result.Variants.Count.ShouldBe(3);
        result.Variants.Select(v => v["id"]!.GetValue<string>()).ShouldBe(["v1", "v2", "v3"]);
    }

    [Fact]
    public async Task GivenInlineProviderReturnsNull_WhenCompiling_ThenReturnsLocust008Error()
    {
        var compiler = new LocustFixtureCompiler(s_schema, new NullFixtureProvider(), new NullFixtureProvider());

        var (result, diagnostic) = await compiler.CompileAsync(
            LiteralFixture(id: "missing-fixture"), 1, "fixtures.json:fixture:missing-fixture", CancellationToken.None);

        result.ShouldBeNull();
        diagnostic.ShouldNotBeNull();
        diagnostic.Code.ShouldBe("LOCUST008");
        diagnostic.Severity.ShouldBe(LocustDiagnosticSeverity.Error);
        diagnostic.Source.ShouldBe("fixtures.json:fixture:missing-fixture");
        diagnostic.Message.ShouldBe("Fixture 'missing-fixture' could not be materialized.");
    }

    [Fact]
    public async Task GivenGeneratedProviderReturnsNullForLaterVariant_WhenCompiling_ThenReturnsLocust008Error()
    {
        var provider = new SequenceFixtureProvider([PatientWithId("v1"), null]);
        var compiler = new LocustFixtureCompiler(s_schema, provider, new InlineFixtureProvider());

        var (result, diagnostic) = await compiler.CompileAsync(
            FhirFakesFixture(id: "fakes-fixture"), 2, "fixtures.json:fixture:fakes-fixture", CancellationToken.None);

        result.ShouldBeNull();
        diagnostic.ShouldNotBeNull();
        diagnostic.Code.ShouldBe("LOCUST008");
        diagnostic.Message.ShouldBe("Fixture 'fakes-fixture' could not be materialized.");
    }

    [Fact]
    public async Task GivenGeneratedProviderThrows_WhenCompiling_ThenExceptionPropagates()
    {
        var compiler = new LocustFixtureCompiler(s_schema, new ThrowingFixtureProvider(), new InlineFixtureProvider());

        await Should.ThrowAsync<InvalidOperationException>(
            () => compiler.CompileAsync(FhirFakesFixture(), 1, "fixtures.json:fixture:fakes-fixture", CancellationToken.None));
    }

    private sealed class SequenceFixtureProvider : IFixtureProvider
    {
        private readonly Queue<ResourceJsonNode?> _queue;

        public SequenceFixtureProvider(IEnumerable<ResourceJsonNode?> results)
        {
            _queue = new Queue<ResourceJsonNode?>(results);
        }

        public int CallCount { get; private set; }

        public ValueTask<ResourceJsonNode?> ResolveFixtureAsync(
            FixtureDefinition fixture,
            FixtureResolutionContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(_queue.Count > 0 ? _queue.Dequeue() : null);
        }
    }

    private sealed class NullFixtureProvider : IFixtureProvider
    {
        public int CallCount { get; private set; }

        public ValueTask<ResourceJsonNode?> ResolveFixtureAsync(
            FixtureDefinition fixture,
            FixtureResolutionContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult<ResourceJsonNode?>(null);
        }
    }

    private sealed class ThrowingFixtureProvider : IFixtureProvider
    {
        public ValueTask<ResourceJsonNode?> ResolveFixtureAsync(
            FixtureDefinition fixture,
            FixtureResolutionContext context,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("boom");
        }
    }
}
