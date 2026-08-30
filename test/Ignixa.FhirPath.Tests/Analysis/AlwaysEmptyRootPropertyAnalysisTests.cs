using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.Evaluation.Parity;
using Ignixa.FhirPath.Visitors;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// Covers root-relative names that some other resource type declares. These are decidable, not
/// unanalysable: the analyzer knows the root type concretely and knows it has no such element.
/// </summary>
/// <remarks>
/// <para>
/// Every always-empty claim here is paired with an evaluator result, because an always-empty verdict is
/// only dangerous in the direction where it is wrong. Asserting <c>HasAlwaysEmptySubexpression</c>
/// against the analyzer alone cannot distinguish a correct verdict from a false one, and a false one
/// suppresses a search parameter's index silently. The pairing is the assertion with teeth.
/// </para>
/// <para>
/// <see cref="GivenEveryTopLevelElementPresentInTheCorpus_WhenAnalyzedAsABareName_ThenNeverReportsAlwaysEmpty"/>
/// generalises that into the soundness rule itself. It fails at <c>c14f5b76</c>, the commit before the
/// analyzer fix, and is the only test here that would have caught either always-empty defect unaided.
/// </para>
/// </remarks>
public class AlwaysEmptyRootPropertyAnalysisTests
{
    private const string PatientJson = """
        {
          "resourceType": "Patient",
          "id": "pat1",
          "active": true,
          "gender": "female",
          "birthDate": "1980-04-01"
        }
        """;

    private readonly FhirPathAnalyzer _analyzer = new(FhirVersion.R5.GetSchemaProvider());
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly FhirPathParser _parser = new();

    /// <summary>
    /// Each name is a real element on the paired resource type, so the analyzer is deciding a genuine
    /// element name rather than a typo.
    /// </summary>
    public static TheoryData<string, string> RootPropertiesOfOtherResources =>
        new()
        {
            { "status", "Appointment" },
            { "vaccineCode", "Immunization" },
            { "requestedPeriod", "Appointment" },
        };

    [Theory]
    [MemberData(nameof(RootPropertiesOfOtherResources))]
    public void GivenPropertyOfAnotherResource_WhenAnalyzedOnConcreteRoot_ThenReportsAlwaysEmptyAndTheEvaluatorAgrees(
        string propertyName,
        string declaringResourceType)
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();
        var patient = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var result = _analyzer.Analyze(propertyName, "Patient");
        var onPatient = Evaluate(patient, propertyName, schema);
        var declaresProperty = schema.GetTypeDefinition(declaringResourceType)!
            .Children.Any(child => child.Info.Name == propertyName);

        // Assert
        onPatient.ShouldBeEmpty(
            $"'{propertyName}' must yield nothing on a populated Patient, or the always-empty verdict below is false.");
        declaresProperty.ShouldBeTrue(
            $"'{propertyName}' must be a real element on {declaringResourceType}, or this is a typo case rather than a decidable miss.");
        result.Errors.ShouldBeEmpty();
        result.IsIndeterminate.ShouldBeFalse();
        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
        result.Warnings.ShouldContain(warning =>
            warning.Contains("will always be empty on root type 'Patient'", StringComparison.Ordinal));
    }

    /// <summary>
    /// The soundness rule the analyzer declares, stated as something that can fail. For every generated
    /// resource in the parity corpus and every key its JSON actually carries, the bare name names present
    /// data, so no always-empty verdict on it can be correct.
    /// </summary>
    /// <remarks>
    /// Driven from the corpus rather than a hand-written list precisely so it reaches element shapes nobody
    /// thought to enumerate; both always-empty defects this PR fixed were in that category. Failures are
    /// collected and reported together so a regression shows its extent rather than its first instance.
    /// </remarks>
    [Fact]
    public void GivenEveryTopLevelElementPresentInTheCorpus_WhenAnalyzedAsABareName_ThenNeverReportsAlwaysEmpty()
    {
        // Arrange
        var falseVerdicts = new List<string>();
        var checkedNames = 0;

        // Act
        foreach (var version in GeneratedParityCorpus.Build())
        {
            var analyzer = new FhirPathAnalyzer(version.Version.GetSchemaProvider());

            foreach (var resource in version.Resources)
            {
                using var document = JsonDocument.Parse(resource.Json);

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name is "resourceType" || property.Name.StartsWith('_'))
                    {
                        continue;
                    }

                    checkedNames++;

                    if (analyzer.Analyze(property.Name, resource.ResourceType).HasAlwaysEmptySubexpression)
                    {
                        falseVerdicts.Add($"{version.Version} {resource.ResourceType}.{property.Name}");
                    }
                }
            }
        }

        // Assert
        checkedNames.ShouldBeGreaterThan(0, "The corpus must supply elements, or this invariant is vacuous.");
        falseVerdicts.ShouldBeEmpty(
            $"The analyzer called {falseVerdicts.Count} present elements provably empty: "
            + string.Join(", ", falseVerdicts.Take(20)));
    }

    [Fact]
    public void GivenPropertyOfAnotherResource_WhenAnalyzedOnAbstractRoot_ThenReportsIndeterminate()
    {
        var result = _analyzer.Analyze("status", "Resource");

        result.Errors.ShouldBeEmpty();
        result.IsIndeterminate.ShouldBeTrue();
        result.InferredTypes.Types.ShouldHaveSingleItem().IsUnknown.ShouldBeTrue();
    }

    /// <summary>
    /// <c>IsAbstract</c> is not sufficient to make a root's runtime type unknowable: the abstract set also
    /// contains <c>Element</c>, <c>BackboneElement</c>, <c>DataType</c>, <c>BackboneType</c> and
    /// <c>PrimitiveType</c>, none of which a resource's top-level elements can appear on. A runtime
    /// <c>Element</c> is never an <c>Appointment</c>, so the miss is as decidable as it is on
    /// <c>Patient</c>. Not reachable through the corpus, whose roots are always resources.
    /// </summary>
    [Theory]
    [InlineData("Element")]
    [InlineData("BackboneElement")]
    [InlineData("DataType")]
    [InlineData("BackboneType")]
    [InlineData("PrimitiveType")]
    public void GivenPropertyOfAnotherResource_WhenAnalyzedOnAbstractNonResourceRoot_ThenReportsAlwaysEmpty(
        string rootType)
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();

        // Act
        var result = _analyzer.Analyze("status", rootType);
        var declaresStatus = schema.GetTypeDefinition(rootType)!
            .Children.Any(child => child.Info.Name == "status");

        // Assert
        declaresStatus.ShouldBeFalse(
            $"'status' must be absent from {rootType}, or the always-empty verdict below is false.");
        result.Errors.ShouldBeEmpty();
        result.IsIndeterminate.ShouldBeFalse();
        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
    }

    /// <summary>
    /// The always-empty branch contributes no type, which is the precondition for the cascade the corpus
    /// observes: the next navigation step then runs against an emptied focus.
    /// </summary>
    [Fact]
    public void GivenAlwaysEmptyRootProperty_WhenAnalyzed_ThenInfersNoTypes()
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();
        var patient = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var result = _analyzer.Analyze("requestedPeriod", "Patient");
        var evaluated = Evaluate(patient, "requestedPeriod", schema);

        // Assert
        evaluated.ShouldBeEmpty(
            "'requestedPeriod' must yield nothing on a populated Patient, or the verdict below is false.");
        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
        result.InferredTypes.Types.ShouldBeEmpty();
    }

    /// <summary>
    /// Pins the cascade itself. The error is raised by the empty-context guard in <c>VisitChild</c>, not by
    /// the reclassification, and that guard is over-strict against strict FHIRPath, where navigation off an
    /// empty collection yields empty. Pinned so a deliberate change to it is visible here rather than as an
    /// unexplained corpus movement.
    /// </summary>
    [Fact]
    public void GivenAlwaysEmptyRootProperty_WhenNavigatedFurther_ThenCascadesToEmptyContextError()
    {
        var result = _analyzer.Analyze("requestedPeriod.start", "Patient");

        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
        result.IsIndeterminate.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Contains("Cannot access child 'start' on empty context", StringComparison.Ordinal));
    }

    /// <summary>
    /// The decision recorded for a probable typo: a decidable miss is not an error, so <c>IsValid</c> stays
    /// true and the always-empty predicate is the only signal that distinguishes it from a correct
    /// expression. A caller wanting typo rejection must consult that predicate deliberately.
    /// </summary>
    [Fact]
    public void GivenAlwaysEmptyRootProperty_WhenAnalyzed_ThenOnlyTheAlwaysEmptyPredicateSignalsIt()
    {
        var result = _analyzer.Analyze("status", "Patient");

        result.IsValid.ShouldBeTrue();
        result.IsValidOrIndeterminate.ShouldBeTrue();
        result.IsIndeterminate.ShouldBeFalse();
        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
    }

    /// <summary>
    /// Records the pre-existing asymmetry: the classifier keys on root-relativity, not on decidability, so
    /// only a bare name reaches the always-empty branch. Every qualified form of the same fact — including
    /// the resource-qualified <c>Patient.status</c>, which most authors would call the same expression —
    /// stays an error. Reconciling this would reclassify every "property not found" error the analyzer
    /// raises, so it is pinned rather than changed.
    /// </summary>
    [Theory]
    [InlineData("Patient.status")]
    [InlineData("$this.status")]
    [InlineData("%resource.status")]
    [InlineData("Patient.where(status='active')")]
    public void GivenSameMissQualifiedByFocus_WhenAnalyzed_ThenStillReportsError(string expression)
    {
        var result = _analyzer.Analyze(expression, "Patient");

        result.HasAlwaysEmptySubexpression.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Contains("Property 'status' not found", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenPropertyNoResourceDeclares_WhenAnalyzed_ThenStillReportsError()
    {
        var result = _analyzer.Analyze("nosuchpropanywhere", "Patient");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Contains("'nosuchpropanywhere' not found", StringComparison.Ordinal));
    }

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema schema) =>
        _evaluator
            .Evaluate(
                element,
                _parser.Parse(expression),
                new EvaluationContext { Resource = element, RootResource = element, Schema = schema })
            .ToList();
}
