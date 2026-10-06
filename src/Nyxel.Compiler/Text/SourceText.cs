using System.Collections.Immutable;

namespace Nyxel.Compiler.Text;

/// <summary>The text of one source file plus its path and line table. Immutable.</summary>
public sealed class SourceText
{
    private readonly string _text;
    private readonly ImmutableArray<int> _lineStarts;

    private SourceText(string text, string path)
    {
        _text = text;
        Path = path;
        _lineStarts = ComputeLineStarts(text);
    }

    public static SourceText From(string text, string path = "") => new(text, path);

    /// <summary>The path diagnostics are reported against. Empty for text that did not come from a file.</summary>
    public string Path { get; }

    public int Length => _text.Length;

    public char this[int index] => _text[index];

    public int LineCount => _lineStarts.Length;

    public LinePosition GetLinePosition(int position)
    {
        var line = GetLineIndex(position);
        return new LinePosition(line, position - _lineStarts[line]);
    }

    public LinePositionSpan GetLinePositionSpan(TextSpan span) =>
        new(GetLinePosition(span.Start), GetLinePosition(span.End));

    public int GetLineIndex(int position)
    {
        var index = _lineStarts.BinarySearch(position);
        return index >= 0 ? index : ~index - 1;
    }

    public int GetLineStart(int line) => _lineStarts[line];

    public string ToString(TextSpan span) => _text.Substring(span.Start, span.Length);

    public override string ToString() => _text;

    // A line break is \r\n, \n or \r, the same set the lexer recognizes.
    private static ImmutableArray<int> ComputeLineStarts(string text)
    {
        var starts = ImmutableArray.CreateBuilder<int>();
        starts.Add(0);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                starts.Add(i + 1);
            }
            else if (c == '\n')
            {
                starts.Add(i + 1);
            }
        }
        return starts.ToImmutable();
    }
}
