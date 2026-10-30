using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Syntax;

// Imports (ADR-0012) and namespace aliases (ADR-0033).
internal sealed partial class Parser
{
    /// <summary>
    /// <c>import A.B</c> or <c>import A.B under Alias</c>. C#'s <c>using A.B</c> and an alias written with <c>as</c>
    /// are reported and read as the Nyxel form. Static imports and <c>Alias = A.B</c> are reported and skipped:
    /// null then.
    /// </summary>
    private ImportDirectiveSyntax? ParseImportDirective()
    {
        var keyword = NextToken();
        if (Current.Kind == SyntaxKind.StaticKeyword && !AtLineBreak)
        {
            // C#'s 'using static System.MathF', Java's 'import static'.
            var staticKeyword = NextToken();
            var type = ParseQualifiedName();
            var typeName = type.DescendantTokens().Last(t => t.Kind == SyntaxKind.IdentifierToken).Text;
            Report(DiagnosticDescriptors.StaticImport, TextSpan.FromBounds(keyword.Span.Start, type.Span.End), typeName);
            SkipConsumed([keyword, staticKeyword, .. type.DescendantTokens()]);
            return null;
        }
        var name = ParseQualifiedName();
        if (At(SyntaxKind.EqualsToken))
        {
            // C#'s 'using Pb = ep.protocol'. The alias comes first, so it can't be read as 'under' in source order.
            var equals = NextToken();
            var target = ParseQualifiedName();
            Report(DiagnosticDescriptors.ImportAliasSyntax, TextSpan.FromBounds(keyword.Span.Start, target.Span.End), target.ToString(), name.ToString());
            SkipConsumed([keyword, .. name.DescendantTokens(), equals, .. target.DescendantTokens()]);
            return null;
        }
        if (keyword.Kind == SyntaxKind.UsingKeyword)
        {
            Report(DiagnosticDescriptors.UsingDirective, keyword.Span, name.ToString());
        }
        SyntaxToken? under = null;
        SyntaxToken? alias = null;
        var atUnder = Current.Kind == SyntaxKind.IdentifierToken && Current.Text == SyntaxFacts.UnderContextualKeyword;
        if (!AtLineBreak && (atUnder || Current.Kind == SyntaxKind.AsKeyword))
        {
            under = NextToken();
            alias = ExpectIdentifier();
            if (under.Kind == SyntaxKind.AsKeyword && !alias.IsMissing)
            {
                // Python's and Kotlin's 'import x as y'; 'as' is a conversion in Nyxel (ADR-0014).
                Report(DiagnosticDescriptors.ImportAliasSyntax, under.Span, name.ToString(), alias.Text);
            }
        }
        return new ImportDirectiveSyntax(keyword, name, under, alias);
    }

    /// <summary>Two imports of a file can't have the same alias (ADR-0033).</summary>
    private void CheckDuplicateAlias(List<ImportDirectiveSyntax> imports, ImportDirectiveSyntax import)
    {
        if (import.Alias is { IsMissing: false } alias && imports.Any(i => i.Alias?.Text == alias.Text))
        {
            Report(DiagnosticDescriptors.DuplicateImportAlias, alias.Span, alias.Text);
        }
    }
}
