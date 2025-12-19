# Investigation: Serialization Model Refactoring Plan

**Feature**: serialization
**Status**: Viable
**Created**: 2025-01-08

## Introduction

The current implementation of the serialization models in `Ignixa.Serialization` has some issues that make the code verbose, inefficient, and hard to maintain. The introduction of the `MutableNode` property to support PATCH operations has led to a lot of boilerplate code in the model classes, especially for list properties.

The main problems with the current implementation are:

- **Inefficiency:** Accessing a property that is a complex object or a list often results in the creation of new wrapper objects on-the-fly. In some cases, this involves serializing a `JsonNode` to a string and immediately deserializing it back into a new object, causing unnecessary performance overhead.
- **Awkward List Manipulation:** List properties are exposed as `IReadOnlyList<T>`. Modifying a list requires the developer to create a new list, populate it, and then replace the entire property, or use non-standard helper methods like `AddEntry`. This is contrary to the intuitive `.Add()`, `.Remove()` pattern developers expect.
- **Code Verbosity:** The property implementation logic is repetitive and verbose, making the model classes difficult to maintain and extend.

## Proposed Solution

To address these issues, we propose a new approach that will make the code cleaner, more efficient, and easier to work with. The proposed solution consists of two main parts:

1.  **Introduce mutable list wrappers:** We will create two new classes, `MutableJsonList<T>` and `MutablePrimitiveList<T>`, that wrap a `JsonArray` and provide a strongly-typed, mutable view over it. These classes will implement the `IList<T>` interface, allowing for intuitive list manipulation using methods like `.Add()`, `.Remove()`, and indexers.
2.  **Introduce helper methods in `BaseJsonNode`:** We will add a set of helper methods to the `BaseJsonNode` class to encapsulate the repetitive logic for property access. These methods will provide a clean and consistent way to get and set primitive, complex, and list properties.

## Step-by-step Guide

Here is a step-by-step guide on how to refactor a model class using the new helpers. We will use `BundleJsonNode` as an example.

### 1. Create `MutableJsonList<T>` and `MutablePrimitiveList<T>`

First, we need to create the `MutableJsonList<T>` and `MutablePrimitiveList<T>` classes in the `Ignixa.Serialization` project. These classes will be used to wrap `JsonArray` properties and provide a mutable, strongly-typed API.

**`MutableJsonList<T>`:**

```csharp
public class MutableJsonList<T> : IList<T> where T : BaseJsonNode
{
    // ... implementation ...
}
```

**`MutablePrimitiveList<T>`:**

```csharp
public class MutablePrimitiveList<T> : IList<T>
{
    // ... implementation ...
}
```

### 2. Add helper methods to `BaseJsonNode`

Next, we will add the following helper methods to the `BaseJsonNode` class:

```csharp
protected T? GetProperty<T>(string name);
protected void SetProperty<T>(string name, T? value);
protected T? GetComplexProperty<T>(string name) where T : BaseJsonNode;
protected MutableJsonList<T> GetListProperty<T>(string name) where T : BaseJsonNode;
protected MutablePrimitiveList<T> GetPrimitiveListProperty<T>(string name);
```

### 3. Refactor `BundleJsonNode`

Now we can refactor the `BundleJsonNode` class to use the new helpers.

**Before:**

```csharp
public class BundleJsonNode : ResourceJsonNode
{
    // ...

    [JsonIgnore]
    public int? Total
    {
        get => MutableNode["total"]?.GetValue<int>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("total");
            }
            else
            {
                MutableNode["total"] = value.Value;
            }
        }
    }

    [JsonIgnore]
    public IReadOnlyList<BundleLinkJsonNode> Link
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("link", out var linkNode) || linkNode is not JsonArray array)
            {
                return null;
            }

            var list = new List<BundleLinkJsonNode>();
            foreach (var item in array)
            {
                if (item != null)
                {
                    var json = item.ToJsonString();
                    list.Add(JsonSerializer.Deserialize<BundleLinkJsonNode>(json));
                }
            }

            return list;
        }
        set
        {
            // ...
        }
    }

    // ...
}
```

**After:**

```csharp
public class BundleJsonNode : ResourceJsonNode
{
    // ...

    [JsonIgnore]
    public int? Total
    {
        get => GetProperty<int?>("total");
        set => SetProperty("total", value);
    }

    [JsonIgnore]
    public MutableJsonList<BundleLinkJsonNode> Link => GetListProperty<BundleLinkJsonNode>("link");

    // ...
}
```

As you can see, the code is much cleaner and easier to read. The boilerplate code for getting and setting properties is gone, and the list properties are now exposed as mutable lists, which is more intuitive for developers.

## Benefits

The new approach has several benefits:

- **Cleaner code:** The model classes are much cleaner and easier to read, as the boilerplate code for property access is encapsulated in the `BaseJsonNode` class.
- **Better performance:** The new implementation avoids unnecessary serialization and deserialization of JSON, which improves performance.
- **Improved developer experience:** The new API is more intuitive and easier to work with, especially for list properties.

By following this plan, we can significantly improve the quality of the serialization models and make them easier to maintain and extend in the future.
