using Nyxel.Compiler.Symbols;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

/// <summary>
/// Which conversions exist between two types. The implicit ones are C#'s (ADR-0010, ADR-0030): numeric widening such
/// as <c>int</c> to <c>float</c>, a class to its base class or interfaces, boxing. Nyxel defines no implicit
/// conversions of its own. C#'s user-defined implicit conversions come in a later step.
/// </summary>
internal static class Conversions
{
    public static ConversionKind? ClassifyImplicit(TypeSymbol from, TypeSymbol to)
    {
        if (ReferenceEquals(from, to) || from.IsError || to.IsError)
        {
            return ConversionKind.Identity;
        }
        if (IsImplicitNumeric(from.SpecialType, to.SpecialType))
        {
            return ConversionKind.ImplicitNumeric;
        }
        if (from.IsReferenceType && IsBaseOrInterface(from, to))
        {
            return ConversionKind.ImplicitReference;
        }
        if (from.IsValueType && IsBaseOrInterface(from, to))
        {
            return ConversionKind.Boxing;
        }
        return null;
    }

    /// <summary>The conversions <c>as</c> makes beyond the implicit ones (ADR-0014): between numeric types.</summary>
    public static ConversionKind? ClassifyExplicit(TypeSymbol from, TypeSymbol to)
    {
        if (ClassifyImplicit(from, to) is { } implicitConversion)
        {
            return implicitConversion;
        }
        if (IsNumeric(from.SpecialType) && IsNumeric(to.SpecialType))
        {
            return ConversionKind.ExplicitNumeric;
        }
        return null;
    }

    /// <summary>A conversion to a base class or an interface the other way round: the kind <c>as</c> can't make.</summary>
    public static bool IsDowncast(TypeSymbol from, TypeSymbol to) => !from.IsError && !to.IsError && IsBaseOrInterface(to, from);

    public static bool IsNumeric(SpecialType type) => IsIntegral(type) || IsFloating(type) || type == SpecialType.System_Decimal;

    public static bool IsIntegral(SpecialType type) => type is SpecialType.System_SByte or SpecialType.System_Byte
        or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Char;

    public static bool IsFloating(SpecialType type) => type is SpecialType.System_Single or SpecialType.System_Double;

    public static bool IsSigned(SpecialType type) => type is SpecialType.System_SByte or SpecialType.System_Int16
        or SpecialType.System_Int32 or SpecialType.System_Int64;

    public static bool IsUnsigned(SpecialType type) => type is SpecialType.System_Byte or SpecialType.System_UInt16
        or SpecialType.System_UInt32 or SpecialType.System_UInt64;

    /// <summary>C#'s table of implicit numeric conversions.</summary>
    public static bool IsImplicitNumeric(SpecialType from, SpecialType to)
    {
        var floatingOrDecimal = to is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
        return from switch
        {
            SpecialType.System_SByte => to is SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64 || floatingOrDecimal,
            SpecialType.System_Byte => to is SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
                or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 || floatingOrDecimal,
            SpecialType.System_Int16 => to is SpecialType.System_Int32 or SpecialType.System_Int64 || floatingOrDecimal,
            SpecialType.System_UInt16 or SpecialType.System_Char => to is SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64 || floatingOrDecimal || (from == SpecialType.System_Char && to == SpecialType.System_UInt16),
            SpecialType.System_Int32 => to is SpecialType.System_Int64 || floatingOrDecimal,
            SpecialType.System_UInt32 => to is SpecialType.System_Int64 or SpecialType.System_UInt64 || floatingOrDecimal,
            SpecialType.System_Int64 or SpecialType.System_UInt64 => floatingOrDecimal,
            SpecialType.System_Single => to == SpecialType.System_Double,
            _ => false,
        };
    }

    private static bool IsBaseOrInterface(TypeSymbol from, TypeSymbol to)
    {
        if (to.SpecialType == SpecialType.System_Object)
        {
            return true;
        }
        if (to.TypeKind == TypeKind.Interface)
        {
            return from.AllInterfaces().Any(i => ReferenceEquals(i, to));
        }
        for (var type = from.BaseType; type != null; type = type.BaseType)
        {
            if (ReferenceEquals(type, to))
            {
                return true;
            }
        }
        return false;
    }
}
