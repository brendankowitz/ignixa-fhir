/* Copyright (c) 2025, Ignixa Contributors */

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class EnhancedErrorMessageTests
{
    #region Error Messages During Evaluation

    [Fact]
    public void GivenMissingSourceElement_WhenEvaluatedInLenientMode_ThenErrorShowsAvailableElements()
    {
        // Arrange - Map tries to access src.nonExistentField
        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "TestGroup",
                    parameters:
                    [
                        new ParameterExpression(ParameterMode.Source, "src", "Patient")
                    ],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: "copyNonExistent",
                            sources:
                            [
                                new SourceExpression(
                                    context: new QualifiedIdentifierExpression(
                                        new IdentifierExpression("src"),
                                        "nonExistentField"),
                                    variable: "value",
                                    type: null,
                                    condition: null,
                                    check: null,
                                    log: null,
                                    defaultValue: null,
                                    cardinality: new Cardinality(1, 1)) // Require exactly 1
                            ],
                            targets: [],
                            dependent: null)
                    ])
            ]);

        var options = new MappingEvaluatorOptions
        {
            ErrorMode = ErrorMode.Lenient
        };

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var srcElement = new TestElement("Patient", "src");
        srcElement.AddChild("id", new TestElement("id", "id", "123"));
        srcElement.AddChild("name", new TestElement("HumanName", "name"));
        srcElement.AddChild("gender", new TestElement("code", "gender", "male"));
        context.SetSource("src", srcElement);

        // Act
        evaluator.ExecuteGroup(map, "TestGroup", context);

        // Assert
        context.Errors.Count.ShouldBe(1);

        var error = context.Errors[0];
        error.RuleName.ShouldBe("copyNonExistent");
        error.GroupName.ShouldBe("TestGroup");
        error.RuleIndex.ShouldBe(0);
        error.ElementPath.ShouldBe("src.nonExistentField");
        error.AvailableElements.ShouldNotBeNull();
        error.AvailableElements.ShouldContain("id");
        error.AvailableElements.ShouldContain("name");
        error.AvailableElements.ShouldContain("gender");

        // Verify toString shows helpful message
        var errorMessage = error.ToString();
        errorMessage.ShouldContain("Rule 'copyNonExistent'");
        errorMessage.ShouldContain("Available elements:");
        errorMessage.ShouldContain("id");
        errorMessage.ShouldContain("name");
        errorMessage.ShouldContain("gender");
        errorMessage.ShouldContain("Location: StructureMap.group[TestGroup].rule[0]");
    }

    [Fact]
    public void GivenMultipleRules_WhenErrorsOccur_ThenEachErrorShowsCorrectRuleIndex()
    {
        // Arrange - Map with multiple failing rules
        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "TestGroup",
                    parameters:
                    [
                        new ParameterExpression(ParameterMode.Source, "src", "Patient")
                    ],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: "rule0",
                            sources:
                            [
                                new SourceExpression(
                                    context: new QualifiedIdentifierExpression(
                                        new IdentifierExpression("src"),
                                        "field0"),
                                    variable: "v0",
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
                            name: "rule1",
                            sources:
                            [
                                new SourceExpression(
                                    context: new QualifiedIdentifierExpression(
                                        new IdentifierExpression("src"),
                                        "field1"),
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
                            name: "rule2",
                            sources:
                            [
                                new SourceExpression(
                                    context: new QualifiedIdentifierExpression(
                                        new IdentifierExpression("src"),
                                        "field2"),
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
                    ])
            ]);

        var options = new MappingEvaluatorOptions
        {
            ErrorMode = ErrorMode.Lenient
        };

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        context.SetSource("src", new TestElement("Patient", "src"));

        // Act
        evaluator.ExecuteGroup(map, "TestGroup", context);

        // Assert
        context.Errors.Count.ShouldBe(3);

        context.Errors[0].RuleName.ShouldBe("rule0");
        context.Errors[0].RuleIndex.ShouldBe(0);
        context.Errors[0].ElementPath.ShouldBe("src.field0");

        context.Errors[1].RuleName.ShouldBe("rule1");
        context.Errors[1].RuleIndex.ShouldBe(1);
        context.Errors[1].ElementPath.ShouldBe("src.field1");

        context.Errors[2].RuleName.ShouldBe("rule2");
        context.Errors[2].RuleIndex.ShouldBe(2);
        context.Errors[2].ElementPath.ShouldBe("src.field2");
    }

    [Fact]
    public void GivenAnonymousRule_WhenErrorOccurs_ThenErrorShowsAnonymousInRuleName()
    {
        // Arrange - Rule without a name
        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "TestGroup",
                    parameters:
                    [
                        new ParameterExpression(ParameterMode.Source, "src", "Patient")
                    ],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: null, // Anonymous rule
                            sources:
                            [
                                new SourceExpression(
                                    context: new QualifiedIdentifierExpression(
                                        new IdentifierExpression("src"),
                                        "missing"),
                                    variable: "v",
                                    type: null,
                                    condition: null,
                                    check: null,
                                    log: null,
                                    defaultValue: null,
                                    cardinality: new Cardinality(1, 1))
                            ],
                            targets: [],
                            dependent: null)
                    ])
            ]);

        var options = new MappingEvaluatorOptions
        {
            ErrorMode = ErrorMode.Lenient
        };

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        context.SetSource("src", new TestElement("Patient", "src"));

        // Act
        evaluator.ExecuteGroup(map, "TestGroup", context);

        // Assert
        context.Errors.Count.ShouldBe(1);
        var error = context.Errors[0];

        // When rule name is null, evaluator uses "anonymous"
        var errorMessage = error.ToString();
        errorMessage.ShouldContain("anonymous");
    }

    [Fact]
    public void GivenNestedPropertyAccess_WhenErrorOccurs_ThenElementPathShowsFullPath()
    {
        // Arrange - src.name.nonExistent (where src.name exists but src.name.nonExistent doesn't)
        var map = new MapExpression(
            url: "http://example.org/map",
            identifier: "TestMap",
            uses: [],
            imports: [],
            groups:
            [
                new GroupExpression(
                    name: "TestGroup",
                    parameters:
                    [
                        new ParameterExpression(ParameterMode.Source, "src", "Patient")
                    ],
                    extends: null,
                    rules:
                    [
                        new RuleExpression(
                            name: "accessNestedField",
                            sources:
                            [
                                new SourceExpression(
                                    context: new QualifiedIdentifierExpression(
                                        new QualifiedIdentifierExpression(
                                            new IdentifierExpression("src"),
                                            "name"),
                                        "nonExistent"),
                                    variable: "value",
                                    type: null,
                                    condition: null,
                                    check: null,
                                    log: null,
                                    defaultValue: null,
                                    cardinality: new Cardinality(1, 1))
                            ],
                            targets: [],
                            dependent: null)
                    ])
            ]);

        var options = new MappingEvaluatorOptions
        {
            ErrorMode = ErrorMode.Lenient
        };

        var evaluator = new MappingEvaluator(options);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var srcElement = new TestElement("Patient", "src");
        var nameElement = new TestElement("HumanName", "name");
        nameElement.AddChild("family", new TestElement("string", "family", "Smith"));
        nameElement.AddChild("given", new TestElement("string", "given", "John"));
        srcElement.AddChild("name", nameElement);
        context.SetSource("src", srcElement);

        // Act
        evaluator.ExecuteGroup(map, "TestGroup", context);

        // Assert
        context.Errors.Count.ShouldBe(1);
        var error = context.Errors[0];

        error.ElementPath.ShouldBe("src.name.nonExistent");
        error.AvailableElements!.ShouldContain("family");
        error.AvailableElements!.ShouldContain("given");
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
        public bool HasPrimitiveValue => Value != null;

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
