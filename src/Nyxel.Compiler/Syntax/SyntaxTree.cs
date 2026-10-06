using System.Collections.Immutable;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Syntax;

/// <summary>The result of parsing one file: the root node, the lexical and syntax diagnostics, and the text.</summary>
public sealed class SyntaxTree
{
    private SyntaxTree(SourceText text, CompilationUnitSyntax root, ImmutableArray<Diagnostic> diagnostics)
    {
        Text = text;
        Root = root;
        Diagnostics = diagnostics;
    }

    public SourceText Text { get; }

    public CompilationUnitSyntax Root { get; }

    /// <summary>Lexical and syntax diagnostics, ordered by position.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static SyntaxTree Parse(SourceText text)
    {
        var diagnostics = new DiagnosticBag(text);
        var root = Parser.Parse(text, diagnostics);
        return new SyntaxTree(text, root, diagnostics.ToImmutable());
    }

    public static SyntaxTree Parse(string text, string path = "") => Parse(SourceText.From(text, path));

    /// <summary>Only the tokens, as an editor's highlighter or a test would want them.</summary>
    public static ImmutableArray<SyntaxToken> ParseTokens(SourceText text, out ImmutableArray<Diagnostic> diagnostics)
    {
        var bag = new DiagnosticBag(text);
        var tokens = Lexer.Lex(text, bag);
        diagnostics = bag.ToImmutable();
        return tokens;
    }
}
