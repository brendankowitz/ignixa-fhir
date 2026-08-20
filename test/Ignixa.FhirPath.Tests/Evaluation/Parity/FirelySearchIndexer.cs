using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal sealed class FirelySearchIndexer(
    ISearchParameterDefinitionManager definitions,
    IElementToSearchValueConverterManager converters,
    ISchema schema)
{
    public IReadOnlyCollection<SearchIndexEntry> Extract(IElement resource)
    {
        var entries = new List<SearchIndexEntry>();

        foreach (var parameter in definitions.GetSearchParameters(resource.InstanceType))
        {
            if (IntrinsicSearchParameters.IsIntrinsicCode(parameter.Code)
                || string.IsNullOrWhiteSpace(parameter.Expression))
            {
                continue;
            }

            if (parameter.Type == SearchParamType.Composite)
            {
                entries.AddRange(ExtractComposite(resource, parameter));
                continue;
            }

            IReadOnlyList<Hl7.Fhir.ElementModel.ITypedElement> selected;
            try
            {
                selected = FirelyEngine.Select(resource, schema, parameter.Expression);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var selectedElement in selected)
            {
                var element = new IgnixaElementAdapter(selectedElement);
                if (!converters.TryGetConverter(
                        element.InstanceType,
                        ElementSearchIndexer.GetSearchValueTypeForSearchParamType(parameter.Type),
                        out var converter))
                {
                    continue;
                }

                IReadOnlyList<ISearchValue> values;
                try
                {
                    values = converter.ConvertTo(element).Where(value => value is not null).ToArray();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    continue;
                }

                AddValues(entries, parameter, values);
            }
        }

        ElementSearchIndexer.MarkMinMaxValues(entries);
        return entries;
    }

    private IReadOnlyList<SearchIndexEntry> ExtractComposite(
        IElement resource,
        SearchParameterInfo parameter)
    {
        IReadOnlyList<Hl7.Fhir.ElementModel.ITypedElement> roots;
        try
        {
            roots = FirelyEngine.Select(resource, schema, parameter.Expression);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }

        var entries = new List<SearchIndexEntry>();
        foreach (var root in roots)
        {
            var componentValues = new IReadOnlyList<ISearchValue>[parameter.Component.Count];
            bool complete = true;

            for (int index = 0; index < parameter.Component.Count; index++)
            {
                var component = parameter.Component[index];
                var definition = component.ResolvedSearchParameter;
                if (definition is null || string.IsNullOrWhiteSpace(component.Expression))
                {
                    complete = false;
                    break;
                }

                var values = ExtractComponentValues(root, component.Expression, definition);
                values = values.Where(value => value.IsValidAsCompositeComponent).ToArray();
                if (values.Count == 0)
                {
                    complete = false;
                    break;
                }

                componentValues[index] = values;
            }

            if (complete)
            {
                entries.Add(new SearchIndexEntry(parameter, new CompositeIndexSearchValue(componentValues)));
            }
        }

        return entries;
    }

    private IReadOnlyList<ISearchValue> ExtractComponentValues(
        Hl7.Fhir.ElementModel.ITypedElement root,
        string expression,
        SearchParameterInfo definition)
    {
        IReadOnlyList<Hl7.Fhir.ElementModel.ITypedElement> selected;
        try
        {
            selected = FirelyEngine.Select(root, schema, expression);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }

        var results = new List<ISearchValue>();
        foreach (var selectedElement in selected)
        {
            var element = new IgnixaElementAdapter(selectedElement);
            SearchParamType? effectiveType = definition.Type;
            converters.TryGetConverter(
                element.InstanceType,
                ElementSearchIndexer.GetSearchValueTypeForSearchParamType(effectiveType),
                out var converter);

            if (converter is null)
            {
                effectiveType = ElementSearchIndexer.InferSearchParamTypeFromFhirType(element.InstanceType);
                if (effectiveType is null
                    || !converters.TryGetConverter(
                        element.InstanceType,
                        ElementSearchIndexer.GetSearchValueTypeForSearchParamType(effectiveType),
                        out converter))
                {
                    continue;
                }
            }

            IReadOnlyList<ISearchValue> values;
            try
            {
                values = converter.ConvertTo(element).Where(value => value is not null).ToArray();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                continue;
            }

            AddComponentValues(results, definition, effectiveType, values);
        }

        return results;
    }

    private static void AddValues(
        List<SearchIndexEntry> entries,
        SearchParameterInfo parameter,
        IReadOnlyList<ISearchValue> values)
    {
        foreach (var value in values)
        {
            if (parameter.Type == SearchParamType.Reference
                && parameter.TargetResourceTypes.Count == 1
                && value is ReferenceSearchValue reference
                && string.IsNullOrEmpty(reference.ResourceType))
            {
                entries.Add(
                    new SearchIndexEntry(
                        parameter,
                        new ReferenceSearchValue(
                            reference.Kind,
                            reference.BaseUri,
                            parameter.TargetResourceTypes[0],
                            reference.ResourceId)));
            }
            else
            {
                entries.Add(new SearchIndexEntry(parameter, value));
            }
        }
    }

    private static void AddComponentValues(
        List<ISearchValue> results,
        SearchParameterInfo definition,
        SearchParamType? effectiveType,
        IReadOnlyList<ISearchValue> values)
    {
        foreach (var value in values)
        {
            if (effectiveType == SearchParamType.Reference
                && definition.TargetResourceTypes.Count == 1
                && value is ReferenceSearchValue reference
                && string.IsNullOrEmpty(reference.ResourceType))
            {
                results.Add(
                    new ReferenceSearchValue(
                        reference.Kind,
                        reference.BaseUri,
                        definition.TargetResourceTypes[0],
                        reference.ResourceId));
            }
            else
            {
                results.Add(value);
            }
        }
    }
}
