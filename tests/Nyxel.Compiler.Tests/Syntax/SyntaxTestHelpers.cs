using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Tests.Syntax;

internal static class SyntaxTestHelpers
{
    /// <summary>The repository root, found by walking up from the test binaries to Nyxel.slnx.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Nyxel.slnx")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("Nyxel.slnx not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// A compact S-expression of a tree for assertions: <c>(AddExpression a + (MultiplyExpression b * c))</c>.
    /// Names, literals and predefined types print as their text; missing tokens as <c>&lt;missing&gt;</c>.
    /// </summary>
    public static string Compact(SyntaxNode node) => node switch
    {
        SyntaxToken token => token.IsMissing ? "<missing>" : token.Text,
        IdentifierNameSyntax or LiteralExpressionSyntax or PredefinedTypeSyntax or InstanceExpressionSyntax =>
            node.GetFirstToken().IsMissing ? "<missing>" : node.ToString(),
        _ => $"({node.Kind} {string.Join(" ", node.GetChildren().Select(Compact))})",
    };

    /// <summary>
    /// Every <see cref="SeparatedSyntaxList{T}"/> in the tree alternates elements and separators, even after error
    /// recovery; its indexer relies on that.
    /// </summary>
    public static void AssertSeparatedListsAlternate(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodesAndSelf())
        {
            foreach (var property in node.GetType().GetProperties())
            {
                if (!property.PropertyType.IsGenericType || property.PropertyType.GetGenericTypeDefinition() != typeof(SeparatedSyntaxList<>))
                {
                    continue;
                }
                var elementType = property.PropertyType.GetGenericArguments()[0];
                var list = property.GetValue(node)!;
                var items = ((IEnumerable<SyntaxNode>)property.PropertyType.GetMethod("GetNodesAndSeparators")!.Invoke(list, null)!).ToList();
                for (var i = 0; i < items.Count; i++)
                {
                    var expected = i % 2 == 0 ? elementType : typeof(SyntaxToken);
                    Assert.True(expected.IsInstanceOfType(items[i]), $"{node.Kind}.{property.Name}[{i}] is {items[i].Kind}");
                }
            }
        }
    }

    /// <summary>Parses a file and asserts it has no diagnostics.</summary>
    public static SyntaxTree ParseClean(string source)
    {
        var tree = SyntaxTree.Parse(source, "test.nyxel");
        Assert.True(tree.Diagnostics.IsEmpty, "Unexpected diagnostics:\n" + string.Join("\n", tree.Diagnostics));
        Assert.Equal(source, tree.Root.ToFullString());
        return tree;
    }

    /// <summary>Wraps statements in a method of a class.</summary>
    public static string InMethod(string statements) =>
        "class C {\n    func F(self) {\n" + statements + "\n    }\n}\n";

    public static StatementSyntax ParseStatement(string statement)
    {
        var tree = ParseClean(InMethod(statement));
        return Body(tree).Statements.Single();
    }

    /// <summary>The value of <c>let x = expression</c>.</summary>
    public static ExpressionSyntax ParseExpression(string expression)
    {
        var statement = (LocalDeclarationStatementSyntax)ParseStatement("let x = " + expression);
        return statement.Initializer!.Value;
    }

    public static MemberDeclarationSyntax ParseMember(string member)
    {
        var tree = ParseClean("class C {\n" + member + "\n}\n");
        return ((TypeDeclarationSyntax)tree.Root.Members.Single()).Members.Single();
    }

    public static BlockSyntax Body(SyntaxTree tree)
    {
        var type = (TypeDeclarationSyntax)tree.Root.Members[0];
        return ((FunctionDeclarationSyntax)type.Members[0]).Body!;
    }

    /// <summary>Diagnostics as "NYX1103 3:21" (one-based line:column of the start), in order.</summary>
    public static string[] DiagnosticsOf(string source)
    {
        var tree = SyntaxTree.Parse(source, "test.nyxel");
        Assert.Equal(source, tree.Root.ToFullString());
        return [.. tree.Diagnostics.Select(Format)];
    }

    public static string Format(Diagnostic diagnostic) => $"{diagnostic.Id} {diagnostic.Location.LineSpan.Start}";
}
