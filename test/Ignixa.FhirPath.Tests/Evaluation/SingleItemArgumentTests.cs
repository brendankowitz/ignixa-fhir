/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins how functions treat an argument or input that evaluates to several items (#537).
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.TestHelpers;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// FHIRPath "Singleton Evaluation of Collections": where a function expects a single item and the
/// collection holds several, evaluation ends and signals an error. That is a data-dependent condition - a
/// repeating element as an argument triggers it - so it must surface as
/// <see cref="FhirPathEvaluationException"/>, which hosts classify as an expression error, and not as the
/// <see cref="InvalidOperationException"/> that <c>SingleOrDefault()</c> throws.
/// </summary>
public class SingleItemArgumentTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("indexOf", "'abc'.indexOf('a' | 'b')")]
    [InlineData("lastIndexOf", "'abc'.lastIndexOf('a' | 'b')")]
    [InlineData("join", "('a' | 'b').join(',' | ';')")]
    [InlineData("extension", "name.extension('u' | 'v').count()")]
    [InlineData("substring", "'abc'.substring(1 | 2)")]
    [InlineData("substring", "'abc'.substring(0, 1 | 2)")]
    [InlineData("startsWith", "'abc'.startsWith('a' | 'b')")]
    [InlineData("endsWith", "'abc'.endsWith('a' | 'b')")]
    [InlineData("contains", "'abc'.contains('a' | 'b')")]
    [InlineData("replace", "'abc'.replace('a' | 'b', 'x')")]
    [InlineData("replace", "'abc'.replace('a', 'x' | 'y')")]
    [InlineData("matches", "'abc'.matches('a' | 'b')")]
    [InlineData("matchesFull", "'abc'.matchesFull('a' | 'b')")]
    [InlineData("matchesFull", "'abc'.matchesFull('a', 'i' | 'm')")]
    [InlineData("replaceMatches", "'abc'.replaceMatches('a' | 'b', 'x')")]
    [InlineData("replaceMatches", "'abc'.replaceMatches('a', 'x' | 'y')")]
    [InlineData("split", "'a,b'.split(',' | ';')")]
    [InlineData("skip", "(1 | 2 | 3).skip(1 | 2)")]
    [InlineData("take", "(1 | 2 | 3).take(1 | 2)")]
    [InlineData("round", "1.5.round(1 | 2)")]
    [InlineData("power", "2.power(1 | 2)")]
    [InlineData("log", "8.log(2 | 3)")]
    [InlineData("encode", "'abc'.encode('base64' | 'hex')")]
    [InlineData("decode", "'abc'.decode('base64' | 'hex')")]
    [InlineData("escape", "'abc'.escape('json' | 'html')")]
    [InlineData("unescape", "'abc'.unescape('json' | 'html')")]
    public void GivenAMultiItemArgument_WhenEvaluated_ThenItIsAnEvaluationErrorNamingTheFunction(
        string function,
        string expression)
    {
        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        var failure = Should.Throw<FhirPathEvaluationException>(evaluate);
        failure.Message.ShouldContain($"{function}()");
        failure.Message.ShouldContain("single item");
    }

    [Theory]
    [InlineData("encode", "('a' | 'b').encode('base64')")]
    [InlineData("decode", "('a' | 'b').decode('base64')")]
    [InlineData("escape", "('a' | 'b').escape('json')")]
    [InlineData("unescape", "('a' | 'b').unescape('json')")]
    public void GivenAMultiItemInput_WhenEvaluated_ThenItIsAnEvaluationErrorNamingTheFunction(
        string function,
        string expression)
    {
        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        var failure = Should.Throw<FhirPathEvaluationException>(evaluate);
        failure.Message.ShouldContain($"{function}()");
    }

    [Theory]
    [InlineData("'abc'.substring(name.given)")]
    [InlineData("'abc'.startsWith(name.given)")]
    [InlineData("'abc'.replace(name.given, 'x')")]
    [InlineData("name.given.skip(name.given.count() | 1)")]
    public void GivenARepeatingElementAsAnArgument_WhenEvaluated_ThenItIsAnEvaluationError(string expression)
    {
        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
    }

    [Theory]
    [InlineData("'abc'.indexOf({})")]
    [InlineData("'abc'.substring({})")]
    [InlineData("'abc'.startsWith({})")]
    [InlineData("'abc'.replace({}, 'x')")]
    [InlineData("'abc'.matches({})")]
    [InlineData("'a,b'.split({})")]
    [InlineData("(1 | 2 | 3).skip({})")]
    [InlineData("1.5.round({})")]
    [InlineData("2.power({})")]
    [InlineData("8.log({})")]
    public void GivenAnEmptyArgument_WhenEvaluated_ThenTheResultIsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    private List<Ignixa.Abstractions.IElement> Evaluate(string expression)
    {
        var patient = FuzzPatient.Create();
        var context = new EvaluationContext { Resource = patient, RootResource = patient, Schema = FuzzPatient.Schema };
        return _evaluator.Evaluate(patient, _parser.Parse(expression), context).ToList();
    }
}
