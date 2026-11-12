using Nyxel.Compiler.Symbols;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

/// <summary>
/// Which conversions exist between two types. The implicit ones are C#'s (ADR-0010, ADR-0030): numeric widening such
/// as <c>int</c> to <c>float</c>, a class to its base class or interfaces, boxing, and a value or <c>null</c> to a
/// <c>T?</c>. A <c>T?</c> never converts to a type that can't be null (ADR-0009): it has to be checked first. Nyxel
/// defines no implicit conversions of its own. C#'s user-defined implicit conversions come in a later step.
/// </summary>
internal static class Conversions
{
    public static ConversionKind? ClassifyImplicit(TypeSymbol from, TypeSymbol to)
    {
        if (ReferenceEquals(from, to) || from.IsError || to.IsError)
        {
            return ConversionKind.Identity;
        }
        if (from is NullTypeSymbol)
        {
            return to.IsNullable ? ConversionKind.NullLiteral : null;
        }
        if (to is NullableTypeSymbol target)
        {
            // T to T? and T? to U? go the way T goes to U: int to long?, Slime? to Enemy?, int? to object?.
            return ClassifyImplicit(from.NonNullable, target.UnderlyingType) switch
            {
                ConversionKind.Identity or ConversionKind.ImplicitNumeric => ConversionKind.ImplicitNullable,
                ConversionKind.ImplicitReference => ConversionKind.ImplicitReference,
                ConversionKind.Boxing => ConversionKind.Boxing,
                _ => null,
            };
        }
        if (from.IsNullable)
        {
            return null;
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

    /// <summary>
    /// The conversions <c>as</c> makes beyond the implicit ones (ADR-0014): between numeric types, and between an
    /// enum and a whole number.
    /// </summary>
    public static ConversionKind? ClassifyExplicit(TypeSymbol from, TypeSymbol to)
    {
        if (ClassifyImplicit(from, to) is { } implicitConversion)
        {
            return implicitConversion;
        }
        // Between two T? too (long? as int?), but a T? never becomes a T.
        if (from.IsNullable != to.IsNullable)
        {
            return null;
        }
        var (source, target) = (from.NonNullable, to.NonNullable);
        if (IsNumeric(source.SpecialType) && IsNumeric(target.SpecialType))
        {
            return ConversionKind.ExplicitNumeric;
        }
        if ((source.TypeKind == TypeKind.Enum && IsWholeNumber(target.SpecialType)) || (target.TypeKind == TypeKind.Enum && IsWholeNumber(source.SpecialType)))
        {
            return ConversionKind.ExplicitEnum;
        }
        return null;
    }

    /// <summary>The integer types, without <c>char</c>.</summary>
    public static bool IsWholeNumber(SpecialType type) => IsIntegral(type) && type != SpecialType.System_Char;

    /// <summary>A conversion to a base class or an interface the other way round: the kind <c>as</c> can't make.</summary>
    public static bool IsDowncast(TypeSymbol from, TypeSymbol to) =>
        !from.IsError && !to.IsError && IsBaseOrInterface(to.NonNullable, from.NonNullable);

    /// <summary>
    /// The same .NET type: identical, or <c>T</c> and <c>T?</c> of a reference type, which C# treats as one type when
    /// it chooses an overload.
    /// </summary>
    public static bool IsSameRuntimeType(TypeSymbol a, TypeSymbol b) =>
        ReferenceEquals(a, b)
        || (a.IsReferenceType && ReferenceEquals(a.NonNullable, b.NonNullable));

    /// <summary>
    /// Whether <c>value is target</c> can be true: the value's type and the target are related by inheritance or an
    /// interface (C# reports a pattern that never matches as an error too).
    /// </summary>
    public static bool CanBe(TypeSymbol valueType, TypeSymbol target)
    {
        var source = valueType.NonNullable;
        if (source.IsError || target.IsError || ClassifyImplicit(source, target) is not null || IsBaseOrInterface(target, source))
        {
            return true;
        }
        // An interface and a class that isn't sealed can meet in a derived class; two interfaces always can.
        static bool Open(TypeSymbol type) => type.TypeKind == TypeKind.Interface
            || (type.TypeKind == TypeKind.Class && type is not NamedTypeSymbol { IsSealed: true })
            || type.TypeKind == TypeKind.TypeParameter;
        return (source.TypeKind == TypeKind.Interface && Open(target)) || (target.TypeKind == TypeKind.Interface && Open(source))
            || source.TypeKind == TypeKind.TypeParameter || target.TypeKind == TypeKind.TypeParameter;
    }

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
