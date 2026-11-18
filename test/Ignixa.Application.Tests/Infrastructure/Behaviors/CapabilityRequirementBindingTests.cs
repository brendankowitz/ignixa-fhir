// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Compartment;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure.Behaviors;

/// <summary>
/// An expression that references <c>%resourceType</c> is only safe, and only evaluable, if the request also
/// supplies the value. A request that forgot would be evaluated with the variable unbound.
/// </summary>
public class CapabilityRequirementBindingTests
{
    private const string Type = "Observation";

    private static readonly SearchOptions Options = new() { ResourceType = Type };

    // One instance per implementer. A new IRequireCapability implementer fails the census test below until
    // it is added here, so its binding gets checked.
    private static readonly Dictionary<Type, IRequireCapability> Samples = new()
    {
        [typeof(GetResourceQuery)] = new GetResourceQuery(Type, "1"),
        [typeof(DeleteResourceCommand)] = new DeleteResourceCommand(Type, "1"),
        [typeof(IncludesResourceQuery)] = new IncludesResourceQuery(Type, Options),
        [typeof(SearchResourcesQuery)] = new SearchResourcesQuery(Type, Options),
        [typeof(SearchCompartmentQuery)] = new SearchCompartmentQuery("Patient", "1", Type, Options),
        [typeof(CreateOrUpdateResourceCommand)] = new CreateOrUpdateResourceCommand(
            Type, "1", JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Observation","id":"1"}"""), HttpMethod.Put),
    };

    [Fact]
    public void GivenTheApplicationAssembly_WhenListingCapabilityImplementers_ThenEachHasASample()
    {
        var implementers = typeof(GetResourceQuery).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IRequireCapability).IsAssignableFrom(type))
            .ToList();

        implementers.ShouldBe(Samples.Keys, ignoreOrder: true,
            "Add a sample for the new IRequireCapability implementer so its %resourceType binding is checked.");
    }

    [Fact]
    public void GivenAnExpressionReferencingTheResourceTypeVariable_WhenBinding_ThenTheRequestSuppliesIt()
    {
        foreach (var (type, request) in Samples)
        {
            var expression = request.GetCapabilityRequirementExpression();

            expression.ShouldContain("%resourceType", customMessage: type.Name);
            request.CapabilityResourceType.ShouldBe(Type, type.Name);
        }
    }
}
