using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

internal sealed partial class MethodBinder
{
    /// <summary>
    /// Binds an expression that may also be a type, a namespace or a function name (the left of <c>.</c>, the
    /// function of a call). <paramref name="expected"/> is the type the position wants, which decides the type of
    /// number literals (ADR-0007, ADR-0013); the caller still converts the result.
    /// </summary>
    private BoundExpression BindExpression(ExpressionSyntax syntax, TypeSymbol? expected)
    {
        if (syntax.GetFirstToken().IsMissing && syntax.GetLastToken().IsMissing)
        {
            return Bad(syntax);
        }
        if (IsLiteralOnly(syntax))
        {
            return BindLiteralOnly(syntax, expected);
        }
        switch (syntax)
        {
            case LiteralExpressionSyntax literal:
                return BindLiteral(literal);
            case InterpolatedStringExpressionSyntax interpolated:
                return BindInterpolatedString(interpolated);
            case IdentifierNameSyntax name:
                return BindName(name);
            case InstanceExpressionSyntax { Kind: SyntaxKind.SelfExpression } self:
                if (!HasSelf)
                {
                    Report(DiagnosticDescriptors.SelfNotAvailable, self);
                    return Bad(self);
                }
                return new BoundSelf(self, _containingType);
            case ParenthesizedExpressionSyntax parenthesized:
                return BindExpression(parenthesized.Expression, expected);
            case MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax when DeepestConditional(syntax) is { } conditional:
                return BindConditionalAccess(syntax, conditional);
            case MemberAccessExpressionSyntax memberAccess:
                return BindMemberAccess(memberAccess);
            case InvocationExpressionSyntax invocation:
                return BindInvocation(invocation);
            case ElementAccessExpressionSyntax elementAccess:
                return BindElementAccess(elementAccess);
            case ObjectCreationExpressionSyntax creation:
                return BindObjectCreation(creation);
            case UnaryExpressionSyntax { Kind: SyntaxKind.LogicalNotExpression }:
                return BindLogical(syntax);
            case UnaryExpressionSyntax { Kind: SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression or SyntaxKind.BitwiseNotExpression } unary:
                return BindUnary(unary, expected);
            case BinaryExpressionSyntax { Kind: SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression }:
                return BindLogical(syntax);
            case BinaryExpressionSyntax { Kind: SyntaxKind.CoalesceExpression } coalesce:
                return BindCoalesce(coalesce);
            case BinaryExpressionSyntax { Kind: SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression } equality when IsNullLiteral(equality.Left) || IsNullLiteral(equality.Right):
                return BindNullCheck(equality, IsNullLiteral(equality.Right) ? equality.Left : equality.Right, equality.Kind == SyntaxKind.EqualsExpression);
            case BinaryExpressionSyntax binary:
                return BindBinary(binary, expected);
            case IsExpressionSyntax isExpression:
                return BindIs(isExpression);
            case AsExpressionSyntax asExpression:
                return BindAs(asExpression);
            case TypeSyntax type:
                return new BoundTypeExpression(type, _scope.BindType(type));
            default:
                return NotSupported(syntax, Describe(syntax));
        }
    }

    // Literals -------------------------------------------------------------------------------------------------

    private BoundExpression BindLiteral(LiteralExpressionSyntax syntax) => syntax.Kind switch
    {
        SyntaxKind.StringLiteralExpression => new BoundLiteral(syntax, syntax.Token.Value as string ?? "", _context.String),
        SyntaxKind.CharacterLiteralExpression => new BoundLiteral(syntax, syntax.Token.Value as char? ?? '\0', _context.GetSpecialType(SpecialType.System_Char)),
        SyntaxKind.TrueLiteralExpression => new BoundLiteral(syntax, true, _context.Bool),
        SyntaxKind.FalseLiteralExpression => new BoundLiteral(syntax, false, _context.Bool),
        SyntaxKind.NullLiteralExpression => new BoundLiteral(syntax, null, NullTypeSymbol.Instance),
        _ => NotSupported(syntax, Describe(syntax)),
    };

    /// <summary>
    /// Numbers and arithmetic of numbers only (<c>-1</c>, <c>1.5 * 2</c>): typed by the position (ADR-0013). With an
    /// expected numeric type, every literal has that type, so <c>let x: float = 1.5 * 2</c> is a float 3; without
    /// one, whole numbers are <c>int</c> (or the next type that holds them) and a decimal makes it <c>double</c>, as
    /// in C#. Dividing whole numbers stays whole-number division as in C#, so doing it inside a decimal calculation
    /// (<c>let x: float = 1 / 2</c>) is an error rather than a silent 0 (ADR-0038). The value is folded here, which
    /// also catches values out of range.
    /// </summary>
    private BoundExpression BindLiteralOnly(ExpressionSyntax syntax, TypeSymbol? expected)
    {
        TypeSymbol type;
        if (expected is { } target && IsLiteralTarget(target.SpecialType))
        {
            if (Conversions.IsIntegral(target.SpecialType) && FirstDecimalLiteral(syntax) is { } decimalLiteral)
            {
                Report(DiagnosticDescriptors.DecimalLiteralForInteger, decimalLiteral, decimalLiteral.Token.Text, SymbolDisplay.FormatType(target));
                return Bad(syntax);
            }
            type = target;
        }
        else if (FirstDecimalLiteral(syntax) is not null)
        {
            type = _context.GetSpecialType(SpecialType.System_Double);
        }
        else
        {
            var largest = syntax.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Select(l => l.Token.Value as ulong? ?? 0).DefaultIfEmpty().Max();
            // As in C# (ADR-0007): int, or the first of uint, long, ulong that holds the value.
            type = _context.GetSpecialType(largest <= int.MaxValue ? SpecialType.System_Int32
                : largest <= uint.MaxValue ? SpecialType.System_UInt32
                : largest <= long.MaxValue ? SpecialType.System_Int64
                : SpecialType.System_UInt64);
        }

        if (!Conversions.IsIntegral(type.SpecialType) && IntegerLiteralDivision(syntax) is { } division)
        {
            Report(DiagnosticDescriptors.IntegerLiteralDivisionInDecimal, division, division.ToString(), DecimalDivisionSuggestion(division));
            return Bad(syntax);
        }

        var value = Fold(syntax, type.SpecialType);
        return value is null ? Bad(syntax) : new BoundLiteral(syntax, value, type);
    }

    private static bool IsLiteralTarget(SpecialType type) => Conversions.IsNumeric(type) && type != SpecialType.System_Char;

    /// <summary>The first <c>/</c> or <c>%</c> in a literal-only expression whose both sides are whole numbers.</summary>
    private static BinaryExpressionSyntax? IntegerLiteralDivision(ExpressionSyntax syntax) =>
        syntax.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().FirstOrDefault(binary =>
            binary.Kind is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression
            && FirstDecimalLiteral(binary.Left) is null && FirstDecimalLiteral(binary.Right) is null);

    /// <summary>The same division with one side written as a decimal: <c>1.0 / 2</c>.</summary>
    private static string DecimalDivisionSuggestion(BinaryExpressionSyntax division)
    {
        var op = division.Kind == SyntaxKind.DivideExpression ? "/" : "%";
        var left = division.Left.ToString();
        var right = division.Right.ToString();
        return division.Left is LiteralExpressionSyntax leftLiteral && IsPlainDigits(leftLiteral.Token.Text) ? $"{left}.0 {op} {right}"
            : division.Right is LiteralExpressionSyntax rightLiteral && IsPlainDigits(rightLiteral.Token.Text) ? $"{left} {op} {right}.0"
            : $"{left} * 1.0 {op} {right}";

        static bool IsPlainDigits(string text) => text.All(c => char.IsAsciiDigit(c) || c == '_');
    }

    private static bool IsLiteralOnly(ExpressionSyntax syntax) => syntax switch
    {
        LiteralExpressionSyntax { Kind: SyntaxKind.NumericLiteralExpression } => true,
        ParenthesizedExpressionSyntax parenthesized => IsLiteralOnly(parenthesized.Expression),
        UnaryExpressionSyntax { Kind: SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression } unary => IsLiteralOnly(unary.Operand),
        BinaryExpressionSyntax
        {
            Kind: SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression
            or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression
        } binary => IsLiteralOnly(binary.Left) && IsLiteralOnly(binary.Right),
        _ => false,
    };

    private static LiteralExpressionSyntax? FirstDecimalLiteral(ExpressionSyntax syntax) =>
        syntax.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().FirstOrDefault(l => l.Token.Value is double);

    /// <summary>The value of a literal-only expression in <paramref name="type"/>; null after a reported problem.</summary>
    private object? Fold(ExpressionSyntax syntax, SpecialType type)
    {
        if (Conversions.IsIntegral(type))
        {
            if (FoldInteger(syntax) is not { } integer)
            {
                return null;
            }
            if (!FitsIntegral(integer, type))
            {
                Report(DiagnosticDescriptors.ConstantOutOfRange, syntax, integer.ToString(CultureInfo.InvariantCulture), SymbolDisplay.Keyword(type)!);
                return null;
            }
            return ToIntegral(integer, type);
        }
        if (type == SpecialType.System_Decimal)
        {
            try
            {
                return FoldDecimal(syntax);
            }
            catch (OverflowException)
            {
                Report(DiagnosticDescriptors.ConstantOutOfRange, syntax, syntax.ToString(), "decimal");
                return null;
            }
        }
        var single = type == SpecialType.System_Single;
        if (FoldFloating(syntax, single) is not { } floating)
        {
            return null;
        }
        if (double.IsInfinity(floating))
        {
            Report(DiagnosticDescriptors.ConstantOutOfRange, syntax, syntax.ToString(), single ? "float" : "double");
            return null;
        }
        return single ? (float)floating : (object)floating;
    }

    private BigInteger? FoldInteger(ExpressionSyntax syntax)
    {
        switch (syntax)
        {
            case LiteralExpressionSyntax literal:
                return literal.Token.Value is ulong value ? new BigInteger(value) : null;
            case ParenthesizedExpressionSyntax parenthesized:
                return FoldInteger(parenthesized.Expression);
            case UnaryExpressionSyntax unary:
                return FoldInteger(unary.Operand) is { } operand ? (unary.Kind == SyntaxKind.UnaryMinusExpression ? -operand : operand) : null;
            case BinaryExpressionSyntax binary:
                if (FoldInteger(binary.Left) is not { } left || FoldInteger(binary.Right) is not { } right)
                {
                    return null;
                }
                if (binary.Kind is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression && right.IsZero)
                {
                    Report(DiagnosticDescriptors.ConstantDivisionByZero, binary);
                    return null;
                }
                // BigInteger divides toward zero and keeps the sign of the dividend, as C# does.
                return binary.Kind switch
                {
                    SyntaxKind.AddExpression => left + right,
                    SyntaxKind.SubtractExpression => left - right,
                    SyntaxKind.MultiplyExpression => left * right,
                    SyntaxKind.DivideExpression => BigInteger.Divide(left, right),
                    _ => BigInteger.Remainder(left, right),
                };
            default:
                return null;
        }
    }

    private double? FoldFloating(ExpressionSyntax syntax, bool single)
    {
        switch (syntax)
        {
            case LiteralExpressionSyntax literal:
                double? value = literal.Token.Value switch
                {
                    ulong integer => single ? (float)integer : integer,
                    double number when single => float.Parse(LiteralText(literal), NumberStyles.Float, CultureInfo.InvariantCulture),
                    double number => number,
                    _ => null,
                };
                return value;
            case ParenthesizedExpressionSyntax parenthesized:
                return FoldFloating(parenthesized.Expression, single);
            case UnaryExpressionSyntax unary:
                return FoldFloating(unary.Operand, single) is { } operand ? (unary.Kind == SyntaxKind.UnaryMinusExpression ? -operand : operand) : null;
            case BinaryExpressionSyntax binary:
                if (FoldFloating(binary.Left, single) is not { } left || FoldFloating(binary.Right, single) is not { } right)
                {
                    return null;
                }
                var result = binary.Kind switch
                {
                    SyntaxKind.AddExpression => left + right,
                    SyntaxKind.SubtractExpression => left - right,
                    SyntaxKind.MultiplyExpression => left * right,
                    SyntaxKind.DivideExpression => left / right,
                    _ => left % right,
                };
                return single ? (float)result : result;
            default:
                return null;
        }
    }

    private decimal FoldDecimal(ExpressionSyntax syntax) => syntax switch
    {
        LiteralExpressionSyntax { Token.Value: ulong integer } => integer,
        LiteralExpressionSyntax literal => decimal.Parse(LiteralText(literal), NumberStyles.Float, CultureInfo.InvariantCulture),
        ParenthesizedExpressionSyntax parenthesized => FoldDecimal(parenthesized.Expression),
        UnaryExpressionSyntax unary => unary.Kind == SyntaxKind.UnaryMinusExpression ? -FoldDecimal(unary.Operand) : FoldDecimal(unary.Operand),
        BinaryExpressionSyntax binary => binary.Kind switch
        {
            SyntaxKind.AddExpression => FoldDecimal(binary.Left) + FoldDecimal(binary.Right),
            SyntaxKind.SubtractExpression => FoldDecimal(binary.Left) - FoldDecimal(binary.Right),
            SyntaxKind.MultiplyExpression => FoldDecimal(binary.Left) * FoldDecimal(binary.Right),
            SyntaxKind.DivideExpression => FoldDecimal(binary.Left) / FoldDecimal(binary.Right),
            _ => FoldDecimal(binary.Left) % FoldDecimal(binary.Right),
        },
        _ => 0,
    };

    private static string LiteralText(LiteralExpressionSyntax literal) => literal.Token.Text.Replace("_", "", StringComparison.Ordinal);

    private static bool FitsIntegral(BigInteger value, SpecialType type) => type switch
    {
        SpecialType.System_SByte => value >= sbyte.MinValue && value <= sbyte.MaxValue,
        SpecialType.System_Byte => value >= byte.MinValue && value <= byte.MaxValue,
        SpecialType.System_Int16 => value >= short.MinValue && value <= short.MaxValue,
        SpecialType.System_UInt16 => value >= ushort.MinValue && value <= ushort.MaxValue,
        SpecialType.System_Int32 => value >= int.MinValue && value <= int.MaxValue,
        SpecialType.System_UInt32 => value >= uint.MinValue && value <= uint.MaxValue,
        SpecialType.System_Int64 => value >= long.MinValue && value <= long.MaxValue,
        SpecialType.System_UInt64 => value >= ulong.MinValue && value <= ulong.MaxValue,
        _ => false,
    };

    private static object ToIntegral(BigInteger value, SpecialType type) => type switch
    {
        SpecialType.System_SByte => (sbyte)value,
        SpecialType.System_Byte => (byte)value,
        SpecialType.System_Int16 => (short)value,
        SpecialType.System_UInt16 => (ushort)value,
        SpecialType.System_Int32 => (int)value,
        SpecialType.System_UInt32 => (uint)value,
        SpecialType.System_Int64 => (long)value,
        _ => (ulong)value,
    };

    /// <summary>
    /// A literal-only operand next to a value of <paramref name="otherType"/> (ADR-0013): it takes the other
    /// operand's type, except that a decimal next to a whole number makes the operation floating, in the expected
    /// type when that is floating and in <c>double</c> otherwise.
    /// </summary>
    private BoundExpression BindLiteralOperand(ExpressionSyntax literal, TypeSymbol otherType, TypeSymbol? expected, bool arithmetic)
    {
        // Next to a float?, a literal is a float: 'f == 0.5' compares floats (ADR-0041).
        otherType = otherType.NonNullable;
        if (otherType.IsError)
        {
            return BindLiteralOnly(literal, null);
        }
        if (Conversions.IsIntegral(otherType.SpecialType) && FirstDecimalLiteral(literal) is not null)
        {
            var floating = arithmetic && expected is { } wanted && IsLiteralTarget(wanted.SpecialType)
                ? wanted
                : _context.GetSpecialType(SpecialType.System_Double);
            return BindLiteralOnly(literal, floating);
        }
        return BindLiteralOnly(literal, IsLiteralTarget(otherType.SpecialType) ? otherType : null);
    }

    private BoundExpression BindInterpolatedString(InterpolatedStringExpressionSyntax syntax)
    {
        var parts = ImmutableArray.CreateBuilder<BoundInterpolationPart>();
        foreach (var content in syntax.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    parts.Add(new BoundInterpolationPart(text.TextToken.Value as string ?? "", null, null, null));
                    break;
                case InterpolationSyntax hole:
                    var value = BindValue(hole.Expression);
                    var alignment = hole.AlignmentClause is { } clause ? Convert(BindValue(clause.Value, _context.Int), _context.Int) : null;
                    var format = hole.FormatClause is { FormatStringToken.IsMissing: false } formatClause ? formatClause.FormatStringToken.Text : null;
                    parts.Add(new BoundInterpolationPart(null, value, alignment, format));
                    break;
            }
        }
        return new BoundInterpolatedString(syntax, parts.ToImmutable(), _context.String);
    }

    // Names and members ------------------------------------------------------------------------------------------

    /// <summary>
    /// A name without a receiver: a local or parameter (ADR-0008), or else a type or namespace. A member of the type
    /// written without <c>self.</c> or the type name gets a diagnostic that says which to write.
    /// </summary>
    private BoundExpression BindName(IdentifierNameSyntax syntax)
    {
        if (syntax.Identifier.IsMissing)
        {
            return Bad(syntax);
        }
        var name = syntax.Identifier.Text;
        switch (LookupLocal(name))
        {
            case LocalSymbol local:
                return new BoundLocalAccess(syntax, local, TypeAtThisPoint(local, local.Type));
            case ParameterSymbol parameter:
                return new BoundParameterAccess(syntax, parameter, TypeAtThisPoint(parameter, parameter.Type));
        }
        switch (_scope.LookupTypeOrNamespace(name, 0, syntax))
        {
            case TypeSymbol type:
                return new BoundTypeExpression(syntax, type);
            case NamespaceSymbol ns:
                return new BoundNamespaceExpression(syntax, ns);
        }
        if (LookupMembers(_containingType, name).FirstOrDefault() is { } member)
        {
            var receiver = IsStatic(member) ? _containingType.Name : "self";
            Report(DiagnosticDescriptors.MemberNeedsReceiver, syntax, name, _containingType.Name, receiver);
            return Bad(syntax);
        }
        Report(DiagnosticDescriptors.UndefinedName, syntax, name);
        return Bad(syntax);
    }

    private BoundExpression BindMemberAccess(MemberAccessExpressionSyntax syntax)
    {
        // The receiver of 'a?.B' is in place already: a placeholder for 'a' without null (BindConditionalAccess).
        BoundExpression receiver = syntax.Kind == SyntaxKind.ConditionalMemberAccessExpression && _conditionalReceivers.TryGetValue(syntax, out var conditional)
            ? conditional
            : BindExpression(syntax.Expression, null);
        if (syntax.Name.Identifier.IsMissing)
        {
            return Bad(syntax, receiver);
        }
        var name = syntax.Name.Identifier.Text;
        var typeArguments = syntax.Name is GenericNameSyntax generic ? [.. generic.TypeArgumentList.Arguments.Select(_scope.BindType)] : ImmutableArray<TypeSymbol>.Empty;
        switch (receiver)
        {
            case BoundNamespaceExpression ns:
                if (ns.Namespace.GetTypes(name).FirstOrDefault(t => t.Arity == typeArguments.Length) is { } type)
                {
                    return new BoundTypeExpression(syntax, typeArguments.Length == 0 ? type : typeArguments.Any(a => a.IsError) ? ErrorTypeSymbol.Instance : type.Construct(typeArguments));
                }
                if (typeArguments.Length == 0 && ns.Namespace.GetNamespace(name) is { } child)
                {
                    return new BoundNamespaceExpression(syntax, child);
                }
                Report(DiagnosticDescriptors.MemberNotFound, syntax.Name, name, ns.Namespace.FullName);
                return Bad(syntax);
            case BoundMethodGroup group:
                Report(DiagnosticDescriptors.FunctionIsNotValue, syntax.Expression, group.Name);
                return Bad(syntax);
            case { HasErrors: true }:
                return Bad(syntax, receiver);
            case BoundTypeExpression typeReceiver:
                return BindMember(syntax, typeReceiver, name, onType: true);
            case { Type.IsVoid: true }:
                ReportNoValue(receiver);
                return Bad(syntax);
            case BoundPropertyAccess { Property.HasGetter: false } property:
                Report(DiagnosticDescriptors.PropertyHasNoGetter, syntax.Expression, SymbolDisplay.Format(property.Property));
                return Bad(syntax);
            case { Type: NullTypeSymbol }:
                Report(DiagnosticDescriptors.MemberNotFound, syntax.Name, name, "null");
                return Bad(syntax, receiver);
            case { Type.IsNullable: true }:
                ReportMaybeNull(receiver, syntax.Expression, suggestConditional: true);
                return Bad(syntax, receiver);
            default:
                return BindMember(syntax, receiver, name, onType: false);
        }
    }

    /// <summary><c>Type.Name</c> (static members, nested types) or <c>value.Name</c> (instance members).</summary>
    private BoundExpression BindMember(MemberAccessExpressionSyntax syntax, BoundExpression receiver, string name, bool onType)
    {
        var members = LookupMembers(receiver.Type, name);
        if (members.Length == 0)
        {
            Report(DiagnosticDescriptors.MemberNotFound, syntax.Name, name, SymbolDisplay.FormatType(receiver.Type));
            return Bad(syntax, receiver);
        }
        if (members.All(m => m is MethodSymbol))
        {
            return new BoundMethodGroup(syntax, receiver, name, [.. members.Cast<MethodSymbol>()]);
        }
        var member = members[0];
        if (!CheckAccessible(member, syntax.Name))
        {
            return Bad(syntax, receiver);
        }
        if (member is NamedTypeSymbol nested)
        {
            if (!onType)
            {
                Report(DiagnosticDescriptors.StaticMemberOnValue, syntax.Name, name, SymbolDisplay.FormatType(receiver.Type));
                return Bad(syntax, receiver);
            }
            return new BoundTypeExpression(syntax, nested);
        }
        var isStatic = IsStatic(member);
        if (onType && !isStatic)
        {
            Report(DiagnosticDescriptors.InstanceMemberOnType, syntax.Name, name, SymbolDisplay.FormatType(receiver.Type));
            return Bad(syntax, receiver);
        }
        if (!onType && isStatic)
        {
            Report(DiagnosticDescriptors.StaticMemberOnValue, syntax.Name, name, SymbolDisplay.FormatType(ContainingTypeOf(member)!));
            return Bad(syntax, receiver);
        }
        var instance = isStatic ? null : receiver;
        return member switch
        {
            FieldSymbol field => new BoundFieldAccess(syntax, instance, field),
            PropertySymbol property => new BoundPropertyAccess(syntax, instance, property),
            _ => Bad(syntax, receiver),
        };
    }

    /// <summary>
    /// The members named <paramref name="name"/> of a type and its base types. Functions add up through the base
    /// types, leaving out the ones overridden further down; any other member hides those of base types.
    /// </summary>
    private ImmutableArray<Symbol> LookupMembers(TypeSymbol type, string name)
    {
        var methods = new List<MethodSymbol>();
        var types = new List<TypeSymbol>();
        for (TypeSymbol? current = type; current != null; current = current.BaseType)
        {
            types.Add(current);
        }
        if (type.TypeKind == TypeKind.Interface)
        {
            types.AddRange(type.AllInterfaces());
            types.Add(_context.GetSpecialType(SpecialType.System_Object));
        }
        foreach (var current in types)
        {
            var members = current.GetMembers(name);
            var others = members.Where(m => m is not MethodSymbol).ToImmutableArray();
            if (others.Length > 0 && methods.Count == 0)
            {
                return others;
            }
            foreach (var method in members.OfType<MethodSymbol>())
            {
                if (!methods.Any(m => Overrides(m, method)))
                {
                    methods.Add(method);
                }
            }
        }
        return [.. methods];

        static bool Overrides(MethodSymbol derived, MethodSymbol baseMethod)
        {
            for (var overridden = derived.OriginalDefinition.OverriddenMethod; overridden != null; overridden = overridden.OverriddenMethod)
            {
                if (ReferenceEquals(overridden, baseMethod.OriginalDefinition))
                {
                    return true;
                }
            }
            return false;
        }
    }

    private static bool IsStatic(Symbol member) => member switch
    {
        MethodSymbol method => method.IsStatic,
        FieldSymbol field => field.IsStatic,
        PropertySymbol property => property.IsStatic,
        _ => true,
    };

    private static NamedTypeSymbol? ContainingTypeOf(Symbol member) => member switch
    {
        MethodSymbol method => method.ContainingType,
        FieldSymbol field => field.ContainingType,
        PropertySymbol property => property.ContainingType,
        NamedTypeSymbol type => type.ContainingType,
        _ => null,
    };

    /// <summary>Private members are for their own type, protected ones for derived types too (ADR-0015).</summary>
    private bool CheckAccessible(Symbol member, SyntaxNode location)
    {
        var owner = ContainingTypeOf(member)?.OriginalDefinition;
        var accessible = member.Accessibility switch
        {
            Accessibility.Private => ReferenceEquals(owner, _containingType),
            Accessibility.Protected => DerivesFrom(_containingType, owner),
            _ => true,
        };
        if (!accessible)
        {
            Report(DiagnosticDescriptors.Inaccessible, location, SymbolDisplay.Format(member), member.Accessibility == Accessibility.Private ? "private" : "protected");
        }
        return accessible;

        static bool DerivesFrom(NamedTypeSymbol type, NamedTypeSymbol? baseType)
        {
            for (NamedTypeSymbol? current = type; current != null; current = current.BaseType)
            {
                if (ReferenceEquals(current.OriginalDefinition, baseType))
                {
                    return true;
                }
            }
            return false;
        }
    }

    // Operators ------------------------------------------------------------------------------------------------

    private BoundExpression BindUnary(UnaryExpressionSyntax syntax, TypeSymbol? expected)
    {
        var operatorKind = syntax.Kind switch
        {
            SyntaxKind.UnaryMinusExpression => UnaryOperatorKind.Negate,
            SyntaxKind.UnaryPlusExpression => UnaryOperatorKind.Plus,
            SyntaxKind.BitwiseNotExpression => UnaryOperatorKind.BitwiseNot,
            _ => UnaryOperatorKind.LogicalNot,
        };
        var operand = BindValue(syntax.Operand, expected);
        return ResolveUnary(syntax, operatorKind, operand);
    }

    private BoundExpression ResolveUnary(UnaryExpressionSyntax syntax, UnaryOperatorKind operatorKind, BoundExpression operand)
    {
        if (operand.HasErrors)
        {
            return Bad(syntax, operand);
        }
        var type = operand.Type.SpecialType;
        SpecialType? result = operatorKind switch
        {
            UnaryOperatorKind.LogicalNot => type == SpecialType.System_Boolean ? type : null,
            UnaryOperatorKind.BitwiseNot => Conversions.IsIntegral(type) ? UnaryPromotion(type) : null,
            UnaryOperatorKind.Negate => type == SpecialType.System_UInt64 ? null
                : type == SpecialType.System_UInt32 ? SpecialType.System_Int64
                : Conversions.IsNumeric(type) ? UnaryPromotion(type) : null,
            _ => Conversions.IsNumeric(type) ? UnaryPromotion(type) : null,
        };
        if (result is not { } resultType)
        {
            if (operand.Type.IsNullable)
            {
                ReportMaybeNull(operand, operand.Syntax, suggestConditional: false);
                return Bad(syntax, operand);
            }
            Report(DiagnosticDescriptors.UnaryOperatorNotDefined, syntax.OperatorToken, syntax.OperatorToken.Text, SymbolDisplay.FormatType(operand.Type));
            return Bad(syntax, operand);
        }
        var typeSymbol = _context.GetSpecialType(resultType);
        return new BoundUnary(syntax, operatorKind, Convert(operand, typeSymbol), typeSymbol);
    }

    /// <summary>C#'s unary numeric promotion: the small integer types and <c>char</c> become <c>int</c>.</summary>
    private static SpecialType UnaryPromotion(SpecialType type) => type is SpecialType.System_SByte or SpecialType.System_Byte
        or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char ? SpecialType.System_Int32 : type;

    private BoundExpression BindBinary(BinaryExpressionSyntax syntax, TypeSymbol? expected)
    {
        var operatorKind = BinaryOperator(syntax.Kind);
        var arithmetic = operatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply
            or BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo;
        // The expected type goes in through arithmetic (ADR-0013); a comparison or 'and' doesn't pass it on.
        var inward = arithmetic || operatorKind is BinaryOperatorKind.BitwiseAnd or BinaryOperatorKind.BitwiseOr or BinaryOperatorKind.ExclusiveOr
            ? expected
            : null;
        BoundExpression left, right;
        if (IsLiteralOnly(syntax.Left) && !IsLiteralOnly(syntax.Right))
        {
            right = BindValue(syntax.Right, inward);
            left = BindLiteralOperand(syntax.Left, right.Type, expected, arithmetic);
        }
        else if (IsLiteralOnly(syntax.Right) && !IsLiteralOnly(syntax.Left))
        {
            left = BindValue(syntax.Left, inward);
            right = operatorKind is BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift
                ? BindLiteralOnly(syntax.Right, _context.Int)
                : BindLiteralOperand(syntax.Right, left.Type, expected, arithmetic);
        }
        else
        {
            left = BindValue(syntax.Left, inward);
            right = BindValue(syntax.Right, inward);
        }
        if (left.HasErrors || right.HasErrors)
        {
            return Bad(syntax, left, right);
        }
        return ResolveBinary(syntax, syntax.OperatorToken, operatorKind, left, right);
    }

    private static BinaryOperatorKind BinaryOperator(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddExpression => BinaryOperatorKind.Add,
        SyntaxKind.SubtractExpression => BinaryOperatorKind.Subtract,
        SyntaxKind.MultiplyExpression => BinaryOperatorKind.Multiply,
        SyntaxKind.DivideExpression => BinaryOperatorKind.Divide,
        SyntaxKind.ModuloExpression => BinaryOperatorKind.Modulo,
        SyntaxKind.LeftShiftExpression => BinaryOperatorKind.LeftShift,
        SyntaxKind.RightShiftExpression => BinaryOperatorKind.RightShift,
        SyntaxKind.LessThanExpression => BinaryOperatorKind.Less,
        SyntaxKind.LessThanOrEqualExpression => BinaryOperatorKind.LessOrEqual,
        SyntaxKind.GreaterThanExpression => BinaryOperatorKind.Greater,
        SyntaxKind.GreaterThanOrEqualExpression => BinaryOperatorKind.GreaterOrEqual,
        SyntaxKind.EqualsExpression => BinaryOperatorKind.Equal,
        SyntaxKind.NotEqualsExpression => BinaryOperatorKind.NotEqual,
        SyntaxKind.BitwiseAndExpression => BinaryOperatorKind.BitwiseAnd,
        SyntaxKind.BitwiseOrExpression => BinaryOperatorKind.BitwiseOr,
        SyntaxKind.ExclusiveOrExpression => BinaryOperatorKind.ExclusiveOr,
        SyntaxKind.LogicalAndExpression => BinaryOperatorKind.LogicalAnd,
        _ => BinaryOperatorKind.LogicalOr,
    };

    /// <summary>C#'s predefined operators for the two operand types (ADR-0010); the operands come back converted.</summary>
    private BoundExpression ResolveBinary(SyntaxNode syntax, SyntaxToken operatorToken, BinaryOperatorKind operatorKind, BoundExpression left, BoundExpression right)
    {
        var l = left.Type.SpecialType;
        var r = right.Type.SpecialType;
        var boolType = _context.Bool;
        switch (operatorKind)
        {
            case BinaryOperatorKind.Add when l == SpecialType.System_String || r == SpecialType.System_String:
                return new BoundBinary(syntax, operatorKind, left, right, _context.String);
            case BinaryOperatorKind.LogicalAnd or BinaryOperatorKind.LogicalOr when l == SpecialType.System_Boolean && r == SpecialType.System_Boolean:
                return new BoundBinary(syntax, operatorKind, left, right, boolType);
            case BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual when IsNullableValue(left.Type) || IsNullableValue(right.Type):
                return LiftedEquality();
            case BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual:
                if ((l == r && l is SpecialType.System_Boolean or SpecialType.System_String)
                    || (left.Type.IsReferenceType && right.Type.IsReferenceType
                        && (Conversions.ClassifyImplicit(left.Type, right.Type) is not null || Conversions.ClassifyImplicit(right.Type, left.Type) is not null)))
                {
                    return new BoundBinary(syntax, operatorKind, left, right, boolType);
                }
                return Numeric(boolType);
            case BinaryOperatorKind.Less or BinaryOperatorKind.LessOrEqual or BinaryOperatorKind.Greater or BinaryOperatorKind.GreaterOrEqual:
                return Numeric(boolType);
            case BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply or BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo:
                return Numeric(null);
            case BinaryOperatorKind.BitwiseAnd or BinaryOperatorKind.BitwiseOr or BinaryOperatorKind.ExclusiveOr:
                if (l == SpecialType.System_Boolean && r == SpecialType.System_Boolean)
                {
                    return new BoundBinary(syntax, operatorKind, left, right, boolType);
                }
                return Conversions.IsIntegral(l) && Conversions.IsIntegral(r) ? Numeric(null) : NotDefined();
            case BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift:
                if (Conversions.IsIntegral(l) && Conversions.ClassifyImplicit(right.Type, _context.Int) is not null)
                {
                    var shifted = _context.GetSpecialType(UnaryPromotion(l));
                    return new BoundBinary(syntax, operatorKind, Convert(left, shifted), Convert(right, _context.Int), shifted);
                }
                return NotDefined();
            default:
                return NotDefined();
        }

        BoundExpression Numeric(TypeSymbol? resultType)
        {
            if (NumericPromotion(l, r) is not { } promoted)
            {
                return NotDefined();
            }
            var operandType = _context.GetSpecialType(promoted);
            return new BoundBinary(syntax, operatorKind, Convert(left, operandType), Convert(right, operandType), resultType ?? operandType);
        }

        // int? == int, int? != long?: C#'s lifted operators (ADR-0041). Both sides go to the T? of the type they would
        // be compared in without '?'; null equals only null.
        BoundExpression LiftedEquality()
        {
            var (l0, r0) = (left.Type.NonNullable.SpecialType, right.Type.NonNullable.SpecialType);
            SpecialType? compared = l0 == SpecialType.System_Boolean && r0 == SpecialType.System_Boolean ? SpecialType.System_Boolean : NumericPromotion(l0, r0);
            if (compared is not { } type)
            {
                Report(DiagnosticDescriptors.OperatorNotDefined, operatorToken, operatorToken.Text, SymbolDisplay.FormatType(left.Type), SymbolDisplay.FormatType(right.Type));
                return Bad(syntax, left, right);
            }
            var operandType = _context.Table.GetNullableType(_context.GetSpecialType(type));
            return new BoundBinary(syntax, operatorKind, Convert(left, operandType), Convert(right, operandType), boolType);
        }

        static bool IsNullableValue(TypeSymbol type) => type.IsNullable && type.IsValueType;

        BoundExpression NotDefined()
        {
            // int? + 1: C# lifts the operator; Nyxel wants the value checked first, as for its members (ADR-0009).
            if (left.Type.IsNullable || right.Type.IsNullable)
            {
                var nullable = left.Type.IsNullable ? left : right;
                ReportMaybeNull(nullable, nullable.Syntax, suggestConditional: false);
                return Bad(syntax, left, right);
            }
            Report(DiagnosticDescriptors.OperatorNotDefined, operatorToken, operatorToken.Text, SymbolDisplay.FormatType(left.Type), SymbolDisplay.FormatType(right.Type));
            return Bad(syntax, left, right);
        }
    }

    /// <summary>C#'s binary numeric promotion: the type both operands become; null when there is none.</summary>
    private static SpecialType? NumericPromotion(SpecialType left, SpecialType right)
    {
        if (!Conversions.IsNumeric(left) || !Conversions.IsNumeric(right))
        {
            return null;
        }
        if (left == SpecialType.System_Decimal || right == SpecialType.System_Decimal)
        {
            return Conversions.IsFloating(left) || Conversions.IsFloating(right) ? null : SpecialType.System_Decimal;
        }
        if (left == SpecialType.System_Double || right == SpecialType.System_Double)
        {
            return SpecialType.System_Double;
        }
        if (left == SpecialType.System_Single || right == SpecialType.System_Single)
        {
            return SpecialType.System_Single;
        }
        if (left == SpecialType.System_UInt64 || right == SpecialType.System_UInt64)
        {
            return Conversions.IsSigned(left) || Conversions.IsSigned(right) ? null : SpecialType.System_UInt64;
        }
        if (left == SpecialType.System_Int64 || right == SpecialType.System_Int64)
        {
            return SpecialType.System_Int64;
        }
        if (left == SpecialType.System_UInt32 || right == SpecialType.System_UInt32)
        {
            var other = left == SpecialType.System_UInt32 ? right : left;
            return other is SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 ? SpecialType.System_Int64 : SpecialType.System_UInt32;
        }
        return SpecialType.System_Int32;
    }

    private BoundExpression BindAs(AsExpressionSyntax syntax)
    {
        var operand = BindValue(syntax.Expression);
        var type = _scope.BindType(syntax.Type);
        if (operand.HasErrors || type.IsError)
        {
            return Bad(syntax, operand);
        }
        var kind = Conversions.ClassifyExplicit(operand.Type, type);
        if (kind is null)
        {
            var descriptor = Conversions.IsDowncast(operand.Type, type) ? DiagnosticDescriptors.AsMightFail : DiagnosticDescriptors.AsNotAllowed;
            Report(descriptor, syntax, SymbolDisplay.FormatType(operand.Type), SymbolDisplay.FormatType(type));
            return Bad(syntax, operand);
        }
        if (kind == ConversionKind.Identity)
        {
            return operand;
        }
        // A constant whole number must fit (ADR-0014: a constant out of range is an error, as in C#).
        if (operand.ConstantValue is sbyte or byte or short or ushort or int or uint or long or ulong
            && Conversions.IsIntegral(type.SpecialType) && type.SpecialType != SpecialType.System_Char
            && !FitsIntegral(new BigInteger(System.Convert.ToDecimal(operand.ConstantValue, CultureInfo.InvariantCulture)), type.SpecialType))
        {
            Report(DiagnosticDescriptors.ConstantOutOfRange, syntax, operand.ConstantValue, SymbolDisplay.FormatType(type));
            return Bad(syntax, operand);
        }
        return new BoundConversion(syntax, operand, kind.Value, type, isExplicit: true);
    }
}
