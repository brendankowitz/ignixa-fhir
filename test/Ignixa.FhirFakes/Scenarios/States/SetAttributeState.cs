// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that sets or modifies an attribute in the scenario context.
/// Used to track disease progression, severity levels, and other custom state.
/// </summary>
public sealed class SetAttributeState : ScenarioState
{
    /// <summary>
    /// Gets or sets the attribute name.
    /// </summary>
    public required string AttributeName { get; init; }

    /// <summary>
    /// Gets or sets the value to set.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Gets or sets the operation to perform.
    /// </summary>
    public AttributeOperation Operation { get; init; } = AttributeOperation.Set;

    /// <summary>
    /// Gets or sets the increment/decrement amount for numeric operations.
    /// </summary>
    public int IncrementAmount { get; init; } = 1;

    /// <summary>
    /// Sets or modifies the attribute in the context.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (Operation)
        {
            case AttributeOperation.Set:
                context.SetAttribute(AttributeName, Value!);
                break;

            case AttributeOperation.Increment:
                var currentIncrement = context.GetAttribute<int>(AttributeName, 0);
                context.SetAttribute(AttributeName, currentIncrement + IncrementAmount);
                break;

            case AttributeOperation.Decrement:
                var currentDecrement = context.GetAttribute<int>(AttributeName, 0);
                context.SetAttribute(AttributeName, currentDecrement - IncrementAmount);
                break;

            default:
                throw new InvalidOperationException($"Unknown attribute operation: {Operation}");
        }
    }

    /// <summary>
    /// Creates a state that sets an attribute to a specific value.
    /// </summary>
    public static SetAttributeState Set(string name, object value) => new()
    {
        AttributeName = name,
        Value = value,
        Operation = AttributeOperation.Set
    };

    /// <summary>
    /// Creates a state that increments a numeric attribute.
    /// </summary>
    public static SetAttributeState Increment(string name, int amount = 1) => new()
    {
        AttributeName = name,
        Operation = AttributeOperation.Increment,
        IncrementAmount = amount
    };

    /// <summary>
    /// Creates a state that decrements a numeric attribute.
    /// </summary>
    public static SetAttributeState Decrement(string name, int amount = 1) => new()
    {
        AttributeName = name,
        Operation = AttributeOperation.Decrement,
        IncrementAmount = amount
    };
}
