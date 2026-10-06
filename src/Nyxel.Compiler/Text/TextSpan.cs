namespace Nyxel.Compiler.Text;

/// <summary>A range of characters in a <see cref="SourceText"/>, as UTF-16 offsets. <see cref="End"/> is exclusive.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public static TextSpan FromBounds(int start, int end) => new(start, end - start);

    public bool Contains(int position) => position >= Start && position < End;

    public override string ToString() => $"[{Start}..{End})";
}

/// <summary>A zero-based line and UTF-16 column. Displayed one-based, the same convention as #line (debug-mapping.md).</summary>
public readonly record struct LinePosition(int Line, int Character)
{
    public override string ToString() => $"{Line + 1}:{Character + 1}";
}

/// <summary>Start and end <see cref="LinePosition"/> of a span; the end is exclusive.</summary>
public readonly record struct LinePositionSpan(LinePosition Start, LinePosition End)
{
    public override string ToString() => $"{Start}-{End}";
}
