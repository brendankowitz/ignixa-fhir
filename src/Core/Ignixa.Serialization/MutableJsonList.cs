using System.Collections;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization;
    public class MutableJsonList<T>(Func<JsonArray> arrayFactory, JsonArray? existingArray, FhirVersion? fhirVersion = null) : IList<T>
        where T : BaseJsonNode
    {
        private JsonArray? _jsonArray = existingArray;
        private readonly FhirVersion? _fhirVersion = fhirVersion;

        private static readonly Func<JsonNode, FhirVersion?, T> _factory = CreateFactory();

        private static Func<JsonNode, FhirVersion?, T> CreateFactory()
        {
            var ctor = typeof(T).GetConstructor(new[] { typeof(JsonObject), typeof(FhirVersion) });
            if (ctor == null)
            {
                throw new InvalidOperationException(
                    $"Type {typeof(T).Name} must have a constructor (JsonObject, FhirVersion?)");
            }
            return (node, fhirVersion) => (T)ctor.Invoke([node, fhirVersion]);
        }

        private JsonArray JsonArray => _jsonArray ??= arrayFactory();

        public T this[int index]
        {
            get
            {
                System.ArgumentOutOfRangeException.ThrowIfNegative(index);
                System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, JsonArray.Count, nameof(index));
                var node = JsonArray[index];
                if (node == null)
                {
                    return default!;
                }
                return _factory(node, _fhirVersion);
            }
            set
            {
                System.ArgumentOutOfRangeException.ThrowIfNegative(index);
                System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, JsonArray.Count, nameof(index));
                JsonArray[index] = value?.MutableNode;
            }
        }

        public int Count => JsonArray.Count;

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            JsonArray.Add(item?.MutableNode);
        }

        public void Clear()
        {
            JsonArray.Clear();
        }

        public bool Contains(T item)
        {
            if (item == null)
            {
                foreach (var node in JsonArray)
                {
                    if (node == null) return true;
                }
                return false;
            }
            foreach (var node in JsonArray)
            {
                if (node == null) continue; // Skip null entries if item is not null
                if (JsonNode.DeepEquals(node, item.MutableNode))
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
                    array[arrayIndex + i] = default!; // Use default!
                }
                else
                {
                    array[arrayIndex + i] = _factory(node, _fhirVersion);
                }
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var node in JsonArray)
            {
                if (node == null)
                {
                    yield return default!; // Use default!
                }
                else
                {
                    yield return _factory(node, _fhirVersion);
                }
            }
        }

        public int IndexOf(T item)
        {
            if (item == null)
            {
                for (int i = 0; i < JsonArray.Count; i++)
                {
                    if (JsonArray[i] == null) return i;
                }
                return -1;
            }
            for (int i = 0; i < JsonArray.Count; i++)
            {
                var node = JsonArray[i];
                if (node == null) continue; // Skip null entries if item is not null
                if (JsonNode.DeepEquals(node, item.MutableNode))
                {
                    return i;
                }
            }
            return -1;
        }

        public void Insert(int index, T item)
        {
            JsonArray.Insert(index, item?.MutableNode);
        }

        public bool Remove(T item)
        {
            if (item == null)
            {
                for (int i = 0; i < JsonArray.Count; i++)
                {
                    if (JsonArray[i] == null)
                    {
                        JsonArray.RemoveAt(i);
                        return true;
                    }
                }
                return false;
            }
            for (int i = 0; i < JsonArray.Count; i++)
            {
                var node = JsonArray[i];
                if (node == null) continue; // Skip null entries if item is not null
                if (JsonNode.DeepEquals(node, item.MutableNode))
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
