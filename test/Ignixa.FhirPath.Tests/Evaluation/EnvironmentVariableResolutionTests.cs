/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for the engine-managed FHIRPath environment variables (%context and friends).
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class EnvironmentVariableResolutionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenNoExplicitContextNode_WhenReferencingContext_ThenResolvesToTheEvaluatedNode()
    {
        var node = CreateElement("original");
        var expr = _parser.Parse("%context");

        var result = _evaluator.Evaluate(node, expr).ToList();

        Assert.Single(result);
        Assert.Same(node, result[0]);
    }

    [Fact]
    public void GivenAnElementInsideAResource_WhenReferencingContextAndResource_ThenTheyDiffer()
    {
        // The FHIR profile of FHIRPath defines %context as the node handed to the engine and %resource as
        // the resource containing it - a constraint declared on a child element sees two different nodes.
        var resource = CreateElement("Patient");
        var contained = CreateElement("contact");
        var context = new EvaluationContext().WithResource(resource);

        var contextResult = _evaluator.Evaluate(contained, _parser.Parse("%context"), context).Single();
        var resourceResult = _evaluator.Evaluate(contained, _parser.Parse("%resource"), context).Single();

        Assert.Same(contained, contextResult);
        Assert.Same(resource, resourceResult);
    }

    [Fact]
    public void GivenACallerSuppliedContextNode_WhenEvaluating_ThenTheCallersChoiceWins()
    {
        var declared = CreateElement("declared");
        var evaluated = CreateElement("evaluated");
        var context = new EvaluationContext().WithContextNode(declared);

        var result = _evaluator.Evaluate(evaluated, _parser.Parse("%context"), context).Single();

        Assert.Same(declared, result);
    }

    [Fact]
    public void GivenNoRootResource_WhenReferencingRootResource_ThenFallsBackToResource()
    {
        // %rootResource is the container of %resource, and is %resource itself whenever the resource is not
        // contained in another one.
        var resource = CreateElement("Patient");
        var context = new EvaluationContext().WithResource(resource);

        var result = _evaluator.Evaluate(resource, _parser.Parse("%rootResource"), context).Single();

        Assert.Same(resource, result);
    }

    [Fact]
    public void GivenAHostThatBoundNoResource_WhenReferencingResource_ThenReturnsEmptyRatherThanSignalling()
    {
        // An engine-managed name the host left unbound is still a defined name: the absence of a binding is
        // not the same as the expression naming something that does not exist, so this must not throw.
        var result = _evaluator.Evaluate(CreateElement("x"), _parser.Parse("%resource"), new EvaluationContext()).ToList();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("%ucum", "http://unitsofmeasure.org")]
    [InlineData("%sct", "http://snomed.info/sct")]
    [InlineData("%loinc", "http://loinc.org")]
    [InlineData("%`vs-administrative-gender`", "http://hl7.org/fhir/ValueSet/administrative-gender")]
    [InlineData("%`vs-observation-status`", "http://hl7.org/fhir/ValueSet/observation-status")]
    [InlineData("%`ext-patient-birthTime`", "http://hl7.org/fhir/StructureDefinition/patient-birthTime")]
    [InlineData("%`ext-questionnaire-hidden`", "http://hl7.org/fhir/StructureDefinition/questionnaire-hidden")]
    public void GivenAFixedFhirConstant_WhenReferenced_ThenResolvesToItsUri(string expression, string expected)
    {
        var result = _evaluator.Evaluate(CreateElement("x"), _parser.Parse(expression)).Single();

        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("%vs-mine", "vs-mine")]
    [InlineData("%ext-mine", "ext-mine")]
    public void GivenABareVsOrExtConstant_WhenReferenced_ThenItIsAnUndefinedVariableRatherThanAUri(
        string expression, string expectedName)
    {
        // Issue #438 conflated two questions with different answers.
        //
        // LEXING: "%vs-mine" must lex as one ExternalConstant token, matching HAPI's FHIRLexer, because
        // published cqf-expression content (%p-inactive, in fhir-test-cases) depends on '-' in the bare
        // '%' run. FhirPathTokenizerTests pins that.
        //
        // RESOLUTION: the bare spelling must NOT expand to a ValueSet or StructureDefinition URI. The FHIR
        // profile of FHIRPath writes these families as %`vs-[name]` / %`ext-[name]` ("quoted, just like
        // paths, to allow '-' in the name") and HAPI's FHIRPathEngine expands only the backtick spelling.
        // #438's first cut expanded the bare form too. The outcome for a bare name is a clear error naming
        // the whole hyphenated name - possible only because the lexing fix keeps it in one piece.
        var evaluate = () => _evaluator.Evaluate(CreateElement("x"), _parser.Parse(expression)).ToList();

        var exception = Assert.Throws<FhirPathEvaluationException>(evaluate);
        Assert.Contains($"undefined environment variable: {expectedName}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("%`vs-`", "vs-")]
    [InlineData("%`ext-`", "ext-")]
    public void GivenADelimitedButEmptySuffixConstant_WhenReferenced_ThenItIsAnUndefinedVariableRatherThanAUri(
        string expression, string expectedName)
    {
        // The delimited spelling alone is not sufficient - the prefix needs a non-empty suffix, or there
        // is no name to build a URI from. GetStandardConstant has always required that, but the analyzer
        // and the SQL-on-FHIR validator did not check length and reported these clean, so an expression
        // could pass both and then throw here. All three now share StandardConstantFamilies.
        var evaluate = () => _evaluator.Evaluate(CreateElement("x"), _parser.Parse(expression)).ToList();

        var exception = Assert.Throws<FhirPathEvaluationException>(evaluate);
        Assert.Contains($"undefined environment variable: {expectedName}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("%`vs-mine`", "http://hl7.org/fhir/ValueSet/mine")]
    [InlineData("%`ext-mine`", "http://hl7.org/fhir/StructureDefinition/mine")]
    public void GivenADelimitedVsOrExtConstant_WhenReferenced_ThenResolvesToItsUri(string expression, string expected)
    {
        // The conformant counterpart to the test above, for a name that is in neither family's published list:
        // the expansion is by rule, so any name works, but only in the delimited spelling.
        var result = _evaluator.Evaluate(CreateElement("x"), _parser.Parse(expression)).Single();

        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void GivenAHostSuppliedValueSetVariable_WhenReferenced_ThenOverridesTheGeneratedUri()
    {
        var context = new EvaluationContext()
            .WithEnvironmentVariable("vs-administrative-gender", CreateElement("http://example.org/local"));

        var result = _evaluator.Evaluate(CreateElement("x"), _parser.Parse("%`vs-administrative-gender`"), context).Single();

        Assert.Equal("http://example.org/local", result.Value);
    }

    [Fact]
    public void GivenAnExpressionUsingContext_WhenEvaluatedThroughSelect_ThenResolvesTheSameAsTheInterpreter()
    {
        // TypedElementExtensions.Select tries a compiled delegate before falling back to the interpreter, and
        // the two paths seeding %context differently is exactly the divergence class this engine has been bitten
        // by before. FhirPathDelegateCompiler declines VariableRefExpression and that null propagates up through
        // every parent compile, so any expression naming a variable takes the interpreted path - this pins that.
        var node = CreateElement("original");

        var viaExtension = node.Select("%context").ToList();
        var viaEvaluator = _evaluator.Evaluate(node, _parser.Parse("%context")).ToList();

        Assert.Single(viaExtension);
        Assert.Same(node, viaExtension[0]);
        Assert.Same(viaEvaluator[0], viaExtension[0]);
    }

    [Fact]
    public void GivenNoResourceBinding_WhenReferencingResource_ThenSelectDefaultsItAndEvaluateDoesNot()
    {
        // The two entry points differ on %resource on purpose, and this pins it so the difference cannot drift
        // into an accident. Select documents its input as "the root element" and FHIR blesses %resource =
        // %context for a resource-rooted evaluation; Evaluate's input is "the node handed to the engine", which
        // its callers routinely make a sub-element, and binding a non-resource to %resource is something FHIR
        // defines it never to be. IElement has no parent link, so the engine cannot find the containing
        // resource - only the host knows it.
        var node = CreateElement("bare");

        var viaEvaluator = _evaluator.Evaluate(node, _parser.Parse("%resource"), new EvaluationContext()).ToList();
        var viaExtension = node.Select("%resource").ToList();

        Assert.Empty(viaEvaluator);
        Assert.Single(viaExtension);
        Assert.Same(node, viaExtension[0]);
    }

    [Fact]
    public void GivenAnUnknownVariable_WhenReferenced_ThenSignalsError()
    {
        var expr = _parser.Parse("%noSuchThing");

        var exception = Assert.Throws<FhirPathEvaluationException>(
            () => _evaluator.Evaluate(CreateElement("x"), expr).ToList());

        Assert.Contains("noSuchThing", exception.Message, StringComparison.Ordinal);
    }

    private static IElement CreateElement(string value) => new TestElement(value);

    private sealed class TestElement(string value) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "string";
        public object Value { get; } = value;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
