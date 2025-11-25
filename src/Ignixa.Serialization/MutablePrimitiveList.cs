using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ignixa.Serialization;
    public class MutablePrimitiveList<T> : IList<T>
    {
        private readonly JsonArray _jsonArray;

        public MutablePrimitiveList(JsonArray jsonArray)
        {
            _jsonArray = jsonArray ?? new JsonArray();
        }

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _jsonArray.Count)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(index));
                }
                var node = _jsonArray[index];
                if (node == null)
                {
                    return default!; // If T is a reference type, this will be null. If T is a value type, it will be its default value.
                }
                return node.GetValue<T>();
            }
            set
            {
                if (index < 0 || index >= _jsonArray.Count)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(index));
                }
                _jsonArray[index] = JsonValue.Create(value);
            }
        }

        public int Count => _jsonArray.Count;

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            _jsonArray.Add(JsonValue.Create(item));
        }

        public void Clear()
        {
            _jsonArray.Clear();
        }

        public bool Contains(T item)
        {
            foreach (var node in _jsonArray)
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
            if (array.Length - arrayIndex < _jsonArray.Count) System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(arrayIndex + _jsonArray.Count, array.Length, nameof(arrayIndex));

            for (int i = 0; i < _jsonArray.Count; i++)
            {
                var node = _jsonArray[i];
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
            foreach (var node in _jsonArray)
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
            for (int i = 0; i < _jsonArray.Count; i++)
            {
                var node = _jsonArray[i];
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
            _jsonArray.Insert(index, JsonValue.Create(item));
        }

        public bool Remove(T item)
        {
            for (int i = 0; i < _jsonArray.Count; i++)
            {
                var node = _jsonArray[i];
                // Handle null nodes in array
                if (node == null)
                {
                    if (item == null)
                    {
                        _jsonArray.RemoveAt(i);
                        return true;
                    }
                    continue;
                }
                // Handle non-null nodes
                if (item != null && node.GetValue<T>()!.Equals(item))
                {
                    _jsonArray.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            _jsonArray.RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    
}