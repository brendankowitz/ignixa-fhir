using System.Text;

namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// Builds the emitted SQL while recording where each section landed. The current offset is simply the
/// buffer length, so ranges come from the same assembly that produces the text — there is no second copy
/// of the concatenation arithmetic to drift. Sections nest; recording is skipped entirely when not asked for.
/// </summary>
internal sealed class SqlTextWriter(bool recordRanges)
{
    private readonly StringBuilder _buffer = new();
    private readonly List<SqlTextRange>? _ranges = recordRanges ? [] : null;

    public IReadOnlyList<SqlTextRange>? Ranges => _ranges;

    public void Append(string text) => _buffer.Append(text);

    public void AppendJoin(string separator, IReadOnlyList<string> values, Func<int, string> labelFor, string kind)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                _buffer.Append(separator);
            }

            using (Section(labelFor(i), kind))
            {
                _buffer.Append(values[i]);
            }
        }
    }

    public SectionScope Section(string label, string kind) => new(this, label, kind, _buffer.Length);

    public override string ToString() => _buffer.ToString();

    private void Close(string label, string kind, int start)
        => _ranges?.Add(new SqlTextRange(label, kind, start, _buffer.Length - start));

    internal readonly struct SectionScope(SqlTextWriter writer, string label, string kind, int start) : IDisposable
    {
        public void Dispose() => writer.Close(label, kind, start);
    }
}
