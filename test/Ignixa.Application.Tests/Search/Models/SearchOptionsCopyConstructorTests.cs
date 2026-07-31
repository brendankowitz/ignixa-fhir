// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Models;

/// <summary>
/// Covers <see cref="SearchOptions(SearchOptions)"/>. The completeness test is the important one: a copy
/// constructor that silently drops a property added later is worse than no copy constructor at all,
/// because the loss is invisible at every call site.
/// </summary>
public class SearchOptionsCopyConstructorTests
{
    [Fact]
    public void GivenAFullyPopulatedInstance_WhenCopied_ThenEveryPublicSettablePropertyIsCarriedOver()
    {
        // Arrange: give every property a value distinguishable from its default, discovered by
        // reflection so a newly added property of an already-handled type is covered without editing
        // this test. Anything else fails here rather than passing silently.
        var source = new SearchOptions();
        var defaults = new SearchOptions();
        PropertyInfo[] properties = SettableProperties();
        properties.ShouldNotBeEmpty();

        foreach ((PropertyInfo property, int ordinal) in properties.Select((p, i) => (p, i)))
        {
            object distinct = DistinctValueFor(property, ordinal);

            // Without this, a property whose distinct value happened to equal its default would be
            // reported as carried over even when the constructor never assigns it.
            Equals(distinct, property.GetValue(defaults)).ShouldBeFalse(
                $"DistinctValueFor produced {property.Name}'s default value, so a dropped assignment " +
                "would go undetected. Extend DistinctValueFor to cover this property's type.");

            property.SetValue(source, distinct);
        }

        // Act
        var copy = new SearchOptions(source);

        // Assert: any property the constructor forgot still holds its default, which will not equal the
        // distinct value assigned above.
        var dropped = properties
            .Where(p => !Equals(p.GetValue(copy), p.GetValue(source)))
            .Select(p => p.Name)
            .ToList();

        dropped.ShouldBeEmpty("Add the missing assignment(s) in SearchOptions(SearchOptions).");
    }

    [Fact]
    public void GivenACopy_WhenTheCopyIsMutated_ThenTheSourceIsUnchanged()
    {
        // Arrange: the whole point of the constructor is to vary a request without disturbing the
        // instance the caller still holds.
        var source = new SearchOptions
        {
            MaxItemCount = 10,
            ResourceType = "Patient",
            Include = new[] { WildcardInclude() },
        };

        // Act
        var copy = new SearchOptions(source)
        {
            MaxItemCount = 500,
            ResourceType = "Observation",
            Include = Array.Empty<IncludeExpression>(),
        };

        // Assert
        source.MaxItemCount.ShouldBe(10);
        source.ResourceType.ShouldBe("Patient");
        source.Include.Count.ShouldBe(1);

        copy.MaxItemCount.ShouldBe(500);
        copy.ResourceType.ShouldBe("Observation");
        copy.Include.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNull_WhenCopied_ThenArgumentNullExceptionIsThrown()
    {
        // Arrange, Act, Assert
        Should.Throw<ArgumentNullException>(() => new SearchOptions(null!));
    }

    private static readonly string[] WildcardIncludeResourceTypes = ["Patient"];

    private static IncludeExpression WildcardInclude()
        => new(
            WildcardIncludeResourceTypes,
            referenceSearchParameter: null,
            sourceResourceType: "Patient",
            targetResourceType: null,
            referencedTypes: null,
            wildCard: true,
            reversed: false,
            iterate: false);

    private static PropertyInfo[] SettableProperties()
        => typeof(SearchOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();

    /// <summary>
    /// Produces a value for <paramref name="property"/> that differs both from the property's default and
    /// from every other property's value, so that a dropped assignment shows up as a mismatch and a
    /// transposed one (<c>StartSurrogateId = other.EndSurrogateId</c>) does not slip through two properties
    /// that share a type.
    /// </summary>
    private static object DistinctValueFor(PropertyInfo property, int ordinal)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(string))
        {
            return property.Name + "-value";
        }

        if (type == typeof(int))
        {
            return 4242 + ordinal;
        }

        if (type == typeof(long))
        {
            return 424242L + ordinal;
        }

        if (type.IsEnum)
        {
            // The highest-valued member, so an enum whose default is its first member still differs. The
            // guard in the caller catches the case where the default is the highest-valued member.
            Array values = Enum.GetValues(type);
            return values.GetValue(values.Length - 1)!;
        }

        if (type == typeof(Expression))
        {
            return new StringExpression(StringOperator.Equals, FieldName.String, componentIndex: null, "copy-ctor", ignoreCase: false);
        }

        // The strategy below assumes every remaining property is a collection; a fresh empty instance is a
        // different reference from the default, which is what reference equality compares.
        Type concrete = ConcreteCollectionFor(type)
            ?? throw new InvalidOperationException(
                $"No distinct value strategy for {property.Name} ({type}). Add a case to DistinctValueFor.");

        return Activator.CreateInstance(concrete)!;
    }

    /// <summary>
    /// The instantiable collection type to stand in for <paramref name="type"/>, or null when there is no
    /// strategy — returning null rather than letting <see cref="Activator"/> throw keeps the failure
    /// actionable for whoever adds the next property.
    /// </summary>
    private static Type ConcreteCollectionFor(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.IsInterface || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null ? null : type;
        }

        Type definition = type.GetGenericTypeDefinition();
        Type[] arguments = type.GetGenericArguments();

        if (definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IEnumerable<>))
        {
            return typeof(List<>).MakeGenericType(arguments);
        }

        if (definition == typeof(IReadOnlySet<>) || definition == typeof(ISet<>))
        {
            return typeof(HashSet<>).MakeGenericType(arguments);
        }

        return null;
    }
}
