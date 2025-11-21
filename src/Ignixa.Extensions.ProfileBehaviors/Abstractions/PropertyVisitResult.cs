// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Extensions.ProfileBehaviors.Abstractions;

/// <summary>
/// Result of visiting a property: what action should the visitor take?
/// </summary>
public sealed class PropertyVisitResult
{
    /// <summary>
    /// The action type.
    /// </summary>
    public PropertyAction Action { get; }

    /// <summary>
    /// For Mutate action: function to transform the property value.
    /// </summary>
    public Func<System.Text.Json.Nodes.JsonNode?, System.Text.Json.Nodes.JsonNode?>? MutationFunc { get; }

    /// <summary>
    /// For Inject action: function to create the missing property value.
    /// </summary>
    public Func<System.Text.Json.Nodes.JsonNode>? InjectionFunc { get; }

    private PropertyVisitResult(
        PropertyAction action,
        Func<System.Text.Json.Nodes.JsonNode?, System.Text.Json.Nodes.JsonNode?>? mutationFunc = null,
        Func<System.Text.Json.Nodes.JsonNode>? injectionFunc = null)
    {
        Action = action;
        MutationFunc = mutationFunc;
        InjectionFunc = injectionFunc;
    }

    /// <summary>
    /// Include the property as-is (pass through unchanged).
    /// </summary>
    public static PropertyVisitResult Include() => new(PropertyAction.Include);

    /// <summary>
    /// Skip the property (do not include in output).
    /// </summary>
    public static PropertyVisitResult Skip() => new(PropertyAction.Skip);

    /// <summary>
    /// Mutate the property value using the provided function.
    /// </summary>
    /// <param name="mutationFunc">Function to transform the property value.</param>
    public static PropertyVisitResult Mutate(Func<System.Text.Json.Nodes.JsonNode?, System.Text.Json.Nodes.JsonNode?> mutationFunc)
        => new(PropertyAction.Mutate, mutationFunc: mutationFunc);

    /// <summary>
    /// Inject a new property (for missing mandatory elements).
    /// </summary>
    /// <param name="injectionFunc">Function to create the property value.</param>
    public static PropertyVisitResult Inject(Func<System.Text.Json.Nodes.JsonNode> injectionFunc)
        => new(PropertyAction.Inject, injectionFunc: injectionFunc);
}
