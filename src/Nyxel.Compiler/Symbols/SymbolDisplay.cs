using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Symbols;

/// <summary>
/// Names of symbols as diagnostics show them, in Nyxel's spelling: <c>int</c> rather than <c>Int32</c>,
/// <c>array&lt;int&gt;</c> rather than <c>int[]</c> (docs/design/semantic-analysis.md, "诊断").
/// </summary>
public static class SymbolDisplay
{
    public static string Format(Symbol symbol) => symbol switch
    {
        TypeSymbol type => FormatType(type),
        MethodSymbol { MethodKind: MethodKind.Constructor } constructor =>
            $"init {FormatType(constructor.ContainingType)}({FormatParameters(constructor)})",
        MethodSymbol method => $"{FormatType(method.ContainingType)}.{method.Name}({FormatParameters(method)})"
            + (method.ReturnsVoid ? "" : $" -> {FormatType(method.ReturnType)}"),
        SourceEnumCaseSymbol { HasData: true } dataCase => $"{FormatType(dataCase.ContainingType)}.{dataCase.Name}({string.Join(", ", dataCase.Items.Select(FormatParameter))})",
        FieldSymbol field => $"{FormatType(field.ContainingType)}.{field.Name}",
        PropertySymbol { IsIndexer: true } indexer => $"{FormatType(indexer.ContainingType)}[{string.Join(", ", indexer.Parameters.Select(FormatParameter))}]",
        PropertySymbol property => $"{FormatType(property.ContainingType)}.{property.Name}",
        NamespaceSymbol ns => ns.FullName,
        _ => symbol.Name,
    };

    public static string FormatType(TypeSymbol type) => type switch
    {
        ArrayTypeSymbol array => $"{array.Name}<{FormatType(array.ElementType)}>",
        NullableTypeSymbol nullable => $"{FormatType(nullable.UnderlyingType)}?",
        NamedTypeSymbol named when Keyword(named.SpecialType) is { } keyword => keyword,
        NamedTypeSymbol { Arity: > 0 } generic => $"{generic.Name}<{string.Join(", ", generic.TypeArguments.Select(FormatType))}>",
        _ => type.Name,
    };

    private static string FormatParameters(MethodSymbol method) => string.Join(", ", method.Parameters.Select(FormatParameter));

    private static string FormatParameter(ParameterSymbol parameter) => $"{parameter.Name}: {FormatType(parameter.Type)}";

    /// <summary>The Nyxel keyword for a built-in type (ADR-0007), or null.</summary>
    public static string? Keyword(SpecialType type) => type switch
    {
        SpecialType.System_Boolean => "bool",
        SpecialType.System_Byte => "byte",
        SpecialType.System_SByte => "sbyte",
        SpecialType.System_Int16 => "short",
        SpecialType.System_UInt16 => "ushort",
        SpecialType.System_Int32 => "int",
        SpecialType.System_UInt32 => "uint",
        SpecialType.System_Int64 => "long",
        SpecialType.System_UInt64 => "ulong",
        SpecialType.System_Single => "float",
        SpecialType.System_Double => "double",
        SpecialType.System_Decimal => "decimal",
        SpecialType.System_Char => "char",
        SpecialType.System_String => "string",
        SpecialType.System_Object => "object",
        SpecialType.System_Void => "void",
        _ => null,
    };
}
