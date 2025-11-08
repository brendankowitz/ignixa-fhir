// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Bundle.Serialization;

/// <summary>
/// State machine helper for tracking JSON parsing context during streaming bundle parsing.
/// Manages navigation through the bundle structure and captures entry data.
/// </summary>
internal class BundleParserState
{
    // JSON navigation state
    private int _depth;
    private string? _currentProperty;
    private bool _isInEntryArray;
    private bool _isInEntry;

    // Bundle-level metadata
    private string? _bundleResourceType;
    private string? _bundleType;
    private readonly List<BundleLink> _links = new();
    private readonly List<string> _parsingIssues = new();
    private bool _inLinkArray;
    private bool _inLinkObject;
    private int _linkDepth;
    private string? _currentLinkRelation;
    private string? _currentLinkUrl;
    private bool _entryArrayClosed;

    // Current entry being built
    private BundleEntryContext? _currentEntry;
    private bool _isEntryComplete;

    // Entry construction state
    private int _entryIndex;
    private string? _requestMethod;
    private string? _requestUrl;
    private string? _fullUrl;
    private string? _ifNoneExist;
    private string? _ifMatch;
    private readonly StringBuilder _resourceJsonBuilder = new();
    private int _resourceDepth;
    private bool _inResource;
    private bool _inRequest;
    private int _requestDepth;
    private bool _needsComma; // Tracks if we need a comma before the next property/value
    private bool _justProcessedPropertyName; // Tracks if we just processed a property name (value follows)

    /// <summary>
    /// Gets the current depth in the JSON tree.
    /// </summary>
    public int Depth => _depth;

    /// <summary>
    /// Gets or sets the current property name being parsed.
    /// </summary>
    public string? CurrentProperty
    {
        get => _currentProperty;
        set => _currentProperty = value;
    }

    /// <summary>
    /// Gets a value indicating whether the parser is inside the "entry" array.
    /// </summary>
    public bool IsInEntryArray => _isInEntryArray;

    /// <summary>
    /// Gets a value indicating whether the entry array has been closed (all entries processed).
    /// </summary>
    public bool EntryArrayClosed => _entryArrayClosed;

    /// <summary>
    /// Marks the entry array as closed.
    /// </summary>
    public void CloseEntryArray()
    {
        _entryArrayClosed = true;
        _isInEntryArray = false;
    }

    /// <summary>
    /// Gets a value indicating whether the parser is inside a bundle entry object.
    /// </summary>
    public bool IsInEntry => _isInEntry;

    /// <summary>
    /// Gets the current entry being constructed.
    /// </summary>
    public BundleEntryContext? CurrentEntry => _currentEntry;

    /// <summary>
    /// Gets a value indicating whether the current entry is complete and ready to be yielded.
    /// </summary>
    public bool IsEntryComplete => _isEntryComplete;

    /// <summary>
    /// Increments the depth counter when entering a JSON object or array.
    /// </summary>
    public void IncrementDepth()
    {
        _depth++;
    }

    /// <summary>
    /// Decrements the depth counter when exiting a JSON object or array.
    /// </summary>
    public void DecrementDepth()
    {
        _depth--;
    }

    /// <summary>
    /// Signals that the parser has entered the "entry" array.
    /// </summary>
    public void EnterEntryArray()
    {
        _isInEntryArray = true;
    }

    /// <summary>
    /// Starts tracking a new bundle entry.
    /// Called when entering a new object within the "entry" array.
    /// </summary>
    public void StartNewEntry()
    {
        _isInEntry = true;
        _isEntryComplete = false;
        _requestMethod = null;
        _requestUrl = null;
        _fullUrl = null;
        _ifNoneExist = null;
        _ifMatch = null;
        _resourceJsonBuilder.Clear();
        _resourceDepth = 0;
        _inResource = false;
        _inRequest = false;
        _requestDepth = 0;
    }

    /// <summary>
    /// Marks the current entry as complete and builds the BundleEntryContext.
    /// Called when exiting a bundle entry object.
    /// </summary>
    public void CompleteEntry()
    {
        _isInEntry = false;
        _isEntryComplete = true;

        // Parse resource JSON if present
        ISourceNode? resourceNode = null;
        string? resourceType = null;
        if (_resourceJsonBuilder.Length > 0)
        {
            var resourceJson = _resourceJsonBuilder.ToString();
            // Parse to ResourceJsonNode to enable caching
            // Using cached ToSourceNode() prevents repeated ReflectedSourceNode allocations
            var parsedResource = ResourceJsonNode.Parse(resourceJson);
            resourceNode = parsedResource.ToSourceNode();
            resourceType = resourceNode.Name;
        }

        // Extract resource ID from request URL
        string? resourceId = ExtractIdFromUrl(_requestUrl);

        // Build BundleEntryContext
        _currentEntry = new BundleEntryContext
        {
            Index = _entryIndex,
            HttpVerb = _requestMethod ?? "GET",
            RequestUrl = _requestUrl ?? string.Empty,
            Resource = resourceNode,
            ResourceType = resourceType,
            ResourceId = resourceId,
            FullUrl = _fullUrl,
            RawJson = _resourceJsonBuilder.Length > 0 ? _resourceJsonBuilder.ToString() : null,
            IfNoneExist = _ifNoneExist,
            IfMatch = _ifMatch
        };

        _entryIndex++;
    }

    /// <summary>
    /// Resets the entry completion flag after the entry has been yielded.
    /// </summary>
    public void ResetEntry()
    {
        _isEntryComplete = false;
        _currentEntry = null;
    }

    /// <summary>
    /// Sets a property value for the current entry.
    /// </summary>
    public void SetPropertyValue(string? propertyName, string? value)
    {
        if (!_isInEntry)
        {
            return;
        }

        // Handle top-level entry properties (at depth 3 - inside entry object)
        if (_depth == 3 && propertyName == "fullUrl")
        {
            _fullUrl = value;
            return;
        }

        // Handle request properties (at depth 4 - inside request object, which is inside entry object at depth 3)
        if (_inRequest && _depth == 4)
        {
            switch (propertyName)
            {
                case "method":
                    _requestMethod = value;
                    break;
                case "url":
                    _requestUrl = value;
                    break;
                case "ifNoneExist":
                    _ifNoneExist = value;
                    break;
                case "ifMatch":
                    _ifMatch = value;
                    break;
            }
        }
    }

    /// <summary>
    /// Signals that the parser has entered the "request" object.
    /// </summary>
    public void EnterRequest()
    {
        _inRequest = true;
        _requestDepth = _depth;
    }

    /// <summary>
    /// Signals that the parser has exited the "request" object.
    /// </summary>
    public void ExitRequest()
    {
        _inRequest = false;
        _requestDepth = 0;
    }

    /// <summary>
    /// Signals that the parser has entered the "resource" object.
    /// </summary>
    public void EnterResource()
    {
        _inResource = true;
        _resourceDepth = 1;
        _resourceJsonBuilder.Append('{');
        _needsComma = false; // Reset comma flag for new object
    }

    /// <summary>
    /// Signals that the parser has exited the "resource" object.
    /// </summary>
    public void ExitResource()
    {
        _inResource = false;
        _resourceDepth = 0;
    }

    /// <summary>
    /// Gets a value indicating whether the parser is currently inside a resource object.
    /// </summary>
    public bool InResource => _inResource;

    /// <summary>
    /// Gets a value indicating whether the parser is currently inside a request object.
    /// </summary>
    public bool InRequest => _inRequest;

    /// <summary>
    /// Gets the depth at which the request object was entered.
    /// </summary>
    public int RequestDepth => _requestDepth;

    /// <summary>
    /// Appends a token to the resource JSON builder.
    /// </summary>
    public void AppendResourceToken(string token)
    {
        _resourceJsonBuilder.Append(token);
    }

    /// <summary>
    /// Appends a comma if needed (if this is not the first item in an object/array).
    /// </summary>
    public void AppendCommaIfNeeded()
    {
        if (_needsComma)
        {
            _resourceJsonBuilder.Append(',');
        }
    }

    /// <summary>
    /// Sets the flag to add comma before next item.
    /// Call this after appending a value/object/array.
    /// </summary>
    public void SetCommaNeeded()
    {
        _needsComma = true;
    }

    /// <summary>
    /// Resets the comma flag (call when entering a new object or array).
    /// </summary>
    public void ResetCommaFlag()
    {
        _needsComma = false;
    }

    /// <summary>
    /// Sets the flag indicating we just processed a property name.
    /// The next value token is the property value and should not have a comma before it.
    /// </summary>
    public void SetPropertyNameProcessed()
    {
        _justProcessedPropertyName = true;
    }

    /// <summary>
    /// Checks if we just processed a property name (so next value is a property value, not an array element).
    /// </summary>
    public bool JustProcessedPropertyName => _justProcessedPropertyName;

    /// <summary>
    /// Resets the property name flag after processing the value.
    /// </summary>
    public void ClearPropertyNameFlag()
    {
        _justProcessedPropertyName = false;
    }

    /// <summary>
    /// Increments the resource depth counter.
    /// </summary>
    public void IncrementResourceDepth()
    {
        _resourceDepth++;
    }

    /// <summary>
    /// Decrements the resource depth counter and returns true if the resource is complete.
    /// </summary>
    public bool DecrementResourceDepth()
    {
        _resourceDepth--;
        return _resourceDepth == 0;
    }

    /// <summary>
    /// Gets the current resource depth.
    /// </summary>
    public int ResourceDepth => _resourceDepth;

    /// <summary>
    /// Extracts the resource ID from a FHIR request URL.
    /// Examples: "Patient/123" → "123", "Patient" → null.
    /// </summary>
    private static string? ExtractIdFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        // Remove query string if present
        var urlWithoutQuery = url.Split('?')[0];

        // Parse "Patient/123" → "123"
        var parts = urlWithoutQuery.Split('/');
        return parts.Length == 2 ? parts[1] : null;
    }

    /// <summary>
    /// Gets the bundle resource type.
    /// </summary>
    public string? BundleResourceType => _bundleResourceType;

    /// <summary>
    /// Gets the bundle type (transaction, batch, searchset, etc.).
    /// </summary>
    public string? BundleType => _bundleType;

    /// <summary>
    /// Gets the list of links in the bundle.
    /// </summary>
    public IReadOnlyList<BundleLink> Links => _links;

    /// <summary>
    /// Gets the list of parsing issues encountered.
    /// </summary>
    public IReadOnlyList<string> ParsingIssues => _parsingIssues;

    /// <summary>
    /// Gets a value indicating whether the parser is inside the link array.
    /// </summary>
    public bool InLinkArray => _inLinkArray;

    /// <summary>
    /// Signals that the parser has entered the "link" array.
    /// </summary>
    public void EnterLinkArray()
    {
        _inLinkArray = true;
    }

    /// <summary>
    /// Signals that the parser has exited the "link" array.
    /// </summary>
    public void ExitLinkArray()
    {
        _inLinkArray = false;
    }

    /// <summary>
    /// Signals that the parser has entered a link object within the link array.
    /// </summary>
    public void EnterLinkObject()
    {
        _inLinkObject = true;
        _linkDepth = _depth;
        _currentLinkRelation = null;
        _currentLinkUrl = null;
    }

    /// <summary>
    /// Signals that the parser has exited a link object.
    /// </summary>
    public void ExitLinkObject()
    {
        _inLinkObject = false;

        // Add completed link to list
        if (!string.IsNullOrEmpty(_currentLinkRelation) && !string.IsNullOrEmpty(_currentLinkUrl))
        {
            _links.Add(new BundleLink
            {
                Relation = _currentLinkRelation,
                Url = _currentLinkUrl
            });
        }
        else if (_currentLinkRelation != null || _currentLinkUrl != null)
        {
            _parsingIssues.Add($"Incomplete link: relation={_currentLinkRelation}, url={_currentLinkUrl}");
        }

        _linkDepth = 0;
        _currentLinkRelation = null;
        _currentLinkUrl = null;
    }

    /// <summary>
    /// Gets a value indicating whether the parser is inside a link object.
    /// </summary>
    public bool InLinkObject => _inLinkObject;

    /// <summary>
    /// Sets bundle-level property values (resourceType, type).
    /// </summary>
    public void SetBundleProperty(string? propertyName, string? value)
    {
        // Only capture bundle-level properties at depth 1
        if (_depth != 1)
        {
            return;
        }

        switch (propertyName)
        {
            case "resourceType":
                _bundleResourceType = value;
                if (value != "Bundle")
                {
                    _parsingIssues.Add($"Expected resourceType 'Bundle', got '{value}'");
                }
                break;

            case "type":
                _bundleType = value;
                // Validate bundle type
                var validTypes = new[] { "transaction", "batch", "searchset", "collection", "document", "message", "transaction-response", "batch-response", "history" };
                if (value != null && !validTypes.Contains(value))
                {
                    _parsingIssues.Add($"Unknown bundle type: '{value}'");
                }
                break;
        }
    }

    /// <summary>
    /// Sets link property values (relation, url).
    /// </summary>
    public void SetLinkProperty(string? propertyName, string? value)
    {
        if (!_inLinkObject)
        {
            return;
        }

        switch (propertyName)
        {
            case "relation":
                _currentLinkRelation = value;
                break;

            case "url":
                _currentLinkUrl = value;
                break;
        }
    }

    /// <summary>
    /// Adds a parsing issue to the list.
    /// </summary>
    public void AddParsingIssue(string issue)
    {
        _parsingIssues.Add(issue);
    }
}
