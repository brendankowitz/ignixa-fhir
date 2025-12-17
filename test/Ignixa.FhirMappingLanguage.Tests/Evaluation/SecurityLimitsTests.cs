/* Copyright (c) 2025, Ignixa Contributors */

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class SecurityLimitsTests
{
    #region MaxRecursionDepth

    [Fact]
    public void GivenCircularGroupCalls_WhenMaxRecursionDepthExceeded_ThenThrowsMappingExecutionException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxRecursionDepth = 5
        };

        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "GroupA",
                    parameters: [],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: "callB",
                            sources: [],
                            targets: [],
                            dependent: new GroupInvocationExpression(
                                groupName: "GroupB",
                                arguments: []))
                    ]),
                new GroupExpression(
                    name: "GroupB",
                    parameters: [],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: "callA",
                            sources: [],
                            targets: [],
                            dependent: new GroupInvocationExpression(
                                groupName: "GroupA",
                                arguments: []))
                    ])
            ]);

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext();

        // Act
        var act = () => evaluator.Execute(map, context);

        // Assert
        var ex = Should.Throw<MappingExecutionException>(act);
        ex.Message.ShouldContain("Maximum recursion depth exceeded");
        ex.Message.ShouldContain("Check for circular group calls");
    }

    [Fact]
    public void GivenDeepGroupHierarchy_WhenWithinLimit_ThenExecutesSuccessfully()
    {
        // Arrange - Create a deep hierarchy that's within the limit
        var options = new MappingEvaluatorOptions
        {
            MaxRecursionDepth = 10
        };

        // Create groups: Group0 -> Group1 -> Group2 -> ... -> Group5
        var groups = new List<GroupExpression>();
        for (int i = 5; i >= 0; i--)
        {
            var dependent = i < 5
                ? new GroupInvocationExpression($"Group{i + 1}", [])
                : null;

            groups.Add(new GroupExpression(
                name: $"Group{i}",
                parameters: [],
                extends: null,
                rules:
                [
                    new RuleExpression(
                        name: $"rule{i}",
                        sources: [],
                        targets: [],
                        dependent: dependent)
                ]));
        }

        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups: groups);

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext();

        // Act
        var act = () => evaluator.Execute(map, context);

        // Assert - Should not throw
        Should.NotThrow(act);
    }

    #endregion

    #region MaxElementsCreated

    [Fact]
    public void GivenManyElementsCreated_WhenMaxElementsExceeded_ThenThrowsMappingExecutionException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxElementsCreated = 10
        };

        // Create a map that tries to create many elements via literal values
        var targets = new List<TargetExpression>();
        for (int i = 0; i < 15; i++)
        {
            targets.Add(new TargetExpression(
                context: new IdentifierExpression("tgt"),
                variable: $"var{i}",
                transform: new LiteralExpression($"value{i}"),
                listMode: null));
        }

        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "Main",
                    parameters:
                    [
                        new ParameterExpression(ParameterMode.Source, "src", "Patient"),
                        new ParameterExpression(ParameterMode.Target, "tgt", "Bundle")
                    ],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: "createMany",
                            sources:
                            [
                                new SourceExpression(
                                    context: new IdentifierExpression("src"),
                                    variable: "s",
                                    type: null,
                                    condition: null,
                                    check: null,
                                    log: null,
                                    defaultValue: null,
                                    cardinality: new Cardinality(1, 1))
                            ],
                            targets: targets,
                            dependent: null)
                    ])
            ]);

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext();
        context.SetSource("src", new TestElement("Patient", "src"));
        context.SetTarget("tgt", new TestElement("Bundle", "tgt"));

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Main", context);

        // Assert
        Should.Throw<MappingExecutionException>(act).Message.ShouldContain("Maximum elements created exceeded");
    }

    [Fact]
    public void GivenElementCreationWithinLimit_WhenExecuted_ThenSucceeds()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxElementsCreated = 100
        };

        var targets = new List<TargetExpression>();
        for (int i = 0; i < 5; i++)
        {
            targets.Add(new TargetExpression(
                context: new IdentifierExpression("tgt"),
                variable: $"var{i}",
                transform: new LiteralExpression($"value{i}"),
                listMode: null));
        }

        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "Main",
                    parameters:
                    [
                        new ParameterExpression(ParameterMode.Source, "src", "Patient"),
                        new ParameterExpression(ParameterMode.Target, "tgt", "Bundle")
                    ],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: "createFew",
                            sources:
                            [
                                new SourceExpression(
                                    context: new IdentifierExpression("src"),
                                    variable: "s",
                                    type: null,
                                    condition: null,
                                    check: null,
                                    log: null,
                                    defaultValue: null,
                                    cardinality: new Cardinality(1, 1))
                            ],
                            targets: targets,
                            dependent: null)
                    ])
            ]);

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext();
        context.SetSource("src", new TestElement("Patient", "src"));
        context.SetTarget("tgt", new TestElement("Bundle", "tgt"));

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Main", context);

        // Assert
        Should.NotThrow(act);
    }

    #endregion

    #region MaxErrorsCollected

    [Fact]
    public void GivenManyErrors_WhenMaxErrorsCollectedExceeded_ThenThrowsMappingExecutionException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            ErrorMode = ErrorMode.Lenient,
            MaxErrorsCollected = 5
        };

        // Create rules that will fail (referencing non-existent properties with required cardinality)
        var rules = new List<RuleExpression>();
        for (int i = 0; i < 10; i++)
        {
            rules.Add(new RuleExpression(
                name: $"failingRule{i}",
                sources:
                [
                    new SourceExpression(
                        context: new QualifiedIdentifierExpression(
                            new IdentifierExpression("src"),
                            $"nonExistent{i}"),
                        variable: $"v{i}",
                        type: null,
                        condition: null,
                        check: null,
                        log: null,
                        defaultValue: null,
                        cardinality: new Cardinality(1, 1)) // Require exactly 1
                ],
                targets: [],
                dependent: null));
        }

        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "Main",
                    parameters:
                    [
                        new ParameterExpression(ParameterMode.Source, "src", "Patient")
                    ],
                    extends: null,
                    rules: rules)
            ]);

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };
        context.SetSource("src", new TestElement("Patient", "src"));

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Main", context);

        // Assert
        var ex = Should.Throw<MappingExecutionException>(act);
        ex.Message.ShouldContain("Maximum errors collected");
        ex.Message.ShouldContain("prevent memory exhaustion");
    }

    [Fact]
    public void GivenErrorsWithinLimit_WhenExecutedInLenientMode_ThenCollectsErrors()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            ErrorMode = ErrorMode.Lenient,
            MaxErrorsCollected = 10
        };

        var rules = new List<RuleExpression>
        {
            new RuleExpression(
                name: "failingRule1",
                sources:
                [
                    new SourceExpression(
                        context: new QualifiedIdentifierExpression(
                            new IdentifierExpression("src"),
                            "nonExistent1"),
                        variable: "v1",
                        type: null,
                        condition: null,
                        check: null,
                        log: null,
                        defaultValue: null,
                        cardinality: new Cardinality(1, 1))
                ],
                targets: [],
                dependent: null),
            new RuleExpression(
                name: "failingRule2",
                sources:
                [
                    new SourceExpression(
                        context: new QualifiedIdentifierExpression(
                            new IdentifierExpression("src"),
                            "nonExistent2"),
                        variable: "v2",
                        type: null,
                        condition: null,
                        check: null,
                        log: null,
                        defaultValue: null,
                        cardinality: new Cardinality(1, 1))
                ],
                targets: [],
                dependent: null)
        };

        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "Main",
                    parameters:
                    [
                        new ParameterExpression(ParameterMode.Source, "src", "Patient")
                    ],
                    extends: null,
                    rules: rules)
            ]);

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };
        context.SetSource("src", new TestElement("Patient", "src"));

        // Act
        evaluator.ExecuteGroup(map, "Main", context);

        // Assert
        context.Errors.Count.ShouldBe(2);
        context.Errors[0].RuleName.ShouldBe("failingRule1");
        context.Errors[1].RuleName.ShouldBe("failingRule2");
    }

    #endregion

    #region Error Message Context

    [Fact]
    public void GivenRecursionLimitError_WhenThrown_ThenIncludesGroupName()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxRecursionDepth = 2
        };

        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "RecursiveGroup",
                    parameters: [],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: "recurse",
                            sources: [],
                            targets: [],
                            dependent: new GroupInvocationExpression("RecursiveGroup", []))
                    ])
            ]);

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext();

        // Act
        var act = () => evaluator.Execute(map, context);

        // Assert
        var exception = Should.Throw<MappingExecutionException>(act);
        exception.Message.ShouldContain("Check for circular group calls");
        exception.Message.ShouldContain("RecursiveGroup");
    }

    [Fact]
    public void GivenElementCreationLimitError_WhenThrown_ThenIncludesLimit()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxElementsCreated = 3
        };

        var targets = new List<TargetExpression>();
        for (int i = 0; i < 5; i++)
        {
            targets.Add(new TargetExpression(
                context: new IdentifierExpression("tgt"),
                variable: $"var{i}",
                transform: new LiteralExpression($"value{i}"),
                listMode: null));
        }

        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "Main",
                    parameters:
                    [
                        new ParameterExpression(ParameterMode.Source, "src", "Patient"),
                        new ParameterExpression(ParameterMode.Target, "tgt", "Bundle")
                    ],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: "create",
                            sources:
                            [
                                new SourceExpression(
                                    context: new IdentifierExpression("src"),
                                    variable: "s",
                                    type: null,
                                    condition: null,
                                    check: null,
                                    log: null,
                                    defaultValue: null,
                                    cardinality: new Cardinality(1, 1))
                            ],
                            targets: targets,
                            dependent: null)
                    ])
            ]);

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext();
        context.SetSource("src", new TestElement("Patient", "src"));
        context.SetTarget("tgt", new TestElement("Bundle", "tgt"));

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Main", context);

        // Assert
        var exception = Should.Throw<MappingExecutionException>(act);
        exception.Message.ShouldContain("Maximum elements created exceeded");
        exception.Message.ShouldContain("(3)");
    }

    #endregion

    #region Helper Classes

    private class TestElement : IElement
    {
        private readonly Dictionary<string, List<IElement>> _children = new();

        public TestElement(string instanceType, string name, object? value = null)
        {
            InstanceType = instanceType;
            Name = name;
            Value = value;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => $"{InstanceType}.{Name}";
        public IType? Type => null;

        public void AddChild(string propertyName, IElement child)
        {
            if (!_children.ContainsKey(propertyName))
            {
                _children[propertyName] = [];
            }
            _children[propertyName].Add(child);
        }

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children.Values.SelectMany(x => x).ToList();
            }

            return _children.TryGetValue(name, out var children) ? children : [];
        }

        public T? Meta<T>() where T : class => null;
    }

    #endregion
}
