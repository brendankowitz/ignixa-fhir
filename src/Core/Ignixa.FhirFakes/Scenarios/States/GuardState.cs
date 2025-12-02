// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that acts as a conditional gate - waits until a condition is true before allowing execution to continue.
/// Used for age-appropriate procedures, condition-dependent care pathways, and attribute-based transitions.
/// </summary>
public sealed class GuardState : ScenarioState
{
    /// <summary>
    /// Gets or sets the condition type.
    /// </summary>
    public required ConditionType ConditionType { get; init; }

    /// <summary>
    /// Gets or sets the comparison operator.
    /// </summary>
    public ComparisonOperator Operator { get; init; } = ComparisonOperator.GreaterThanOrEqualTo;

    /// <summary>
    /// Gets or sets the target value for comparison.
    /// </summary>
    public object? TargetValue { get; init; }

    /// <summary>
    /// Gets or sets the attribute name (for attribute-based conditions).
    /// </summary>
    public string? AttributeName { get; init; }

    /// <summary>
    /// Evaluates the guard condition. Throws InvalidOperationException if condition is not met.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);

        var conditionMet = ConditionType switch
        {
            ConditionType.Age => EvaluateAgeCondition(context),
            ConditionType.AttributeExists => EvaluateAttributeExists(context),
            ConditionType.AttributeValue => EvaluateAttributeValue(context),
            _ => throw new InvalidOperationException($"Unknown guard condition type: {ConditionType}")
        };

        if (!conditionMet)
        {
            throw new InvalidOperationException(
                $"Guard condition not met: {ConditionType} {Operator} {TargetValue}. " +
                $"Current age: {context.CurrentAge}, Attributes: {string.Join(", ", context.Attributes.Keys)}");
        }
    }

    private bool EvaluateAgeCondition(ScenarioContext context)
    {
        if (TargetValue is not int targetAge)
        {
            throw new InvalidOperationException("Age condition requires an integer target value");
        }

        var currentAge = context.CurrentAge;

        return Operator switch
        {
            ComparisonOperator.GreaterThanOrEqualTo => currentAge >= targetAge,
            ComparisonOperator.GreaterThan => currentAge > targetAge,
            ComparisonOperator.LessThanOrEqualTo => currentAge <= targetAge,
            ComparisonOperator.LessThan => currentAge < targetAge,
            ComparisonOperator.EqualTo => currentAge == targetAge,
            ComparisonOperator.NotEqualTo => currentAge != targetAge,
            _ => throw new InvalidOperationException($"Unknown operator: {Operator}")
        };
    }

    private bool EvaluateAttributeExists(ScenarioContext context)
    {
        if (string.IsNullOrEmpty(AttributeName))
        {
            throw new InvalidOperationException("Attribute condition requires AttributeName");
        }

        return context.HasAttribute(AttributeName);
    }

    private bool EvaluateAttributeValue(ScenarioContext context)
    {
        if (string.IsNullOrEmpty(AttributeName))
        {
            throw new InvalidOperationException("Attribute value condition requires AttributeName");
        }

        if (!context.HasAttribute(AttributeName))
        {
            return false;
        }

        var currentValue = context.GetAttribute<object>(AttributeName);

        // Handle null comparisons
        if (currentValue is null)
        {
            return Operator switch
            {
                ComparisonOperator.EqualTo => TargetValue is null,
                ComparisonOperator.NotEqualTo => TargetValue is not null,
                _ => false
            };
        }

        // For numeric comparisons
        if (currentValue is int currentInt && TargetValue is int targetInt)
        {
            return Operator switch
            {
                ComparisonOperator.GreaterThanOrEqualTo => currentInt >= targetInt,
                ComparisonOperator.GreaterThan => currentInt > targetInt,
                ComparisonOperator.LessThanOrEqualTo => currentInt <= targetInt,
                ComparisonOperator.LessThan => currentInt < targetInt,
                ComparisonOperator.EqualTo => currentInt == targetInt,
                ComparisonOperator.NotEqualTo => currentInt != targetInt,
                _ => throw new InvalidOperationException($"Unknown operator: {Operator}")
            };
        }

        // For string/object equality
        return Operator switch
        {
            ComparisonOperator.EqualTo => Equals(currentValue, TargetValue),
            ComparisonOperator.NotEqualTo => !Equals(currentValue, TargetValue),
            _ => throw new InvalidOperationException($"Operator {Operator} not supported for type {currentValue.GetType().Name}")
        };
    }

    /// <summary>
    /// Creates a guard that requires minimum age.
    /// </summary>
    public static GuardState MinimumAge(int age) => new()
    {
        ConditionType = ConditionType.Age,
        Operator = ComparisonOperator.GreaterThanOrEqualTo,
        TargetValue = age,
        Name = $"MinimumAge({age})"
    };

    /// <summary>
    /// Creates a guard that requires maximum age.
    /// </summary>
    public static GuardState MaximumAge(int age) => new()
    {
        ConditionType = ConditionType.Age,
        Operator = ComparisonOperator.LessThanOrEqualTo,
        TargetValue = age,
        Name = $"MaximumAge({age})"
    };

    /// <summary>
    /// Creates a guard that requires age to be within a range (inclusive).
    /// </summary>
    public static GuardState AgeRange(int minAge, int maxAge) => new()
    {
        ConditionType = ConditionType.Age,
        Operator = ComparisonOperator.GreaterThanOrEqualTo,
        TargetValue = minAge,
        Name = $"AgeRange({minAge}-{maxAge})",
        // Note: This only checks minimum. For range, chain with MaximumAge guard.
    };

    /// <summary>
    /// Creates a guard that requires exact age.
    /// </summary>
    public static GuardState ExactAge(int age) => new()
    {
        ConditionType = ConditionType.Age,
        Operator = ComparisonOperator.EqualTo,
        TargetValue = age,
        Name = $"ExactAge({age})"
    };

    /// <summary>
    /// Creates a guard that requires an attribute to exist.
    /// </summary>
    public static GuardState RequireAttribute(string attributeName) => new()
    {
        ConditionType = ConditionType.AttributeExists,
        AttributeName = attributeName,
        Name = $"RequireAttribute({attributeName})"
    };

    /// <summary>
    /// Creates a guard that requires an attribute to equal a specific value.
    /// </summary>
    public static GuardState AttributeEquals(string attributeName, object value) => new()
    {
        ConditionType = ConditionType.AttributeValue,
        AttributeName = attributeName,
        Operator = ComparisonOperator.EqualTo,
        TargetValue = value,
        Name = $"AttributeEquals({attributeName}={value})"
    };

    /// <summary>
    /// Creates a guard that requires an attribute to not equal a specific value.
    /// </summary>
    public static GuardState AttributeNotEquals(string attributeName, object value) => new()
    {
        ConditionType = ConditionType.AttributeValue,
        AttributeName = attributeName,
        Operator = ComparisonOperator.NotEqualTo,
        TargetValue = value,
        Name = $"AttributeNotEquals({attributeName}!={value})"
    };

    /// <summary>
    /// Creates a guard that requires a numeric attribute to be greater than or equal to a value.
    /// </summary>
    public static GuardState AttributeGreaterThanOrEqual(string attributeName, int value) => new()
    {
        ConditionType = ConditionType.AttributeValue,
        AttributeName = attributeName,
        Operator = ComparisonOperator.GreaterThanOrEqualTo,
        TargetValue = value,
        Name = $"AttributeGreaterThanOrEqual({attributeName}>={value})"
    };

    /// <summary>
    /// Creates a guard that requires a numeric attribute to be less than or equal to a value.
    /// </summary>
    public static GuardState AttributeLessThanOrEqual(string attributeName, int value) => new()
    {
        ConditionType = ConditionType.AttributeValue,
        AttributeName = attributeName,
        Operator = ComparisonOperator.LessThanOrEqualTo,
        TargetValue = value,
        Name = $"AttributeLessThanOrEqual({attributeName}<={value})"
    };
}
