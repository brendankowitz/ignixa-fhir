// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Visitors;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Visitors;

public class VisitorPatternTests
{
    private readonly FhirPathParser _parser = new();

    [Fact]
    public void GivenCustomVisitor_WhenTraversingExpression_ThenVisitsAllNodes()
    {
        // Arrange
        var expr = _parser.Parse("Patient.name.given.first()");
        var visitor = new CountingVisitor();

        // Act
        expr.AcceptVisitor(visitor, new FhirPathVisitorContext());

        // Assert
        Assert.True(visitor.PropertyAccessCount > 0);
        Assert.Equal(1, visitor.FunctionCallCount);
    }

    [Fact]
    public void GivenNestedExpression_WhenTraversing_ThenVisitsAllLevels()
    {
        // Arrange
        var expr = _parser.Parse("Patient.name.where(use = 'official').given.first()");
        var visitor = new CountingVisitor();

        // Act
        expr.AcceptVisitor(visitor, new FhirPathVisitorContext());

        // Assert - verify visitor traversed the expression tree
        Assert.True(visitor.FunctionCallCount >= 2, $"Expected at least 2 function calls, got {visitor.FunctionCallCount}"); // where() and first()
        Assert.True(visitor.BinaryExpressionCount + visitor.PropertyAccessCount + visitor.FunctionCallCount > 0);
    }

    [Fact]
    public void GivenFhirPathType_WhenTrackingCollections_ThenMarksCorrectly()
    {
        // Arrange
        var singleNode = new FhirPathType("string", isCollection: false);
        var collectionNode = new FhirPathType("string", isCollection: true);

        // Assert
        Assert.False(singleNode.IsCollection);
        Assert.True(collectionNode.IsCollection);

        Assert.Equal("string", singleNode.ToString());
        Assert.Equal("string[]", collectionNode.ToString());
    }

    [Fact]
    public void GivenFhirPathType_WhenConvertingToCollection_ThenMarksAsCollection()
    {
        // Arrange
        var singleNode = new FhirPathType("Patient", isCollection: false);

        // Act
        var collectionNode = singleNode.AsCollection();

        // Assert
        Assert.True(collectionNode.IsCollection);
        Assert.Equal("Patient", collectionNode.TypeName);
    }

    [Fact]
    public void GivenFhirPathType_WhenConvertingToSingle_ThenMarksAsSingle()
    {
        // Arrange
        var collectionNode = new FhirPathType("Patient", isCollection: true);

        // Act
        var singleNode = collectionNode.AsSingle();

        // Assert
        Assert.False(singleNode.IsCollection);
        Assert.Equal("Patient", singleNode.TypeName);
    }

    [Fact]
    public void GivenFhirPathType_WhenUpdatingPath_ThenPreservesOtherProperties()
    {
        // Arrange
        var node = new FhirPathType("Patient", isCollection: true, path: "Patient");

        // Act
        var updatedNode = node.WithPath("Patient.name");

        // Assert
        Assert.Equal("Patient.name", updatedNode.Path);
        Assert.Equal("Patient", updatedNode.TypeName);
        Assert.True(updatedNode.IsCollection);
    }

    [Fact]
    public void GivenFhirPathType_WhenCheckingPrimitiveTypes_ThenIdentifiesCorrectly()
    {
        // Arrange & Assert
        Assert.True(new FhirPathType("string").IsPrimitive);
        Assert.True(new FhirPathType("integer").IsPrimitive);
        Assert.True(new FhirPathType("boolean").IsPrimitive);
        Assert.True(new FhirPathType("decimal").IsPrimitive);
        Assert.True(new FhirPathType("dateTime").IsPrimitive);
        Assert.True(new FhirPathType("code").IsPrimitive);

        Assert.False(new FhirPathType("Patient").IsPrimitive);
        Assert.False(new FhirPathType("HumanName").IsPrimitive);
    }

    [Fact]
    public void GivenFhirPathTypeSet_WhenCombiningTypes_ThenMergesCorrectly()
    {
        // Arrange
        var props1 = new FhirPathTypeSet();
        props1.AddPrimitiveType("string");

        var props2 = new FhirPathTypeSet();
        props2.AddPrimitiveType("integer");

        // Act
        props1.CopyFrom(props2);

        // Assert
        Assert.Equal(2, props1.Types.Count);
        Assert.True(props1.CanBeOfType("string"));
        Assert.True(props1.CanBeOfType("integer"));
    }

    [Fact]
    public void GivenFhirPathTypeSet_WhenCheckingCollection_ThenReturnsCorrectly()
    {
        // Arrange
        var singleProps = new FhirPathTypeSet();
        singleProps.AddPrimitiveType("string", forceCollection: false);

        var collectionProps = new FhirPathTypeSet();
        collectionProps.AddPrimitiveType("string", forceCollection: true);

        // Assert
        Assert.False(singleProps.IsCollection());
        Assert.True(collectionProps.IsCollection());
    }

    [Fact]
    public void GivenFhirPathTypeSet_WhenConvertingToSingle_ThenAllTypesBecomeSingle()
    {
        // Arrange
        var props = new FhirPathTypeSet();
        props.AddPrimitiveType("string", forceCollection: true);
        props.AddPrimitiveType("integer", forceCollection: true);

        // Act
        var singleProps = props.AsSingle();

        // Assert
        Assert.All(singleProps.Types, t => Assert.False(t.IsCollection));
    }

    [Fact]
    public void GivenFhirPathTypeSet_WhenConvertingToCollection_ThenAllTypesBecomeCollections()
    {
        // Arrange
        var props = new FhirPathTypeSet();
        props.AddPrimitiveType("string", forceCollection: false);
        props.AddPrimitiveType("integer", forceCollection: false);

        // Act
        var collectionProps = props.AsCollection();

        // Assert
        Assert.All(collectionProps.Types, t => Assert.True(t.IsCollection));
    }

    [Fact]
    public void GivenFhirPathTypeSet_WhenCheckingTypeCompatibility_ThenValidatesCorrectly()
    {
        // Arrange
        var props = new FhirPathTypeSet();
        props.AddPrimitiveType("string");
        props.AddPrimitiveType("integer");

        // Assert
        Assert.True(props.CanBeOfType("string"));
        Assert.True(props.CanBeOfType("integer"));
        Assert.False(props.CanBeOfType("boolean"));
    }

    [Fact]
    public void GivenFhirPathTypeSet_WhenGettingTypeNames_ThenReturnsCommaSeparated()
    {
        // Arrange
        var props = new FhirPathTypeSet();
        props.AddPrimitiveType("string");
        props.AddPrimitiveType("integer");

        // Act
        var typeNames = props.TypeNames();

        // Assert
        Assert.Contains("string", typeNames, StringComparison.Ordinal);
        Assert.Contains("integer", typeNames, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenFhirPathTypeEquality_WhenComparingIdentical_ThenReturnsTrue()
    {
        // Arrange
        var node1 = new FhirPathType("Patient", isCollection: true);
        var node2 = new FhirPathType("Patient", isCollection: true);

        // Assert
        Assert.True(node1.Equals(node2));
        Assert.True(node1 == node2);
        Assert.Equal(node1.GetHashCode(), node2.GetHashCode());
    }

    [Fact]
    public void GivenFhirPathTypeEquality_WhenComparingDifferent_ThenReturnsFalse()
    {
        // Arrange
        var node1 = new FhirPathType("Patient", isCollection: true);
        var node2 = new FhirPathType("Patient", isCollection: false);
        var node3 = new FhirPathType("Observation", isCollection: true);

        // Assert
        Assert.False(node1.Equals(node2));
        Assert.False(node1.Equals(node3));
        Assert.True(node1 != node2);
    }

    [Fact]
    public void GivenValidationIssue_WhenCreated_ThenContainsExpectedData()
    {
        // Arrange & Act
        var issue = new ValidationIssue
        {
            Severity = ValidationIssueSeverity.Error,
            Message = "Test error",
            Location = "Patient.name",
            Expression = "name.given"
        };

        // Assert
        Assert.Equal(ValidationIssueSeverity.Error, issue.Severity);
        Assert.Equal("Test error", issue.Message);
        Assert.Equal("Patient.name", issue.Location);
        Assert.Equal("name.given", issue.Expression);
    }

    [Fact]
    public void GivenFhirPathVisitorContext_WhenCreated_ThenInitializesCorrectly()
    {
        // Arrange & Act
        var context = new FhirPathVisitorContext();

        // Assert
        Assert.NotNull(context);
        Assert.NotNull(context.Variables);
        Assert.Empty(context.Variables);
    }

    [Fact]
    public void GivenComplexExpression_WhenVisiting_ThenTraversesAllParts()
    {
        // Arrange
        var expr = _parser.Parse("Patient.name.where(use = 'official').given.first() | Patient.name.family");
        var visitor = new CountingVisitor();

        // Act
        expr.AcceptVisitor(visitor, new FhirPathVisitorContext());

        // Assert - verify visitor traversed the complete expression tree including union
        var totalVisits = visitor.PropertyAccessCount + visitor.FunctionCallCount + visitor.BinaryExpressionCount;
        Assert.True(totalVisits > 5, $"Expected more than 5 total visits, got {totalVisits}");
        Assert.True(visitor.FunctionCallCount >= 2, $"Expected at least 2 function calls, got {visitor.FunctionCallCount}");
        Assert.True(visitor.BinaryExpressionCount >= 2, $"Expected at least 2 binary expressions (union + equals), got {visitor.BinaryExpressionCount}");
    }

    // Helper visitor that counts node types
    private class CountingVisitor : DefaultFhirPathExpressionVisitor<FhirPathVisitorContext, FhirPathTypeSet>
    {
        public int PropertyAccessCount { get; private set; }
        public int FunctionCallCount { get; private set; }
        public int BinaryExpressionCount { get; private set; }

        public override FhirPathTypeSet VisitPropertyAccess(PropertyAccessExpression expression, FhirPathVisitorContext context)
        {
            PropertyAccessCount++;
            return base.VisitPropertyAccess(expression, context);
        }

        public override FhirPathTypeSet VisitFunctionCall(FunctionCallExpression expression, FhirPathVisitorContext context)
        {
            FunctionCallCount++;
            return base.VisitFunctionCall(expression, context);
        }

        public override FhirPathTypeSet VisitBinary(BinaryExpression expression, FhirPathVisitorContext context)
        {
            BinaryExpressionCount++;
            return base.VisitBinary(expression, context);
        }
    }
}
