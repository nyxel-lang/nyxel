using System.Globalization;
using System.Text;
using Nyxel.Compiler.Symbols;

namespace Nyxel.Compiler.Binding;

/// <summary>The bound tree as indented text, one node per line with its type: for tests and debugging.</summary>
internal static class BoundTreePrinter
{
    public static string Print(BoundNode node)
    {
        var builder = new StringBuilder();
        Print(builder, node, 0);
        return builder.ToString();
    }

    private static void Print(StringBuilder builder, BoundNode? node, int depth)
    {
        if (node is null)
        {
            return;
        }
        builder.Append(' ', depth * 2).Append(Describe(node)).Append('\n');
        foreach (var child in node.GetChildren())
        {
            Print(builder, child, depth + 1);
        }
    }

    private static string Describe(BoundNode node)
    {
        var name = node.GetType().Name["Bound".Length..];
        var detail = node switch
        {
            BoundLiteral literal => literal.Value switch
            {
                string text => $"\"{text}\"",
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                var value => value?.ToString() ?? "null",
            },
            BoundLocalDeclaration declaration => $"{(declaration.Local.IsReadOnly ? "let" : "var")} {declaration.Local.Name}: {SymbolDisplay.FormatType(declaration.Local.Type)}",
            BoundLocalAccess local => local.Local.Name,
            BoundParameterAccess parameter => parameter.Parameter.Name,
            BoundFieldAccess field => SymbolDisplay.Format(field.Field),
            BoundPropertyAccess property => SymbolDisplay.Format(property.Property),
            BoundIndexerAccess indexer => SymbolDisplay.Format(indexer.Indexer),
            BoundCall call => SymbolDisplay.Format(call.Method),
            BoundObjectCreation creation => creation.Constructor is { } constructor ? SymbolDisplay.Format(constructor) : "zero",
            BoundUnary unary => unary.OperatorKind.ToString(),
            BoundBinary binary => binary.OperatorKind.ToString(),
            BoundCompoundAssignment compound => compound.OperatorKind.ToString(),
            BoundConversion conversion => conversion.IsExplicit ? $"{conversion.ConversionKind} (as)" : conversion.ConversionKind.ToString(),
            BoundForRange range => $"{range.Variable.Name}: {SymbolDisplay.FormatType(range.Variable.Type)}{(range.IncludesEnd ? " ..." : " ..<")}",
            BoundForEach each => $"{each.Variable.Name}: {SymbolDisplay.FormatType(each.Variable.Type)}",
            BoundNullCheck check => check.IsNull ? "== null" : "!= null",
            BoundIsType isType => $"{(isType.IsNegated ? "is not" : "is")} {SymbolDisplay.FormatType(isType.TestedType)}",
            BoundIsCase isCase => $"{(isCase.IsNegated ? "is not" : "is")} {SymbolDisplay.Format(isCase.Case)}",
            _ => "",
        };
        var type = node is BoundExpression { Type.IsError: false } expression and not BoundTypeExpression ? $": {SymbolDisplay.FormatType(expression.Type)}" : "";
        return detail.Length == 0 ? $"{name}{type}" : $"{name} {detail}{type}";
    }
}
