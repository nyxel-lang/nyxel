using System.Collections;
using System.Collections.Immutable;
using System.Text;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Syntax;

/// <summary>
/// A node of the syntax tree. Trees are immutable and lossless: every character of the source, including
/// whitespace, comments and skipped tokens, belongs to exactly one token, so <see cref="ToFullString"/> of the root
/// reproduces the file. Tokens are nodes too (leaves), which keeps child enumeration uniform.
/// </summary>
public abstract class SyntaxNode
{
    private protected SyntaxNode(SyntaxKind kind) => Kind = kind;

    public SyntaxKind Kind { get; }

    public SyntaxNode? Parent { get; private set; }

    /// <summary>Child nodes and tokens in source order, without nulls.</summary>
    public abstract IEnumerable<SyntaxNode> GetChildren();

    /// <summary>The span of the text without the leading trivia of the first token and trailing trivia of the last.</summary>
    public virtual TextSpan Span => TextSpan.FromBounds(GetFirstToken().Span.Start, GetLastToken().Span.End);

    /// <summary>The span including all trivia.</summary>
    public virtual TextSpan FullSpan => TextSpan.FromBounds(GetFirstToken().FullSpan.Start, GetLastToken().FullSpan.End);

    public SyntaxToken GetFirstToken() => this as SyntaxToken ?? GetChildren().First().GetFirstToken();

    public SyntaxToken GetLastToken() => this as SyntaxToken ?? GetChildren().Last().GetLastToken();

    public IEnumerable<SyntaxToken> DescendantTokens()
    {
        if (this is SyntaxToken token)
        {
            yield return token;
            yield break;
        }
        foreach (var child in GetChildren())
        {
            foreach (var descendant in child.DescendantTokens())
            {
                yield return descendant;
            }
        }
    }

    public IEnumerable<SyntaxNode> DescendantNodesAndSelf()
    {
        yield return this;
        foreach (var child in GetChildren())
        {
            foreach (var descendant in child.DescendantNodesAndSelf())
            {
                yield return descendant;
            }
        }
    }

    public string ToFullString()
    {
        var builder = new StringBuilder();
        foreach (var token in DescendantTokens())
        {
            token.WriteTo(builder, leading: true, trailing: true);
        }
        return builder.ToString();
    }

    /// <summary>The source text of the node without its outer trivia.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        var first = GetFirstToken();
        var last = GetLastToken();
        foreach (var token in DescendantTokens())
        {
            token.WriteTo(builder, leading: token != first, trailing: token != last);
        }
        return builder.ToString();
    }

    /// <summary>Called at the end of every constructor: makes this node the parent of its children.</summary>
    private protected void AdoptChildren()
    {
        foreach (var child in GetChildren())
        {
            child.Parent = this;
        }
    }

    /// <summary>Flattens children for <see cref="GetChildren"/>: nodes, tokens and lists; nulls are skipped.</summary>
    private protected static IEnumerable<SyntaxNode> Children(params object?[] items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case null:
                    break;
                case SyntaxNode node:
                    yield return node;
                    break;
                case ISyntaxList list:
                    foreach (var element in list.GetNodesAndSeparators())
                    {
                        yield return element;
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected child {item.GetType()}.");
            }
        }
    }
}

/// <summary>Whitespace, a line break, a comment, or tokens the parser skipped. Belongs to a token.</summary>
public readonly record struct SyntaxTrivia(SyntaxKind Kind, int Position, string Text)
{
    public TextSpan Span => new(Position, Text.Length);
}

public sealed class SyntaxToken : SyntaxNode
{
    internal SyntaxToken(
        SyntaxKind kind,
        int position,
        string text,
        object? value,
        ImmutableArray<SyntaxTrivia> leadingTrivia,
        ImmutableArray<SyntaxTrivia> trailingTrivia,
        bool hasLeadingLineBreak,
        bool isMissing = false)
        : base(kind)
    {
        Position = position;
        Text = text;
        Value = value;
        LeadingTrivia = leadingTrivia;
        TrailingTrivia = trailingTrivia;
        HasLeadingLineBreak = hasLeadingLineBreak;
        IsMissing = isMissing;
    }

    internal static SyntaxToken Missing(SyntaxKind kind, int position) =>
        new(kind, position, "", null, [], [], hasLeadingLineBreak: false, isMissing: true);

    /// <summary>Start of the token text, after its leading trivia.</summary>
    public int Position { get; }

    public string Text { get; }

    /// <summary>The value of a literal: <see cref="ulong"/> or <see cref="double"/> for numbers, the unescaped string for strings.</summary>
    public object? Value { get; }

    public ImmutableArray<SyntaxTrivia> LeadingTrivia { get; }

    public ImmutableArray<SyntaxTrivia> TrailingTrivia { get; }

    /// <summary>
    /// True when a line break separates this token from the previous one (or it is the first token of the file).
    /// The parser uses this for the line rules of ADR-0013; it is not affected by skipped-token trivia added later.
    /// </summary>
    public bool HasLeadingLineBreak { get; }

    /// <summary>True for a zero-width token the parser inserted where a required token was absent.</summary>
    public bool IsMissing { get; }

    public override TextSpan Span => new(Position, Text.Length);

    public override TextSpan FullSpan => TextSpan.FromBounds(
        Position - LeadingTrivia.Sum(t => t.Text.Length),
        Span.End + TrailingTrivia.Sum(t => t.Text.Length));

    public override IEnumerable<SyntaxNode> GetChildren() => [];

    internal SyntaxToken WithLeadingTrivia(ImmutableArray<SyntaxTrivia> leadingTrivia) =>
        new(Kind, Position, Text, Value, leadingTrivia, TrailingTrivia, HasLeadingLineBreak, IsMissing);

    internal void WriteTo(StringBuilder builder, bool leading, bool trailing)
    {
        if (leading)
        {
            foreach (var trivia in LeadingTrivia)
            {
                builder.Append(trivia.Text);
            }
        }
        builder.Append(Text);
        if (trailing)
        {
            foreach (var trivia in TrailingTrivia)
            {
                builder.Append(trivia.Text);
            }
        }
    }

    public override string ToString() => Text;
}

internal interface ISyntaxList
{
    IEnumerable<SyntaxNode> GetNodesAndSeparators();
}

/// <summary>A list of nodes of one kind, such as the statements of a block.</summary>
public sealed class SyntaxList<T> : IReadOnlyList<T>, ISyntaxList
    where T : SyntaxNode
{
    private readonly ImmutableArray<T> _items;

    internal SyntaxList(ImmutableArray<T> items) => _items = items;

    public static SyntaxList<T> Empty { get; } = new([]);

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    IEnumerable<SyntaxNode> ISyntaxList.GetNodesAndSeparators() => _items;
}

/// <summary>Nodes separated by tokens, such as parameters separated by commas. Enumerates the nodes only.</summary>
public sealed class SeparatedSyntaxList<T> : IReadOnlyList<T>, ISyntaxList
    where T : SyntaxNode
{
    private readonly ImmutableArray<SyntaxNode> _nodesAndSeparators;

    internal SeparatedSyntaxList(ImmutableArray<SyntaxNode> nodesAndSeparators) => _nodesAndSeparators = nodesAndSeparators;

    public static SeparatedSyntaxList<T> Empty { get; } = new([]);

    public int Count => (_nodesAndSeparators.Length + 1) / 2;

    public T this[int index] => (T)_nodesAndSeparators[index * 2];

    public SyntaxToken GetSeparator(int index) => (SyntaxToken)_nodesAndSeparators[(index * 2) + 1];

    public int SeparatorCount => _nodesAndSeparators.Length / 2;

    public IEnumerable<SyntaxNode> GetNodesAndSeparators() => _nodesAndSeparators;

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
