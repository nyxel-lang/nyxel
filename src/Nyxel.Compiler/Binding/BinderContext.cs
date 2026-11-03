using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

/// <summary>What every part of binding shares: the symbols of the compilation and where diagnostics go.</summary>
internal sealed class BinderContext(SymbolTable table, DiagnosticBag diagnostics)
{
    public SymbolTable Table => table;

    public DiagnosticBag Diagnostics => diagnostics;

    public NamedTypeSymbol GetSpecialType(SpecialType type) => table.GetSpecialType(type);

    public NamedTypeSymbol Void => table.GetSpecialType(SpecialType.System_Void);

    public NamedTypeSymbol Bool => table.GetSpecialType(SpecialType.System_Boolean);

    public NamedTypeSymbol Int => table.GetSpecialType(SpecialType.System_Int32);

    public NamedTypeSymbol String => table.GetSpecialType(SpecialType.System_String);

    public void Report(DiagnosticDescriptor descriptor, SyntaxTree tree, SyntaxNode node, params object[] args) =>
        diagnostics.Report(descriptor, new Location(tree.Text, node.Span), args);

    /// <summary>The type a keyword such as <c>int</c> names (ADR-0007).</summary>
    public NamedTypeSymbol? GetPredefinedType(SyntaxKind keyword)
    {
        SpecialType type = keyword switch
        {
            SyntaxKind.BoolKeyword => SpecialType.System_Boolean,
            SyntaxKind.ByteKeyword => SpecialType.System_Byte,
            SyntaxKind.SByteKeyword => SpecialType.System_SByte,
            SyntaxKind.ShortKeyword => SpecialType.System_Int16,
            SyntaxKind.UShortKeyword => SpecialType.System_UInt16,
            SyntaxKind.IntKeyword => SpecialType.System_Int32,
            SyntaxKind.UIntKeyword => SpecialType.System_UInt32,
            SyntaxKind.LongKeyword => SpecialType.System_Int64,
            SyntaxKind.ULongKeyword => SpecialType.System_UInt64,
            SyntaxKind.FloatKeyword => SpecialType.System_Single,
            SyntaxKind.DoubleKeyword => SpecialType.System_Double,
            SyntaxKind.DecimalKeyword => SpecialType.System_Decimal,
            SyntaxKind.CharKeyword => SpecialType.System_Char,
            SyntaxKind.StringKeyword => SpecialType.System_String,
            SyntaxKind.ObjectKeyword => SpecialType.System_Object,
            _ => SpecialType.None,
        };
        return type == SpecialType.None ? null : table.GetSpecialType(type);
    }
}
