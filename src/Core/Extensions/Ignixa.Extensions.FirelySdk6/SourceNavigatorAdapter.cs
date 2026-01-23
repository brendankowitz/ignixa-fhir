// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using SdkISourceNode = Hl7.Fhir.ElementModel.ISourceNode;

namespace Ignixa.Extensions.FirelySdk;

/// <summary>
/// Adapts Firely SDK's ISourceNode to Ignixa's ISourceNavigator.
/// </summary>
/// <remarks>
/// <para>
/// This adapter enables using Firely SDK-parsed FHIR data with Ignixa's schema-aware
/// element navigation. Chain with the <c>.ToElement(schema)</c> extension method
/// (from <c>Ignixa.Serialization</c>) to get an <see cref="IElement"/> with type metadata.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// ISourceNode firelyNode = FhirJsonNode.Parse(json);
/// ISourceNavigator navigator = firelyNode.ToSourceNavigator();
/// IElement element = navigator.ToElement(schema);  // From Ignixa.Serialization
/// </code>
/// </para>
/// <para>
/// Note: Firely's ISourceNode is simpler than Ignixa's ISourceNavigator. This adapter:
/// - Derives ResourceType from the "resourceType" child element (for FHIR resources)
/// - Forwards annotations to the underlying IAnnotatable if supported
/// </para>
/// </remarks>
public class SourceNavigatorAdapter : ISourceNavigator
{
    private readonly SdkISourceNode _firelyNode;
    private string? _resourceType;
    private bool _resourceTypeResolved;

    /// <summary>
    /// Creates a new adapter wrapping a Firely SDK ISourceNode.
    /// </summary>
    /// <param name="sourceNode">Firely SDK source node to adapt.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceNode"/> is null.</exception>
    public SourceNavigatorAdapter(SdkISourceNode sourceNode)
    {
        _firelyNode = sourceNode ?? throw new ArgumentNullException(nameof(sourceNode));
    }

    /// <inheritdoc/>
    public string Name => _firelyNode.Name;

    /// <inheritdoc/>
    public string Text => _firelyNode.Text;

    /// <inheritdoc/>
    public string Location => _firelyNode.Location;

    /// <inheritdoc/>
    /// <remarks>
    /// For Firely SDK nodes, we check if Text is not null to determine if there's a primitive value.
    /// Firely handles shadow properties internally, so if Text has a value, it's the actual primitive.
    /// </remarks>
    public bool HasPrimitiveValue => _firelyNode.Text != null;

    /// <inheritdoc/>
    /// <remarks>
    /// Firely's ISourceNode doesn't have a ResourceType property.
    /// This implementation derives it from the "resourceType" child element if present.
    /// </remarks>
    public string ResourceType
    {
        get
        {
            if (!_resourceTypeResolved)
            {
                // Look for "resourceType" child which contains the FHIR resource type
                var resourceTypeChild = _firelyNode.Children("resourceType").FirstOrDefault();
                _resourceType = resourceTypeChild?.Text ?? string.Empty;
                _resourceTypeResolved = true;
            }

            return _resourceType ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public IEnumerable<ISourceNavigator> Children(string? name = null)
    {
        foreach (var child in _firelyNode.Children(name))
        {
            yield return new SourceNavigatorAdapter(child);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Firely's ISourceNode doesn't have a Meta method directly.
    /// This implementation checks if the underlying node implements IAnnotatable
    /// (which Firely's source nodes typically do) and forwards to it.
    /// </remarks>
    public T? Meta<T>() where T : class
    {
        // Firely's ISourceNode implementations typically implement IAnnotated (SDK 5.x+)
        // In SDK 6.x, IAnnotatable extends IAnnotated, so we can cast to either
        if (_firelyNode is Hl7.Fhir.Utility.IAnnotated annotated)
        {
            // SDK 5.x and 6.x: IAnnotated.Annotations(Type) method
            return annotated.Annotations(typeof(T)).OfType<T>().FirstOrDefault();
        }

        // Check if the adapter itself matches
        if (this is T typed)
        {
            return typed;
        }

        return null;
    }
}
