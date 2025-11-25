using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes; // Added this in previous step

namespace Ignixa.Serialization;
    public class MutableJsonList<T> : IList<T> where T : BaseJsonNode
    {
        private readonly JsonArray _jsonArray;

        public MutableJsonList(JsonArray jsonArray)
        {
            _jsonArray = jsonArray ?? new JsonArray();
        }

        public T this[int index]
        {
            get
            {
                System.ArgumentOutOfRangeException.ThrowIfNegative(index);
                System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _jsonArray.Count, nameof(index));
                var node = _jsonArray[index];
                if (node == null)
                {
                    return default!;
                }
                return (T)System.Activator.CreateInstance(typeof(T), node, null)!;
            }
            set
            {
                System.ArgumentOutOfRangeException.ThrowIfNegative(index);
                System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _jsonArray.Count, nameof(index));
                _jsonArray[index] = value?.MutableNode;
            }
        }

        public int Count => _jsonArray.Count;

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            _jsonArray.Add(item?.MutableNode);
        }

        public void Clear()
        {
            _jsonArray.Clear();
        }

        public bool Contains(T item)
        {
            if (item == null)
            {
                foreach (var node in _jsonArray)
                {
                    if (node == null) return true;
                }
                return false;
            }
            foreach (var node in _jsonArray)
            {
                if (node == null) continue; // Skip null entries if item is not null
                // This might need a more robust comparison for complex objects
                if (node.ToJsonString() == item.MutableNode.ToJsonString())
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
                    array[arrayIndex + i] = default!; // Use default!
                }
                else
                {
                    array[arrayIndex + i] = (T)System.Activator.CreateInstance(typeof(T), node, null)!;
                }
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var node in _jsonArray)
            {
                if (node == null)
                {
                    yield return default!; // Use default!
                }
                else
                {
                    yield return (T)System.Activator.CreateInstance(typeof(T), node, null)!;
                }
            }
        }

        public int IndexOf(T item)
        {
            if (item == null)
            {
                for (int i = 0; i < _jsonArray.Count; i++)
                {
                    if (_jsonArray[i] == null) return i;
                }
                return -1;
            }
            for (int i = 0; i < _jsonArray.Count; i++)
            {
                var node = _jsonArray[i];
                if (node == null) continue; // Skip null entries if item is not null
                if (node.ToJsonString() == item.MutableNode.ToJsonString())
                {
                    return i;
                }
            }
            return -1;
        }

        public void Insert(int index, T item)
        {
            _jsonArray.Insert(index, item?.MutableNode);
        }

        public bool Remove(T item)
        {
            if (item == null)
            {
                for (int i = 0; i < _jsonArray.Count; i++)
                {
                    if (_jsonArray[i] == null)
                    {
                        _jsonArray.RemoveAt(i);
                        return true;
                    }
                }
                return false;
            }
            for (int i = 0; i < _jsonArray.Count; i++)
            {
                var node = _jsonArray[i];
                if (node == null) continue; // Skip null entries if item is not null
                if (node.ToJsonString() == item.MutableNode.ToJsonString())
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