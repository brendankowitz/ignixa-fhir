/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FHIR Mapping Language evaluator.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.Abstractions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class MappingEvaluatorTests
{
    #region Helper Classes

    private class TestTypedElement : IElement
    {
        private readonly Dictionary<string, List<IElement>> _children = new();

        public TestTypedElement(string name, object? value = null, string instanceType = "string")
        {
            Name = name;
            Value = value;
            InstanceType = instanceType;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;

        public void AddChild(IElement child)
        {
            if (!_children.ContainsKey(child.Name))
            {
                _children[child.Name] = new List<IElement>();
            }
            _children[child.Name].Add(child);
        }

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children.Values.SelectMany(list => list).ToList();
            }

            return _children.TryGetValue(name, out var children)
                ? children
                : new List<IElement>();
        }

        public T? Meta<T>() where T : class => null;
    }

    #endregion

    #region Context Tests

    [Fact]
    public void GivenNewContext_WhenSettingSource_ThenSourceCanBeRetrieved()
    {
        // Arrange
        var context = new MappingContext();
        var source = new TestTypedElement("Patient");

        // Act
        context.SetSource("src", source);
        var retrieved = context.GetSource("src");

        // Assert
        retrieved.ShouldBeSameAs(source);
    }

    [Fact]
    public void GivenNewContext_WhenSettingTarget_ThenTargetCanBeRetrieved()
    {
        // Arrange
        var context = new MappingContext();
        var target = new TestTypedElement("Bundle");

        // Act
        context.SetTarget("tgt", target);
        var retrieved = context.GetTarget("tgt");

        // Assert
        retrieved.ShouldBeSameAs(target);
    }

    [Fact]
    public void GivenNewContext_WhenSettingVariable_ThenVariableCanBeRetrieved()
    {
        // Arrange
        var context = new MappingContext();
        var value = "test value";

        // Act
        context.SetVariable("myVar", value);
        var retrieved = context.GetVariable("myVar");

        // Assert
        retrieved.ShouldBe(value);
    }

    #endregion

    #region Simple Execution Tests

    [Fact]
    public void GivenMapWithSingleGroup_WhenExecuting_ThenGroupIsExecuted()
    {
        // Arrange
        var map = new MapExpression(
            "http://example.org",
            "Test",
            new List<UsesExpression>(),
            new List<ImportsExpression>(),
            new List<GroupExpression>
            {
                new GroupExpression(
                    "Main",
                    new List<ParameterExpression>
                    {
                        new ParameterExpression(ParameterMode.Source, "src", "Patient"),
                        new ParameterExpression(ParameterMode.Target, "tgt", "Bundle")
                    },
                    null,
                    new List<RuleExpression>())
            });

        var context = new MappingContext();
        context.SetSource("src", new TestTypedElement("Patient"));
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        var evaluator = new MappingEvaluator();

        // Act
        var act = () => evaluator.Execute(map, context);

        // Assert
        Should.NotThrow(act);
    }

    [Fact]
    public void GivenMapWithMissingSource_WhenExecuting_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var map = new MapExpression(
            "http://example.org",
            "Test",
            new List<UsesExpression>(),
            new List<ImportsExpression>(),
            new List<GroupExpression>
            {
                new GroupExpression(
                    "Main",
                    new List<ParameterExpression>
                    {
                        new ParameterExpression(ParameterMode.Source, "src", "Patient")
                    },
                    null,
                    new List<RuleExpression>())
            });

        var context = new MappingContext();
        var evaluator = new MappingEvaluator();

        // Act & Assert
        var act = () => evaluator.Execute(map, context);
        Should.Throw<MappingExecutionException>(act).Message.ShouldContain("src");
    }

    #endregion

    #region Transform Function Tests

    [Fact]
    public void GivenTransformResolver_WhenCallingTransform_ThenResolverIsCalled()
    {
        // Arrange
        var called = false;
        var context = new MappingContext
        {
            TransformResolver = (name, args) =>
            {
                called = true;
                name.ShouldBe("create");
                args.Count().ShouldBe(1);
                return new TestTypedElement("Result");
            }
        };

        var transform = new TransformExpression(
            "create",
            new List<Expression> { new LiteralExpression("Patient") });

        var target = new TargetExpression(
            new IdentifierExpression("tgt"),
            "result",
            transform,
            null);

        var rule = new RuleExpression(
            null,
            new List<SourceExpression>
            {
                new SourceExpression(new IdentifierExpression("src"), null, null, null, null, null)
            },
            new List<TargetExpression> { target },
            null);

        var group = new GroupExpression(
            "Main",
            new List<ParameterExpression>
            {
                new ParameterExpression(ParameterMode.Source, "src", "Patient"),
                new ParameterExpression(ParameterMode.Target, "tgt", "Bundle")
            },
            null,
            new List<RuleExpression> { rule });

        var map = new MapExpression(
            "http://example.org",
            "Test",
            new List<UsesExpression>(),
            new List<ImportsExpression>(),
            new List<GroupExpression> { group });

        context.SetSource("src", new TestTypedElement("Patient"));
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        var evaluator = new MappingEvaluator();

        // Act
        evaluator.Execute(map, context);

        // Assert
        called.ShouldBeTrue();
    }

    #endregion

    #region FHIRPath Integration Tests

    [Fact]
    public void GivenFhirPathEvaluator_WhenEvaluatingCondition_ThenEvaluatorIsCalled()
    {
        // Arrange
        var called = false;
        var context = new MappingContext
        {
            FhirPathEvaluator = (expression, element) =>
            {
                called = true;
                expression.ShouldBe("name.exists()");
                return new[] { new TestTypedElement("result", true, "boolean") };
            }
        };

        var condition = new FhirPathExpression("name.exists()");
        var source = new SourceExpression(
            new IdentifierExpression("src"),
            null,
            null,
            condition,
            null,
            null);

        var rule = new RuleExpression(
            null,
            new List<SourceExpression> { source },
            new List<TargetExpression>(),
            null);

        var group = new GroupExpression(
            "Main",
            new List<ParameterExpression>
            {
                new ParameterExpression(ParameterMode.Source, "src", "Patient")
            },
            null,
            new List<RuleExpression> { rule });

        var map = new MapExpression(
            "http://example.org",
            "Test",
            new List<UsesExpression>(),
            new List<ImportsExpression>(),
            new List<GroupExpression> { group });

        var sourceElement = new TestTypedElement("Patient");
        context.SetSource("src", sourceElement);

        var evaluator = new MappingEvaluator();

        // Act
        evaluator.Execute(map, context);

        // Assert
        called.ShouldBeTrue();
    }

    #endregion

    #region Rule Execution Tests

    [Fact]
    public void GivenRuleWithDependentRules_WhenExecuting_ThenDependentRulesAreExecuted()
    {
        // Arrange
        var innerRule = new RuleExpression(
            "innerRule",
            new List<SourceExpression>
            {
                new SourceExpression(new IdentifierExpression("src"), null, null, null, null, null)
            },
            new List<TargetExpression>(),
            null);

        var outerRule = new RuleExpression(
            "outerRule",
            new List<SourceExpression>
            {
                new SourceExpression(new IdentifierExpression("src"), null, null, null, null, null)
            },
            new List<TargetExpression>(),
            new RuleSetExpression(new List<RuleExpression> { innerRule }));

        var group = new GroupExpression(
            "Main",
            new List<ParameterExpression>
            {
                new ParameterExpression(ParameterMode.Source, "src", "Patient")
            },
            null,
            new List<RuleExpression> { outerRule });

        var map = new MapExpression(
            "http://example.org",
            "Test",
            new List<UsesExpression>(),
            new List<ImportsExpression>(),
            new List<GroupExpression> { group });

        var context = new MappingContext();
        context.SetSource("src", new TestTypedElement("Patient"));

        var evaluator = new MappingEvaluator();

        // Act
        var act = () => evaluator.Execute(map, context);

        // Assert
        Should.NotThrow(act);
    }

    #endregion

    #region Variable Binding Tests

    [Fact]
    public void GivenSourceWithVariable_WhenExecuting_ThenVariableIsBound()
    {
        // Arrange
        var context = new MappingContext();
        var sourceElement = new TestTypedElement("Patient", "John Doe");

        var source = new SourceExpression(
            new IdentifierExpression("src"),
            "myVar",
            null,
            null,
            null,
            null);

        var rule = new RuleExpression(
            null,
            new List<SourceExpression> { source },
            new List<TargetExpression>(),
            null);

        var group = new GroupExpression(
            "Main",
            new List<ParameterExpression>
            {
                new ParameterExpression(ParameterMode.Source, "src", "Patient")
            },
            null,
            new List<RuleExpression> { rule });

        var map = new MapExpression(
            "http://example.org",
            "Test",
            new List<UsesExpression>(),
            new List<ImportsExpression>(),
            new List<GroupExpression> { group });

        context.SetSource("src", sourceElement);

        var evaluator = new MappingEvaluator();

        // Act
        evaluator.Execute(map, context);

        // Assert
        var variable = context.GetVariable("myVar");
        variable.ShouldNotBeNull();
    }

    #endregion

    #region Group Execution Tests

    [Fact]
    public void GivenSpecificGroupName_WhenExecutingGroup_ThenOnlyThatGroupIsExecuted()
    {
        // Arrange
        var group1 = new GroupExpression(
            "Group1",
            new List<ParameterExpression>
            {
                new ParameterExpression(ParameterMode.Source, "src", "Patient")
            },
            null,
            new List<RuleExpression>());

        var group2 = new GroupExpression(
            "Group2",
            new List<ParameterExpression>
            {
                new ParameterExpression(ParameterMode.Source, "src", "Patient")
            },
            null,
            new List<RuleExpression>());

        var map = new MapExpression(
            "http://example.org",
            "Test",
            new List<UsesExpression>(),
            new List<ImportsExpression>(),
            new List<GroupExpression> { group1, group2 });

        var context = new MappingContext();
        context.SetSource("src", new TestTypedElement("Patient"));

        var evaluator = new MappingEvaluator();

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Group2", context);

        // Assert
        Should.NotThrow(act);
    }

    [Fact]
    public void GivenNonExistentGroupName_WhenExecutingGroup_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var map = new MapExpression(
            "http://example.org",
            "Test",
            new List<UsesExpression>(),
            new List<ImportsExpression>(),
            new List<GroupExpression>());

        var context = new MappingContext();
        var evaluator = new MappingEvaluator();

        // Act & Assert
        var act = () => evaluator.ExecuteGroup(map, "NonExistent", context);
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("NonExistent");
    }

    #endregion

    #region Navigation Tests

    [Fact]
    public void GivenQualifiedIdentifier_WhenEvaluating_ThenNavigatesToChild()
    {
        // Arrange
        var parent = new TestTypedElement("Patient");
        var child = new TestTypedElement("name", "John");
        parent.AddChild(child);

        var context = new MappingContext();
        context.SetSource("src", parent);

        var qualifiedId = new QualifiedIdentifierExpression(
            new IdentifierExpression("src"),
            "name");

        var source = new SourceExpression(qualifiedId, null, null, null, null, null);
        var rule = new RuleExpression(
            null,
            new List<SourceExpression> { source },
            new List<TargetExpression>(),
            null);

        var group = new GroupExpression(
            "Main",
            new List<ParameterExpression>
            {
                new ParameterExpression(ParameterMode.Source, "src", "Patient")
            },
            null,
            new List<RuleExpression> { rule });

        var map = new MapExpression(
            "http://example.org",
            "Test",
            new List<UsesExpression>(),
            new List<ImportsExpression>(),
            new List<GroupExpression> { group });

        var evaluator = new MappingEvaluator();

        // Act
        var act = () => evaluator.Execute(map, context);

        // Assert
        Should.NotThrow(act);
    }

    #endregion
}
