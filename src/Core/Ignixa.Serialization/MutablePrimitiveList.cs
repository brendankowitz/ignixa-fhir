using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ignixa.Serialization;
    public class MutablePrimitiveList<T> : IList<T>
    {
        private readonly Func<JsonArray> _arrayFactory;
        private JsonArray? _jsonArray;

        public MutablePrimitiveList(Func<JsonArray> arrayFactory, JsonArray? existingArray)
        {
            _arrayFactory = arrayFactory;
            _jsonArray = existingArray;
        }

        // Reads must never resolve the factory: doing so injects an empty array into the document,
        // which is invalid FHIR and mutates a resource nobody edited. Only mutating members vivify.
        private JsonArray? ReadArray => _jsonArray;

        private JsonArray MutableArray => _jsonArray ??= _arrayFactory();

        private int ReadCount => _jsonArray?.Count ?? 0;

        public T this[int index]
        {
            get
            {
                System.ArgumentOutOfRangeException.ThrowIfNegative(index);
                System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ReadCount, nameof(index));
                var node = ReadArray![index];
                if (node == null)
                {
                    return default!; // If T is a reference type, this will be null. If T is a value type, it will be its default value.
                }
                return node.GetValue<T>();
            }
            set
            {
                System.ArgumentOutOfRangeException.ThrowIfNegative(index);
                System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ReadCount, nameof(index));
                MutableArray[index] = JsonValue.Create(value);
            }
        }

        public int Count => ReadCount;

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            MutableArray.Add(JsonValue.Create(item));
        }

        public void Clear()
        {
            _jsonArray?.Clear();
        }

        public bool Contains(T item)
        {
            foreach (var node in ReadArray ?? [])
            {
                // Handle null nodes in array
                if (node == null)
                {
                    if (item == null) return true;
                    continue;
                }
                // Handle non-null nodes
                if (item != null && node.GetValue<T>()!.Equals(item)) // Add null-forgiving operator for GetValue<T>() as we checked node != null
                {
                    return true;
                }
            }
            return false;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            System.ArgumentNullException.ThrowIfNull(array);
            System.ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            if (array.Length - arrayIndex < ReadCount) System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(arrayIndex + ReadCount, array.Length, nameof(arrayIndex));

            for (int i = 0; i < ReadCount; i++)
            {
                var node = ReadArray![i];
                if (node == null)
                {
                    array[arrayIndex + i] = default!; // Assign default if node is null
                }
                else
                {
                    array[arrayIndex + i] = node.GetValue<T>();
                }
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var node in ReadArray ?? [])
            {
                if (node == null)
                {
                    yield return default!; // Yield default if node is null
                }
                else
                {
                    yield return node.GetValue<T>();
                }
            }
        }

        public int IndexOf(T item)
        {
            for (int i = 0; i < ReadCount; i++)
            {
                var node = ReadArray![i];
                // Handle null nodes in array
                if (node == null)
                {
                    if (item == null) return i;
                    continue;
                }
                // Handle non-null nodes
                if (item != null && node.GetValue<T>()!.Equals(item))
                {
                    return i;
                }
            }
            return -1;
        }

        public void Insert(int index, T item)
        {
            MutableArray.Insert(index, JsonValue.Create(item));
        }

        public bool Remove(T item)
        {
            for (int i = 0; i < ReadCount; i++)
            {
                var node = ReadArray![i];
                // Handle null nodes in array
                if (node == null)
                {
                    if (item == null)
                    {
                        ReadArray!.RemoveAt(i);
                        return true;
                    }
                    continue;
                }
                // Handle non-null nodes
                if (item != null && node.GetValue<T>()!.Equals(item))
                {
                    ReadArray!.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            MutableArray.RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    
}