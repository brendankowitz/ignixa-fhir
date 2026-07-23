using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Locust.Compilation;

/// <summary>
/// Compiles a single TestScript <see cref="FixtureDefinition"/> into a compile-time pool of resource
/// variants, preferring an <c>fhirfakes</c>-generated pool over a single literal fixture resource.
/// </summary>
public sealed class LocustFixtureCompiler
{
    private readonly IFhirSchemaProvider _schema;
    private readonly IFixtureProvider _generated;
    private readonly IFixtureProvider _inline;

    /// <summary>
    /// Creates a compiler that prefers <see cref="FhirFakesFixtureProvider"/>-generated resources over
    /// the fixture's literal, inline resource.
    /// </summary>
    /// <param name="schema">The FHIR schema provider used to resolve resource shapes.</param>
    public LocustFixtureCompiler(IFhirSchemaProvider schema)
        : this(schema, new FhirFakesFixtureProvider(), new InlineFixtureProvider())
    {
    }

    /// <summary>
    /// Creates a compiler with explicit provider instances, for deterministic testing.
    /// </summary>
    internal LocustFixtureCompiler(
        IFhirSchemaProvider schema,
        IFixtureProvider generated,
        IFixtureProvider inline)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(inline);

        _schema = schema;
        _generated = generated;
        _inline = inline;
    }

    /// <summary>
    /// Compiles <paramref name="fixture"/> into a <see cref="LocustIrFixture"/> resource-variant pool.
    /// </summary>
    /// <param name="fixture">The fixture definition to compile.</param>
    /// <param name="variantCount">
    /// The number of resource variants to generate when <paramref name="fixture"/> resolves through the
    /// generated (<c>fhirfakes</c>) provider. Ignored for literal fixtures, which always produce exactly
    /// one variant.
    /// </param>
    /// <param name="source">The canonical diagnostic source to attribute any failure to.</param>
    /// <param name="cancellationToken">A token used to observe cancellation requests.</param>
    /// <returns>
    /// The compiled fixture, or a <c>LOCUST007</c>/<c>LOCUST008</c> error diagnostic describing why the
    /// fixture could not be compiled.
    /// </returns>
    public async Task<(LocustIrFixture? Fixture, LocustDiagnostic? Diagnostic)> CompileAsync(
        FixtureDefinition fixture,
        int variantCount,
        string source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        cancellationToken.ThrowIfCancellationRequested();

        FixtureResolutionContext context = new()
        {
            Schema = _schema,
            ResourceType = fixture.Resource?.ResourceType
        };

        var firstGenerated = await _generated.ResolveFixtureAsync(fixture, context, cancellationToken);
        var generated = firstGenerated is not null;
        if (generated && variantCount < 1)
        {
            return (null, new LocustDiagnostic(
                "LOCUST007",
                LocustDiagnosticSeverity.Error,
                source,
                "fhirfakes fixtures require --fixture-variants greater than zero."));
        }

        var count = generated ? variantCount : 1;
        List<JsonObject> variants = new(count);
        for (var index = 0; index < count; index++)
        {
            var resource = index switch
            {
                0 when firstGenerated is not null => firstGenerated,
                0 => await _inline.ResolveFixtureAsync(fixture, context, cancellationToken),
                _ => await _generated.ResolveFixtureAsync(fixture, context, cancellationToken)
            };

            if (resource is null)
            {
                return (null, new LocustDiagnostic(
                    "LOCUST008",
                    LocustDiagnosticSeverity.Error,
                    source,
                    $"Fixture '{fixture.Id}' could not be materialized."));
            }

            variants.Add(JsonNode.Parse(resource.SerializeToString())!.AsObject());
        }

        return (new LocustIrFixture(fixture.Id, fixture.Autocreate, fixture.Autodelete, variants), null);
    }
}
