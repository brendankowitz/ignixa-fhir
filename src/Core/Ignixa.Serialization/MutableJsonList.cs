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

        /// <summary>
        /// The backing array as it exists today, or null when the element is absent. Reads must use this:
        /// resolving the factory on a read would inject an empty array into the document, which is both
        /// invalid FHIR (arrays must have at least one element) and a mutation of a resource nobody edited.
        /// </summary>
        private JsonArray? ReadArray => _jsonArray;

        /// <summary>
        /// The backing array, creating and attaching it if absent. Only mutating members may use this.
        /// </summary>
        private JsonArray MutableArray => _jsonArray ??= arrayFactory();

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
                    return default!;
                }
                return _factory(node, _fhirVersion);
            }
            set
            {
                System.ArgumentOutOfRangeException.ThrowIfNegative(index);
                System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ReadCount, nameof(index));
                MutableArray[index] = value?.MutableNode;
            }
        }

        public int Count => ReadCount;

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            MutableArray.Add(item?.MutableNode);
        }

        public void Clear()
        {
            _jsonArray?.Clear();
        }

        public bool Contains(T item)
        {
            if (item == null)
            {
                foreach (var node in ReadArray ?? [])
                {
                    if (node == null) return true;
                }
                return false;
            }
            foreach (var node in ReadArray ?? [])
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
            if (array.Length - arrayIndex < ReadCount) System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(arrayIndex + ReadCount, array.Length, nameof(arrayIndex));

            for (int i = 0; i < ReadCount; i++)
            {
                var node = ReadArray![i];
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
            foreach (var node in ReadArray ?? [])
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
                for (int i = 0; i < ReadCount; i++)
                {
                    if (ReadArray![i] == null) return i;
                }
                return -1;
            }
            for (int i = 0; i < ReadCount; i++)
            {
                var node = ReadArray![i];
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
            MutableArray.Insert(index, item?.MutableNode);
        }

        public bool Remove(T item)
        {
            if (item == null)
            {
                for (int i = 0; i < ReadCount; i++)
                {
                    if (ReadArray![i] == null)
                    {
                        ReadArray!.RemoveAt(i);
                        return true;
                    }
                }
                return false;
            }
            for (int i = 0; i < ReadCount; i++)
            {
                var node = ReadArray![i];
                if (node == null) continue; // Skip null entries if item is not null
                if (JsonNode.DeepEquals(node, item.MutableNode))
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
