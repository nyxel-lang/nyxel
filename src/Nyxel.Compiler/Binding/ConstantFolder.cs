using System.Globalization;
using System.Numerics;
using Nyxel.Compiler.Symbols;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

/// <summary>Why folding gave no value.</summary>
internal enum FoldProblem
{
    None,

    /// <summary>The result doesn't fit in its type: C# reports this for constants (CS0220, CS0221), so does Nyxel (NYX2024).</summary>
    OutOfRange,

    /// <summary>Whole-number or <c>decimal</c> division by zero (NYX2025).</summary>
    DivideByZero,
}

/// <summary>
/// A folded value, or the problem that stopped it. <see cref="Value"/> is null for an operation that isn't constant;
/// <see cref="OutOfRange"/> is the value that didn't fit, as text.
/// </summary>
internal readonly record struct Folded(object? Value, FoldProblem Problem = FoldProblem.None, string? OutOfRange = null)
{
    public static Folded None => default;
}

/// <summary>
/// Works out operations on constants at compile time, with C#'s rules for constant expressions (ADR-0038): checked
/// arithmetic, so a result out of range is an error rather than a wrapped value; shift counts masked to the width;
/// floating-point results as IEEE computes them. An enum's value is held as its underlying type, as the members of
/// a C# enum are. The binder calls this when both operands are constants and reports the problems.
/// </summary>
internal static class ConstantFolder
{
    public static Folded Unary(UnaryOperatorKind operatorKind, object operand, TypeSymbol resultType)
    {
        var type = Underlying(resultType);
        if (operatorKind == UnaryOperatorKind.LogicalNot)
        {
            return operand is bool b ? new Folded(!b) : Folded.None;
        }
        if (operatorKind == UnaryOperatorKind.Plus)
        {
            return new Folded(operand);
        }
        if (Conversions.IsIntegral(type) && type != SpecialType.System_Char && AsInteger(operand) is { } integer)
        {
            if (operatorKind == UnaryOperatorKind.Negate)
            {
                return FromInteger(-integer, type);
            }
            // ~x keeps the width: -x - 1 for a signed type, max - x for an unsigned one (a byte enum's ~1 is 254).
            return FromInteger(Conversions.IsSigned(type) ? -integer - 1 : MaxValue(type) - integer, type);
        }
        return (operatorKind, operand) switch
        {
            (UnaryOperatorKind.Negate, double d) => new Folded(-d),
            (UnaryOperatorKind.Negate, float f) => new Folded(-f),
            (UnaryOperatorKind.Negate, decimal m) => new Folded(-m),
            _ => Folded.None,
        };
    }

    /// <summary>
    /// <paramref name="operandType"/> is the type both operands were converted to; <paramref name="resultType"/> the
    /// operation's (<c>bool</c> for a comparison).
    /// </summary>
    public static Folded Binary(BinaryOperatorKind operatorKind, object left, object right, TypeSymbol operandType, TypeSymbol resultType)
    {
        if (left is bool l && right is bool r)
        {
            return operatorKind switch
            {
                BinaryOperatorKind.LogicalAnd or BinaryOperatorKind.BitwiseAnd => new Folded(l && r),
                BinaryOperatorKind.LogicalOr or BinaryOperatorKind.BitwiseOr => new Folded(l || r),
                BinaryOperatorKind.ExclusiveOr or BinaryOperatorKind.NotEqual => new Folded(l != r),
                BinaryOperatorKind.Equal => new Folded(l == r),
                _ => Folded.None,
            };
        }
        if (left is string || right is string)
        {
            // Only string + string and string equality are constant in C#; "a" + 1 is not.
            return (operatorKind, left, right) switch
            {
                (BinaryOperatorKind.Add, string a, string b) when resultType.SpecialType == SpecialType.System_String => new Folded(a + b),
                (BinaryOperatorKind.Equal, string a, string b) => new Folded(string.Equals(a, b, StringComparison.Ordinal)),
                (BinaryOperatorKind.NotEqual, string a, string b) => new Folded(!string.Equals(a, b, StringComparison.Ordinal)),
                _ => Folded.None,
            };
        }

        var type = Underlying(operandType);
        if (Conversions.IsIntegral(type) && AsInteger(left) is { } a1 && AsInteger(right) is { } b1)
        {
            return Integer(operatorKind, a1, b1, type, Underlying(resultType));
        }
        if (type == SpecialType.System_Decimal && left is decimal a2 && right is decimal b2)
        {
            return Decimal(operatorKind, a2, b2);
        }
        if (type == SpecialType.System_Single && left is float a3 && right is float b3)
        {
            return CompareFloating(operatorKind, a3, b3) ?? operatorKind switch
            {
                BinaryOperatorKind.Add => new Folded(a3 + b3),
                BinaryOperatorKind.Subtract => new Folded(a3 - b3),
                BinaryOperatorKind.Multiply => new Folded(a3 * b3),
                BinaryOperatorKind.Divide => new Folded(a3 / b3),
                BinaryOperatorKind.Modulo => new Folded(a3 % b3),
                _ => Folded.None,
            };
        }
        if (type == SpecialType.System_Double && left is double a4 && right is double b4)
        {
            return CompareFloating(operatorKind, a4, b4) ?? operatorKind switch
            {
                BinaryOperatorKind.Add => new Folded(a4 + b4),
                BinaryOperatorKind.Subtract => new Folded(a4 - b4),
                BinaryOperatorKind.Multiply => new Folded(a4 * b4),
                BinaryOperatorKind.Divide => new Folded(a4 / b4),
                BinaryOperatorKind.Modulo => new Folded(a4 % b4),
                _ => Folded.None,
            };
        }
        return Folded.None;
    }

    /// <summary>
    /// A numeric or enum conversion of a constant. <c>T?</c>, boxing and references aren't constant in C#, so they
    /// give no value; a value that doesn't fit the target is out of range, also for <c>as</c> (C#'s CS0221).
    /// </summary>
    public static Folded Conversion(object value, TypeSymbol from, TypeSymbol to)
    {
        if (from.IsNullable || to.IsNullable)
        {
            return Folded.None;
        }
        var source = Underlying(from);
        var target = Underlying(to);
        if (source == target)
        {
            return new Folded(value);
        }
        if (Conversions.IsIntegral(source) && AsInteger(value) is { } integer)
        {
            return target switch
            {
                _ when Conversions.IsIntegral(target) => FromInteger(integer, target),
                SpecialType.System_Single => new Folded((float)integer),
                SpecialType.System_Double => new Folded((double)integer),
                SpecialType.System_Decimal => new Folded((decimal)integer),
                _ => Folded.None,
            };
        }
        if (value is float or double)
        {
            var d = value is float f ? f : (double)value;
            switch (target)
            {
                case SpecialType.System_Single:
                    return new Folded((float)d);
                case SpecialType.System_Double:
                    return new Folded(d);
                case SpecialType.System_Decimal:
                    return double.IsFinite(d) && Math.Abs(d) < 7.9228162514264338e28
                        ? new Folded((decimal)d)
                        : OutOfRange(d.ToString("R", CultureInfo.InvariantCulture));
                case var _ when Conversions.IsIntegral(target):
                    // Toward zero, as C# converts.
                    return double.IsFinite(d)
                        ? FromInteger(new BigInteger(Math.Truncate(d)), target, d.ToString("R", CultureInfo.InvariantCulture))
                        : OutOfRange(d.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        if (value is decimal m)
        {
            return target switch
            {
                SpecialType.System_Single => new Folded((float)m),
                SpecialType.System_Double => new Folded((double)m),
                _ when Conversions.IsIntegral(target) => FromInteger(new BigInteger(decimal.Truncate(m)), target, m.ToString(CultureInfo.InvariantCulture)),
                _ => Folded.None,
            };
        }
        return Folded.None;
    }

    /// <summary>The type an enum's value is held as; any other type's own special type.</summary>
    public static SpecialType Underlying(TypeSymbol type) =>
        type is NamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } ? underlying.SpecialType : type.SpecialType;

    public static BigInteger? AsInteger(object? value) => value switch
    {
        sbyte v => v,
        byte v => v,
        short v => v,
        ushort v => v,
        int v => v,
        uint v => v,
        long v => v,
        ulong v => v,
        char v => v,
        _ => null,
    };

    /// <summary>Whether a whole number fits in an integer type (or <c>char</c>).</summary>
    public static bool Fits(BigInteger value, SpecialType type) => value >= MinValue(type) && value <= MaxValue(type);

    /// <summary>A whole number as a value of the type, which it must fit.</summary>
    public static object ToIntegral(BigInteger value, SpecialType type) => type switch
    {
        SpecialType.System_SByte => (sbyte)value,
        SpecialType.System_Byte => (byte)value,
        SpecialType.System_Int16 => (short)value,
        SpecialType.System_UInt16 => (ushort)value,
        SpecialType.System_Int32 => (int)value,
        SpecialType.System_UInt32 => (uint)value,
        SpecialType.System_Int64 => (long)value,
        SpecialType.System_Char => (char)(ushort)value,
        _ => (ulong)value,
    };

    private static Folded Integer(BinaryOperatorKind operatorKind, BigInteger a, BigInteger b, SpecialType type, SpecialType resultType)
    {
        switch (operatorKind)
        {
            case BinaryOperatorKind.Add:
                return FromInteger(a + b, resultType);
            case BinaryOperatorKind.Subtract:
                return FromInteger(a - b, resultType);
            case BinaryOperatorKind.Multiply:
                return FromInteger(a * b, resultType);
            case BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo when b.IsZero:
                return new Folded(null, FoldProblem.DivideByZero);
            case BinaryOperatorKind.Divide:
                // BigInteger divides toward zero, as C# does; int.MinValue / -1 doesn't fit.
                return FromInteger(BigInteger.Divide(a, b), resultType);
            case BinaryOperatorKind.Modulo:
                return FromInteger(BigInteger.Remainder(a, b), resultType);
            case BinaryOperatorKind.BitwiseAnd:
                return FromInteger(a & b, resultType);
            case BinaryOperatorKind.BitwiseOr:
                return FromInteger(a | b, resultType);
            case BinaryOperatorKind.ExclusiveOr:
                return FromInteger(a ^ b, resultType);
            case BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift:
                return new Folded(Shift(operatorKind == BinaryOperatorKind.LeftShift, a, (int)(b & 63), type));
            default:
                return Compare(operatorKind, a.CompareTo(b)) ?? Folded.None;
        }
    }

    /// <summary>
    /// C#'s shifts: the count is masked to the width (31 for 32 bits, 63 for 64), bits shifted out of the left are
    /// lost without an error, and <c>&gt;&gt;</c> keeps the sign of a signed value.
    /// </summary>
    private static object Shift(bool left, BigInteger value, int count, SpecialType type) => type switch
    {
        SpecialType.System_Int32 => left ? (int)value << (count & 31) : (int)value >> (count & 31),
        SpecialType.System_UInt32 => left ? (uint)value << (count & 31) : (uint)value >> (count & 31),
        SpecialType.System_Int64 => left ? (long)value << count : (long)value >> count,
        _ => left ? (ulong)value << count : (ulong)value >> count,
    };

    private static Folded Decimal(BinaryOperatorKind operatorKind, decimal a, decimal b)
    {
        if (operatorKind is BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo && b == 0)
        {
            return new Folded(null, FoldProblem.DivideByZero);
        }
        try
        {
            return Compare(operatorKind, a.CompareTo(b)) ?? operatorKind switch
            {
                BinaryOperatorKind.Add => new Folded(a + b),
                BinaryOperatorKind.Subtract => new Folded(a - b),
                BinaryOperatorKind.Multiply => new Folded(a * b),
                BinaryOperatorKind.Divide => new Folded(a / b),
                BinaryOperatorKind.Modulo => new Folded(a % b),
                _ => Folded.None,
            };
        }
        catch (OverflowException)
        {
            return OutOfRange($"{a} {Text(operatorKind)} {b}");
        }
    }

    /// <summary>A comparison of floating-point values, where NaN is neither less, equal nor greater than anything.</summary>
    private static Folded? CompareFloating(BinaryOperatorKind operatorKind, double a, double b) => operatorKind switch
    {
        BinaryOperatorKind.Equal => new Folded(a == b),
        BinaryOperatorKind.NotEqual => new Folded(a != b),
        BinaryOperatorKind.Less => new Folded(a < b),
        BinaryOperatorKind.LessOrEqual => new Folded(a <= b),
        BinaryOperatorKind.Greater => new Folded(a > b),
        BinaryOperatorKind.GreaterOrEqual => new Folded(a >= b),
        _ => null,
    };

    /// <summary>A comparison from the operands' order; null for an operator that isn't one.</summary>
    private static Folded? Compare(BinaryOperatorKind operatorKind, int order) => operatorKind switch
    {
        BinaryOperatorKind.Equal => new Folded(order == 0),
        BinaryOperatorKind.NotEqual => new Folded(order != 0),
        BinaryOperatorKind.Less => new Folded(order < 0),
        BinaryOperatorKind.LessOrEqual => new Folded(order <= 0),
        BinaryOperatorKind.Greater => new Folded(order > 0),
        BinaryOperatorKind.GreaterOrEqual => new Folded(order >= 0),
        _ => null,
    };

    private static Folded FromInteger(BigInteger value, SpecialType type, string? text = null) =>
        Fits(value, type) ? new Folded(ToIntegral(value, type)) : OutOfRange(text ?? value.ToString(CultureInfo.InvariantCulture));

    private static Folded OutOfRange(string text) => new(null, FoldProblem.OutOfRange, text);

    private static BigInteger MinValue(SpecialType type) => type switch
    {
        SpecialType.System_SByte => sbyte.MinValue,
        SpecialType.System_Int16 => short.MinValue,
        SpecialType.System_Int32 => int.MinValue,
        SpecialType.System_Int64 => long.MinValue,
        _ => BigInteger.Zero,
    };

    private static BigInteger MaxValue(SpecialType type) => type switch
    {
        SpecialType.System_SByte => sbyte.MaxValue,
        SpecialType.System_Byte => byte.MaxValue,
        SpecialType.System_Int16 => short.MaxValue,
        SpecialType.System_UInt16 or SpecialType.System_Char => ushort.MaxValue,
        SpecialType.System_Int32 => int.MaxValue,
        SpecialType.System_UInt32 => uint.MaxValue,
        SpecialType.System_Int64 => long.MaxValue,
        _ => ulong.MaxValue,
    };

    private static string Text(BinaryOperatorKind operatorKind) => operatorKind switch
    {
        BinaryOperatorKind.Add => "+",
        BinaryOperatorKind.Subtract => "-",
        BinaryOperatorKind.Multiply => "*",
        BinaryOperatorKind.Divide => "/",
        _ => "%",
    };
}
