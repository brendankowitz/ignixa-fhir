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

        private JsonArray JsonArray => _jsonArray ??= _arrayFactory();

        public T this[int index]
        {
            get
            {
                System.ArgumentOutOfRangeException.ThrowIfNegative(index);
                System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, JsonArray.Count, nameof(index));
                var node = JsonArray[index];
                if (node == null)
                {
                    return default!; // If T is a reference type, this will be null. If T is a value type, it will be its default value.
                }
                return node.GetValue<T>();
            }
            set
            {
                System.ArgumentOutOfRangeException.ThrowIfNegative(index);
                System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, JsonArray.Count, nameof(index));
                JsonArray[index] = JsonValue.Create(value);
            }
        }

        public int Count => JsonArray.Count;

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            JsonArray.Add(JsonValue.Create(item));
        }

        public void Clear()
        {
            JsonArray.Clear();
        }

        public bool Contains(T item)
        {
            foreach (var node in JsonArray)
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
            if (array.Length - arrayIndex < JsonArray.Count) System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(arrayIndex + JsonArray.Count, array.Length, nameof(arrayIndex));

            for (int i = 0; i < JsonArray.Count; i++)
            {
                var node = JsonArray[i];
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
            foreach (var node in JsonArray)
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
            for (int i = 0; i < JsonArray.Count; i++)
            {
                var node = JsonArray[i];
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
            JsonArray.Insert(index, JsonValue.Create(item));
        }

        public bool Remove(T item)
        {
            for (int i = 0; i < JsonArray.Count; i++)
            {
                var node = JsonArray[i];
                // Handle null nodes in array
                if (node == null)
                {
                    if (item == null)
                    {
                        JsonArray.RemoveAt(i);
                        return true;
                    }
                    continue;
                }
                // Handle non-null nodes
                if (item != null && node.GetValue<T>()!.Equals(item))
                {
                    JsonArray.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            JsonArray.RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    
}