/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPathDelegateCompiler - tests compiled vs interpreted evaluation equivalence.
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Xunit;

namespace Ignixa.FhirPath.Tests;

/// <summary>
/// Tests for FhirPathDelegateCompiler to ensure compiled delegates produce identical results
/// to interpreted evaluation, and that unsupported patterns gracefully fall back to interpreter.
/// </summary>
public class FhirPathDelegateCompilerTests
{
    private static readonly FhirPathParser Parser = new(preserveTrivia: false);
    private static readonly FhirPathEvaluator _evaluator = new();
    private static readonly FhirPathDelegateCompiler _delegateCompiler = new(_evaluator);

    private static Expression ParseExpression(string expression) => Parser.Parse(expression);

    private static IEnumerable<IElement> EvaluateInterpreted(IElement input, string expression)
    {
        var ast = ParseExpression(expression);
        return _evaluator.Evaluate(input, ast);
    }

    private static IEnumerable<IElement> EvaluateCompiled(IElement input, string expression)
    {
        var ast = ParseExpression(expression);
        var compiled = _delegateCompiler.TryCompile(ast);
        if (compiled != null)
        {
            return compiled(input, new EvaluationContext());
        }
        // Fallback if not compiled
        return _evaluator.Evaluate(input, ast);
    }

    private static void AssertEvaluationEquivalent(IElement input, string expression)
    {
        var interpreted = EvaluateInterpreted(input, expression).ToList();
        var compiled = EvaluateCompiled(input, expression).ToList();

        Assert.Equal(interpreted.Count, compiled.Count);
        for (int i = 0; i < interpreted.Count; i++)
        {
            Assert.Equal(interpreted[i].Value, compiled[i].Value);
            Assert.Equal(interpreted[i].InstanceType, compiled[i].InstanceType);
        }
    }

    #region Simple Identifier Tests

    [Fact]
    public void GivenSimpleIdentifier_WhenCompiled_ThenReturnsChildElements()
    {
        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] { new MockTypedElement("HumanName") { Value = "John Doe" } } }
            }
        };

        AssertEvaluationEquivalent(input, "name");
    }

    [Fact]
    public void GivenSimpleIdentifierWithNoChildren_WhenCompiled_ThenReturnsEmpty()
    {
        var input = new MockTypedElement("Patient") { ChildrenSetup = new() };

        AssertEvaluationEquivalent(input, "nonexistent");
    }

    #endregion

    #region Axis Expression Tests

    [Fact]
    public void GivenAxisThis_WhenCompiled_ThenReturnsInput()
    {
        var input = new MockTypedElement("Patient") { Value = "test" };

        var compiled = _delegateCompiler.TryCompile(ParseExpression("$this"));
        Assert.NotNull(compiled);

        var result = compiled(input, new EvaluationContext()).ToList();
        Assert.Single(result);
        Assert.Same(input, result[0]);
    }

    #endregion

    #region Child Expression Tests (Two-Level Paths)

    [Fact]
    public void GivenTwoLevelPath_WhenCompiled_ThenNavigatesNestedChildren()
    {
        var nameElement = new MockTypedElement("HumanName")
        {
            ChildrenSetup = new()
            {
                { "family", new[] { new MockTypedElement("string") { Value = "Doe" } } }
            }
        };

        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] { nameElement } }
            }
        };

        AssertEvaluationEquivalent(input, "name.family");
    }

    [Fact]
    public void GivenThreeLevelPath_WhenCompiled_ThenNavigatesRecursively()
    {
        var organizationElement = new MockTypedElement("Organization")
        {
            ChildrenSetup = new()
            {
                { "name", new[] { new MockTypedElement("string") { Value = "Acme Corp" } } }
            }
        };

        var practitionerElement = new MockTypedElement("Practitioner")
        {
            ChildrenSetup = new()
            {
                { "organization", new[] { organizationElement } }
            }
        };

        var input = new MockTypedElement("Endpoint")
        {
            ChildrenSetup = new()
            {
                { "managingOrganization", new[] { practitionerElement } }
            }
        };

        AssertEvaluationEquivalent(input, "managingOrganization.organization.name");
    }

    #endregion

    #region Where Clause Tests

    [Fact]
    public void GivenWhereClauseWithEquality_WhenCompiled_ThenFiltersMatches()
    {
        var telecom1 = new MockTypedElement("ContactPoint")
        {
            ChildrenSetup = new()
            {
                { "system", new[] { new MockTypedElement("code") { Value = "phone" } } },
                { "value", new[] { new MockTypedElement("string") { Value = "555-1234" } } }
            }
        };

        var telecom2 = new MockTypedElement("ContactPoint")
        {
            ChildrenSetup = new()
            {
                { "system", new[] { new MockTypedElement("code") { Value = "email" } } },
                { "value", new[] { new MockTypedElement("string") { Value = "test@example.com" } } }
            }
        };

        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "telecom", new[] { telecom1, telecom2 } }
            }
        };

        AssertEvaluationEquivalent(input, "telecom.where(system='phone')");
    }

    [Fact]
    public void GivenWhereClauseWithNoMatches_WhenCompiled_ThenReturnsEmpty()
    {
        var telecom = new MockTypedElement("ContactPoint")
        {
            ChildrenSetup = new()
            {
                { "system", new[] { new MockTypedElement("code") { Value = "email" } } }
            }
        };

        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "telecom", new[] { telecom } }
            }
        };

        AssertEvaluationEquivalent(input, "telecom.where(system='phone')");
    }

    #endregion

    #region Function Tests

    [Fact]
    public void GivenFirstFunction_WhenCompiled_ThenReturnsFirstElement()
    {
        var element1 = new MockTypedElement("string") { Value = "first" };
        var element2 = new MockTypedElement("string") { Value = "second" };

        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] { element1, element2 } }
            }
        };

        AssertEvaluationEquivalent(input, "name.first()");
    }

    [Fact]
    public void GivenFirstFunctionOnEmpty_WhenCompiled_ThenReturnsEmpty()
    {
        var input = new MockTypedElement("Patient") { ChildrenSetup = new() };

        AssertEvaluationEquivalent(input, "name.first()");
    }

    [Fact]
    public void GivenExistsFunction_WhenEvaluated_ThenProducesConsistentResults()
    {
        var element = new MockTypedElement("string") { Value = "test" };
        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] { element } }
            }
        };

        AssertEvaluationEquivalent(input, "name.exists()");
    }

    [Fact]
    public void GivenExistsFunctionOnEmpty_WhenEvaluated_ThenProducesConsistentResults()
    {
        var input = new MockTypedElement("Patient") { ChildrenSetup = new() };

        AssertEvaluationEquivalent(input, "name.exists()");
    }

    [Fact]
    public void GivenCountFunction_WhenEvaluated_ThenProducesConsistentResults()
    {
        var element1 = new MockTypedElement("string") { Value = "first" };
        var element2 = new MockTypedElement("string") { Value = "second" };
        var element3 = new MockTypedElement("string") { Value = "third" };

        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] { element1, element2, element3 } }
            }
        };

        AssertEvaluationEquivalent(input, "name.count()");
    }

    [Fact]
    public void GivenEmptyFunction_WhenEvaluated_ThenProducesConsistentResults()
    {
        var inputEmpty = new MockTypedElement("Patient") { ChildrenSetup = new() };
        var inputNonEmpty = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] { new MockTypedElement("string") { Value = "test" } } }
            }
        };

        // Test both empty and non-empty cases
        AssertEvaluationEquivalent(inputEmpty, "name.empty()");
        AssertEvaluationEquivalent(inputNonEmpty, "name.empty()");
    }

    #endregion

    #region Binary Expression Tests

    [Fact]
    public void GivenBinaryEqualityExpression_WhenEvaluated_ThenProducesConsistentResults()
    {
        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "active", new[] { new MockTypedElement("boolean") { Value = true } } }
            }
        };

        // Test equivalence between interpreted and compiled/fallback execution
        AssertEvaluationEquivalent(input, "active = true");
    }

    [Fact]
    public void GivenBinaryNotEqualExpression_WhenEvaluated_ThenProducesConsistentResults()
    {
        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "active", new[] { new MockTypedElement("boolean") { Value = true } } }
            }
        };

        // Test equivalence between interpreted and compiled/fallback execution
        AssertEvaluationEquivalent(input, "active != false");
    }

    #endregion

    #region Constant Expression Tests

    [Fact]
    public void GivenConstantString_WhenCompiled_ThenReturnsConstant()
    {
        var input = new MockTypedElement("Patient");

        var compiled = _delegateCompiler.TryCompile(ParseExpression("'test-string'"));
        Assert.NotNull(compiled);

        var result = compiled(input, new EvaluationContext()).ToList();
        Assert.Single(result);
        Assert.Equal("test-string", result[0].Value);
    }

    [Fact]
    public void GivenConstantInteger_WhenCompiled_ThenReturnsConstant()
    {
        var input = new MockTypedElement("Patient");

        var compiled = _delegateCompiler.TryCompile(ParseExpression("42"));
        Assert.NotNull(compiled);

        var result = compiled(input, new EvaluationContext()).ToList();
        Assert.Single(result);
        Assert.Equal(42, result[0].Value);
    }

    #endregion

    #region Unsupported Pattern Fallback Tests

    [Fact]
    public void GivenUnsupportedPattern_WhenCompiled_ThenReturnsNullForFallback()
    {
        // Variable references are not supported in compiler
        var compiled = _delegateCompiler.TryCompile(ParseExpression("%resource"));
        Assert.Null(compiled);
    }

    [Fact]
    public void GivenComplexExpression_WhenCompiled_ThenReturnsNullForFallback()
    {
        // Complex nested logic not supported
        var compiled = _delegateCompiler.TryCompile(ParseExpression("iif(active, 'yes', 'no')"));
        Assert.Null(compiled);
    }

    [Fact]
    public void GivenUnsupportedPattern_WhenEvaluated_ThenFallsBackToInterpreter()
    {
        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "active", new[] { new MockTypedElement("boolean") { Value = true } } }
            }
        };

        // This pattern is unsupported but should still work via fallback
        AssertEvaluationEquivalent(input, "%resource.where(resourceType='Patient')");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenEmptyInput_WhenCompiled_ThenHandlesGracefully()
    {
        var input = new MockTypedElement("Patient") { ChildrenSetup = new() };

        AssertEvaluationEquivalent(input, "name.family.given");
    }

    [Fact]
    public void GivenMultipleLevelsWithSomeEmpty_WhenCompiled_ThenNavigatesCorrectly()
    {
        var nameWithFamily = new MockTypedElement("HumanName")
        {
            ChildrenSetup = new()
            {
                { "family", new[] { new MockTypedElement("string") { Value = "Doe" } } }
            }
        };

        var nameWithoutFamily = new MockTypedElement("HumanName")
        {
            ChildrenSetup = new()
        };

        var input = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] { nameWithFamily, nameWithoutFamily } }
            }
        };

        AssertEvaluationEquivalent(input, "name.family");
    }

    #endregion

    #region SQL on FHIR Edge Cases

    /// <summary>
    /// Tests for SQL on FHIR specific FHIRPath scenarios.
    /// These cover the where() function with ofType() and comparison operators.
    /// </summary>

    [Fact]
    public void GivenWhereWithOfTypeIntegerComparison_WhenEvaluated_ThenFiltersNumericValues()
    {
        // Simulates: Observation with valueInteger property
        // WHERE: where(value.ofType(integer) > 11).exists()
        var obs1 = new MockTypedElement("Observation")
        {
            ChildrenSetup = new()
            {
                { "id", new[] { new MockTypedElement("id") { Value = "o1" } } },
                { "value", new[] { new MockTypedElement("integer") { Value = 12 } } }
            }
        };

        var obs2 = new MockTypedElement("Observation")
        {
            ChildrenSetup = new()
            {
                { "id", new[] { new MockTypedElement("id") { Value = "o2" } } },
                { "value", new[] { new MockTypedElement("integer") { Value = 10 } } }
            }
        };

        // Test: value.ofType(integer) > 11
        AssertEvaluationEquivalent(obs1, "value.ofType(integer) > 11");
        AssertEvaluationEquivalent(obs2, "value.ofType(integer) > 11");
    }

    [Fact]
    public void GivenWhereWithOfTypeLessThanComparison_WhenEvaluated_ThenFiltersCorrectly()
    {
        // Simulates: WHERE value.ofType(integer) < 11
        var obs1 = new MockTypedElement("Observation")
        {
            ChildrenSetup = new()
            {
                { "value", new[] { new MockTypedElement("integer") { Value = 10 } } }
            }
        };

        var obs2 = new MockTypedElement("Observation")
        {
            ChildrenSetup = new()
            {
                { "value", new[] { new MockTypedElement("integer") { Value = 12 } } }
            }
        };

        AssertEvaluationEquivalent(obs1, "value.ofType(integer) < 11");
        AssertEvaluationEquivalent(obs2, "value.ofType(integer) < 11");
    }

    [Fact]
    public void GivenWhereWithOfTypeAndExists_WhenEvaluated_ThenChecksPresenceOfMatchingType()
    {
        // Simulates: where(value.ofType(integer) > 11).exists()
        var obsMatch = new MockTypedElement("Observation")
        {
            ChildrenSetup = new()
            {
                { "value", new[] { new MockTypedElement("integer") { Value = 12 } } }
            }
        };

        var obsNoMatch = new MockTypedElement("Observation")
        {
            ChildrenSetup = new()
            {
                { "value", new[] { new MockTypedElement("integer") { Value = 10 } } }
            }
        };

        // Tests should be equivalent - checking if the where clause exists
        AssertEvaluationEquivalent(obsMatch, "where(value.ofType(integer) > 11).exists()");
        AssertEvaluationEquivalent(obsNoMatch, "where(value.ofType(integer) > 11).exists()");
    }

    [Fact]
    public void GivenWhereWithPathAndExists_WhenEvaluated_ThenChecksPathPresence()
    {
        // Simulates: name.where(use = 'official').exists()
        var patientWithOfficial = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] {
                    new MockTypedElement("HumanName")
                    {
                        ChildrenSetup = new()
                        {
                            { "use", new[] { new MockTypedElement("code") { Value = "official" } } }
                        }
                    }
                } }
            }
        };

        var patientWithoutOfficial = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] {
                    new MockTypedElement("HumanName")
                    {
                        ChildrenSetup = new()
                        {
                            { "use", new[] { new MockTypedElement("code") { Value = "nickname" } } }
                        }
                    }
                } }
            }
        };

        AssertEvaluationEquivalent(patientWithOfficial, "name.where(use = 'official').exists()");
        AssertEvaluationEquivalent(patientWithoutOfficial, "name.where(use = 'official').exists()");
    }

    [Fact]
    public void GivenWhereWithNonExistentPath_WhenEvaluated_ThenReturnsEmpty()
    {
        // Simulates: name.where(use = 'maiden').exists()
        // Testing against Patient with no 'maiden' names
        var patient = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] {
                    new MockTypedElement("HumanName")
                    {
                        ChildrenSetup = new()
                        {
                            { "use", new[] { new MockTypedElement("code") { Value = "official" } } }
                        }
                    }
                } }
            }
        };

        AssertEvaluationEquivalent(patient, "name.where(use = 'maiden').exists()");
    }

    [Fact]
    public void GivenWhereClauseWithLogicalAnd_WhenEvaluated_ThenCombinesConditions()
    {
        // Test: where() with 'and' connector
        var patient = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] {
                    new MockTypedElement("HumanName")
                    {
                        ChildrenSetup = new()
                        {
                            { "use", new[] { new MockTypedElement("code") { Value = "official" } } },
                            { "family", new[] { new MockTypedElement("string") { Value = "Smith" } } }
                        }
                    }
                } }
            }
        };

        AssertEvaluationEquivalent(patient, "name.where(use = 'official' and family.exists())");
    }

    [Fact]
    public void GivenWhereClauseWithLogicalOr_WhenEvaluated_ThenCombinesConditions()
    {
        // Test: where() with 'or' connector
        var patient = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "name", new[] {
                    new MockTypedElement("HumanName")
                    {
                        ChildrenSetup = new()
                        {
                            { "use", new[] { new MockTypedElement("code") { Value = "nickname" } } }
                        }
                    },
                    new MockTypedElement("HumanName")
                    {
                        ChildrenSetup = new()
                        {
                            { "use", new[] { new MockTypedElement("code") { Value = "official" } } }
                        }
                    }
                } }
            }
        };

        AssertEvaluationEquivalent(patient, "name.where(use = 'official' or use = 'nickname')");
    }

    [Fact]
    public void GivenWhereClauseThatEvaluatesToTrueWhenEmpty_WhenEvaluated_ThenHandlesEmptyArrays()
    {
        // Test: where() that should evaluate true when result set is empty
        var patient = new MockTypedElement("Patient")
        {
            ChildrenSetup = new()
            {
                { "contact", Array.Empty<IElement>() }  // Empty array
            }
        };

        // This tests behavior when array is empty
        AssertEvaluationEquivalent(patient, "contact.empty()");
    }

    #endregion
}

/// <summary>
/// Mock IElement for testing - provides a simple in-memory tree structure.
/// </summary>
internal class MockTypedElement : IElement
{
    private readonly Dictionary<string, IElement[]> _childrenMap = new();

    public MockTypedElement(string instanceType)
    {
        InstanceType = instanceType;
        Name = instanceType;
    }

    public string Name { get; set; }
    public string InstanceType { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string Location => "[mock]";
    public IType? Type => null;
    public bool HasPrimitiveValue => Value != null;

    /// <summary>
    /// Allows setting up the mock's children dictionary for test setup.
    /// </summary>
    public Dictionary<string, IElement[]> ChildrenSetup
    {
        get => _childrenMap;
        set
        {
            _childrenMap.Clear();
            foreach (var kvp in value)
            {
                _childrenMap[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Implements IElement.Children to return child elements by name.
    /// </summary>
    public IReadOnlyList<IElement> Children(string? name = null)
    {
        if (name == null)
        {
            return _childrenMap.Values.SelectMany(c => c).ToList();
        }

        return _childrenMap.TryGetValue(name, out var children) ? children.ToList() : Array.Empty<IElement>();
    }

    public T? Meta<T>() where T : class => null;
}
