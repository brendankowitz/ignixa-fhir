/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Internal context for managing serialization state.
 */

using System.Text;

namespace Ignixa.FhirMappingLanguage.Serialization;

/// <summary>
/// Internal context for managing serialization state during FML serialization.
/// </summary>
internal sealed class SerializationContext
{
    private readonly StringBuilder _builder;
    private readonly FmlSerializerOptions _options;
    private int _indentLevel;
    private bool _isStartOfLine = true;

    public SerializationContext(StringBuilder builder, FmlSerializerOptions options, int indentLevel)
    {
        _builder = builder;
        _options = options;
        _indentLevel = indentLevel;
    }

    public void Append(string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        if (_isStartOfLine)
        {
            AppendIndent();
        }

        _builder.Append(value);
        _isStartOfLine = value[^1] is '\r' or '\n';
    }

    public void AppendLine()
    {
        _builder.Append(_options.NewLine);
        _isStartOfLine = true;
    }

    public void AppendLine(string value)
    {
        Append(value);
        AppendLine();
    }

    public void IncreaseIndent()
    {
        _indentLevel++;
    }

    public void DecreaseIndent()
    {
        if (_indentLevel > 0)
        {
            _indentLevel--;
        }
    }

    private void AppendIndent()
    {
        for (int i = 0; i < _indentLevel; i++)
        {
            _builder.Append(_options.Indent);
        }
    }
}
