// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;

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
        // reflection so a newly added property is populated here without this test being edited.
        var source = new SearchOptions();
        PropertyInfo[] properties = SettableProperties();
        Assert.NotEmpty(properties);

        foreach (PropertyInfo property in properties)
        {
            property.SetValue(source, DistinctValueFor(property));
        }

        // Act
        var copy = new SearchOptions(source);

        // Assert: any property the constructor forgot still holds its default, which will not equal the
        // distinct value assigned above.
        var dropped = properties
            .Where(p => !Equals(p.GetValue(copy), p.GetValue(source)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            dropped.Count == 0,
            $"SearchOptions' copy constructor did not carry over: {string.Join(", ", dropped)}. " +
            "Add the assignment in SearchOptions(SearchOptions).");
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
        Assert.Equal(10, source.MaxItemCount);
        Assert.Equal("Patient", source.ResourceType);
        Assert.Single(source.Include);

        Assert.Equal(500, copy.MaxItemCount);
        Assert.Equal("Observation", copy.ResourceType);
        Assert.Empty(copy.Include);
    }

    [Fact]
    public void GivenNull_WhenCopied_ThenArgumentNullExceptionIsThrown()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => new SearchOptions(null!));
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
    /// Produces a value for <paramref name="property"/> that differs from the property's default, so a
    /// dropped assignment shows up as a mismatch rather than coincidentally matching.
    /// </summary>
    private static object DistinctValueFor(PropertyInfo property)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(string))
        {
            return property.Name + "-value";
        }

        if (type == typeof(int))
        {
            return 4242;
        }

        if (type == typeof(long))
        {
            return 424242L;
        }

        if (type.IsEnum)
        {
            // The last declared member, so an enum whose default is its first member still differs.
            Array values = Enum.GetValues(type);
            return values.GetValue(values.Length - 1)!;
        }

        if (type == typeof(Expression))
        {
            return new StringExpression(StringOperator.Equals, FieldName.String, componentIndex: null, "copy-ctor", ignoreCase: false);
        }

        // Every remaining property is a collection; a fresh empty instance is a different reference from
        // the default, which is what reference equality compares.
        return Activator.CreateInstance(ConcreteCollectionFor(type))
            ?? throw new InvalidOperationException($"No distinct value strategy for {property.Name} ({type}).");
    }

    private static Type ConcreteCollectionFor(Type type)
    {
        if (!type.IsGenericType)
        {
            return type;
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

        return type;
    }
}
