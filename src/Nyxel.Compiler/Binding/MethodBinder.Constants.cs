using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

// Constants (ADR-0038) and enums without data (ADR-0024, ADR-0040).
internal sealed partial class MethodBinder
{
    /// <summary>An expression whose value has to be a constant, bound without converting it: the value of an enum case.</summary>
    public BoundExpression BindConstantExpression(ExpressionSyntax syntax, TypeSymbol expected) => BindValue(syntax, expected);

    /// <summary>
    /// <c>count: int = 1</c>: as in C#, the value is a constant, <c>null</c> for a <c>T?</c>, or <c>new S()</c> of a
    /// C# struct (all zeros).
    /// </summary>
    public BoundExpression BindDefaultValue(EqualsValueClauseSyntax syntax, TypeSymbol type)
    {
        var value = Convert(BindValue(syntax.Value, type), type);
        if (value.HasErrors || value.ConstantValue is not null
            || value is BoundConversion { ConversionKind: ConversionKind.NullLiteral }
            || value is BoundConversion { ConversionKind: ConversionKind.ImplicitNullable, Operand.ConstantValue: not null }
            || IsZeroStruct(value))
        {
            return value;
        }
        Report(DiagnosticDescriptors.NotConstant, syntax.Value, "A default parameter value", ConstantEvaluator.NotConstantReason(value));
        return Bad(syntax.Value, value);
    }

    /// <summary>
    /// <c>new S()</c> of a C# struct that is all zeros: Roslyn gives a struct from metadata a parameterless constructor
    /// of its own when it declares none. One the struct declares runs code, which a default value can't.
    /// </summary>
    private static bool IsZeroStruct(BoundExpression value) =>
        value is BoundObjectCreation { Arguments.Length: 0, Type.IsValueType: true } creation and not { Type: SourceNamedTypeSymbol }
        && creation.Constructor is null or MetadataMethodSymbol { RoslynSymbol.IsImplicitlyDeclared: true };

    /// <summary>A unary operation, folded when its operand is a constant.</summary>
    private BoundExpression MakeUnary(SyntaxNode syntax, UnaryOperatorKind operatorKind, BoundExpression operand, TypeSymbol type)
    {
        if (operand.ConstantValue is not { } value)
        {
            return new BoundUnary(syntax, operatorKind, operand, type);
        }
        var folded = ConstantFolder.Unary(operatorKind, value, type);
        return ReportFoldProblem(folded, syntax, type) ? Bad(syntax, operand) : new BoundUnary(syntax, operatorKind, operand, type, folded.Value);
    }

    /// <summary>
    /// A binary operation whose operands are converted already, folded when both are constants. Whole numbers and
    /// <c>decimal</c> divided by a constant zero are an error even when the left isn't constant, as in C# (CS0020).
    /// </summary>
    private BoundExpression MakeBinary(SyntaxNode syntax, BinaryOperatorKind operatorKind, BoundExpression left, BoundExpression right, TypeSymbol type)
    {
        if (left.ConstantValue is { } l && right.ConstantValue is { } r)
        {
            var folded = ConstantFolder.Binary(operatorKind, l, r, left.Type, type);
            return ReportFoldProblem(folded, syntax, type) ? Bad(syntax, left, right) : new BoundBinary(syntax, operatorKind, left, right, type, folded.Value);
        }
        if (operatorKind is BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo && IsZero(right.ConstantValue)
            && (Conversions.IsIntegral(right.Type.SpecialType) || right.Type.SpecialType == SpecialType.System_Decimal))
        {
            Report(DiagnosticDescriptors.ConstantDivisionByZero, syntax);
            return Bad(syntax, left, right);
        }
        return new BoundBinary(syntax, operatorKind, left, right, type);
    }

    /// <summary>A numeric or enum conversion, folded when the value is a constant (a value out of range is an error, as in C#).</summary>
    private BoundExpression MakeConversion(SyntaxNode syntax, BoundExpression operand, ConversionKind kind, TypeSymbol type, bool isExplicit)
    {
        if (operand.ConstantValue is not { } value || kind is not (ConversionKind.ImplicitNumeric or ConversionKind.ExplicitNumeric or ConversionKind.ExplicitEnum))
        {
            return new BoundConversion(syntax, operand, kind, type, isExplicit);
        }
        var folded = ConstantFolder.Conversion(value, operand.Type, type);
        return ReportFoldProblem(folded, syntax, type) ? Bad(syntax, operand) : new BoundConversion(syntax, operand, kind, type, isExplicit, folded.Value);
    }

    private bool ReportFoldProblem(Folded folded, SyntaxNode syntax, TypeSymbol type)
    {
        switch (folded.Problem)
        {
            case FoldProblem.OutOfRange:
                Report(DiagnosticDescriptors.ConstantOutOfRange, syntax, folded.OutOfRange!, SymbolDisplay.FormatType(type));
                return true;
            case FoldProblem.DivideByZero:
                Report(DiagnosticDescriptors.ConstantDivisionByZero, syntax);
                return true;
            default:
                return false;
        }
    }

    private static bool IsZero(object? value) => value is decimal m ? m == 0 : ConstantFolder.AsInteger(value) is { IsZero: true };

    /// <summary>
    /// A name of a case: a case of a Nyxel enum (any of the four kinds), a constant of a .NET enum (the constants an
    /// enum declares are its cases), or an alias, a <c>static let</c> of a Nyxel enum whose value is a constant of the
    /// enum (ADR-0045).
    /// </summary>
    private static bool IsCaseName(FieldSymbol field) => field is SourceEnumCaseSymbol
        || (field.ContainingType.TypeKind == TypeKind.Enum && ReferenceEquals(field.Type, field.ContainingType) && field.IsConst);

    private static bool IsEnumCase(BoundExpression expression) =>
        expression is BoundFieldAccess { Field: var field } && IsCaseName(field)
        || expression is BoundEnumCase { Case.HasData: false };

    /// <summary>
    /// <c>state == SlimeState.Idle</c>: whether a value is a case is asked with <c>is</c> (ADR-0040), on either side of
    /// <c>==</c> (ADR-0045). <c>==</c> between two enum values is fine.
    /// </summary>
    private bool ReportCaseComparedWithEquals(BinaryExpressionSyntax syntax, BoundExpression left, BoundExpression right)
    {
        if (syntax.Kind is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
        {
            return false;
        }
        var (value, caseName, caseValue) = syntax.Right is MemberAccessExpressionSyntax && IsEnumCase(right) ? (syntax.Left, syntax.Right, right)
            : syntax.Left is MemberAccessExpressionSyntax && IsEnumCase(left) ? (syntax.Right, syntax.Left, left)
            : default;
        if (caseName is null)
        {
            return false;
        }
        var check = $"{value} {(syntax.Kind == SyntaxKind.EqualsExpression ? "is" : "is not")} {caseName}";
        Report(DiagnosticDescriptors.CaseComparedWithEquals, syntax.OperatorToken, caseName.ToString(), SymbolDisplay.FormatType(caseValue!.Type), check);
        return true;
    }

    /// <summary>
    /// <c>==</c> and <c>!=</c> between two values of one <c>struct enum</c> compare their contents (ADR-0024): the same
    /// case with equal data. Lifted for <c>E?</c>, as for numbers (ADR-0041). A <c>class enum</c> is a reference type,
    /// compared like the others: the record C# makes of it compares the contents too (ADR-0046).
    /// </summary>
    private BoundExpression? ResolveStructEnumEquality(SyntaxNode syntax, BinaryOperatorKind operatorKind, BoundExpression left, BoundExpression right)
    {
        if (operatorKind is not (BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual)
            || left.Type.NonNullable is not SourceNamedTypeSymbol { IsDataEnum: true, TypeKind: TypeKind.Struct } enumType
            || !ReferenceEquals(enumType, right.Type.NonNullable))
        {
            return null;
        }
        if (left.Type.IsNullable || right.Type.IsNullable)
        {
            var nullable = _context.Table.GetNullableType(enumType);
            return MakeBinary(syntax, operatorKind, Convert(left, nullable), Convert(right, nullable), _context.Bool);
        }
        return MakeBinary(syntax, operatorKind, left, right, _context.Bool);
    }

    /// <summary>
    /// Operators on enum values: <c>==</c> and <c>!=</c> between two values of one enum (lifted for <c>E?</c>, as for
    /// numbers, ADR-0041), the order comparisons (ADR-0045), and <c>&amp;</c>, <c>|</c>, <c>^</c> between two of a flags
    /// enum or a C# enum (ADR-0024). Arithmetic is reported (NYX2087); null for other operators, which aren't defined.
    /// </summary>
    private BoundExpression? ResolveEnumBinary(SyntaxNode syntax, SyntaxToken operatorToken, BinaryOperatorKind operatorKind, BoundExpression left, BoundExpression right)
    {
        if (ReportEnumArithmetic(syntax, operatorToken, operatorKind, left, right))
        {
            return Bad(syntax, left, right);
        }
        var enumType = left.Type.NonNullable;
        if (!ReferenceEquals(enumType, right.Type.NonNullable))
        {
            return null;
        }
        switch (operatorKind)
        {
            case BinaryOperatorKind.Less or BinaryOperatorKind.LessOrEqual or BinaryOperatorKind.Greater or BinaryOperatorKind.GreaterOrEqual
                when !left.Type.IsNullable && !right.Type.IsNullable:
                // By the values underneath, as in C#: the order of the cases when none is written.
                return MakeBinary(syntax, operatorKind, left, right, _context.Bool);
            case BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual:
                if (left.Type.IsNullable || right.Type.IsNullable)
                {
                    var nullable = _context.Table.GetNullableType(enumType);
                    return MakeBinary(syntax, operatorKind, Convert(left, nullable), Convert(right, nullable), _context.Bool);
                }
                return MakeBinary(syntax, operatorKind, left, right, _context.Bool);
            case BinaryOperatorKind.BitwiseAnd or BinaryOperatorKind.BitwiseOr or BinaryOperatorKind.ExclusiveOr when !left.Type.IsNullable && !right.Type.IsNullable:
                return ReportPlainEnumBits(operatorToken, enumType) ? Bad(syntax, left, right) : MakeBinary(syntax, operatorKind, left, right, enumType);
            default:
                return null;
        }
    }

    /// <summary>
    /// <c>level + 1</c>, <c>b - a</c>: arithmetic on enum values could give a value that is none of the cases, so it is
    /// written on the numbers (ADR-0045), for .NET's enums too. The message converts the enum side with <c>as int</c>.
    /// </summary>
    private bool ReportEnumArithmetic(SyntaxNode syntax, SyntaxToken operatorToken, BinaryOperatorKind operatorKind, BoundExpression left, BoundExpression right)
    {
        if (operatorKind is not (BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply
            or BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo or BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift))
        {
            return false;
        }
        static bool IsNumberOrEnum(TypeSymbol type) => type.TypeKind == TypeKind.Enum || Conversions.IsWholeNumber(type.SpecialType);
        var enumSide = left.Type.TypeKind == TypeKind.Enum ? left : right.Type.TypeKind == TypeKind.Enum ? right : null;
        if (enumSide is null || !IsNumberOrEnum(left.Type) || !IsNumberOrEnum(right.Type))
        {
            return false;
        }
        var text = syntax is BinaryExpressionSyntax binary ? (ReferenceEquals(enumSide, left) ? binary.Left : binary.Right).ToString()
            : syntax is AssignmentStatementSyntax assignment ? assignment.Target.ToString()
            : enumSide.Syntax.ToString();
        Report(DiagnosticDescriptors.EnumArithmetic, operatorToken, operatorToken.Text, SymbolDisplay.FormatType(enumSide.Type), text);
        return true;
    }

    /// <summary>
    /// Nyxel's enums combine values only when declared <c>flags enum</c> (ADR-0024): any other combination isn't one
    /// of the cases. C#'s enums all can, as in C#: engines don't always mark theirs <c>[Flags]</c>.
    /// </summary>
    private bool ReportPlainEnumBits(SyntaxToken operatorToken, TypeSymbol enumType)
    {
        if (enumType is SourceNamedTypeSymbol { IsFlags: false })
        {
            Report(DiagnosticDescriptors.BitwiseOnPlainEnum, operatorToken, operatorToken.Text, enumType.Name);
            return true;
        }
        return false;
    }

    /// <summary>
    /// <c>x is Element.Fire</c> (ADR-0024): <paramref name="type"/> names a case when its left is an enum with a case
    /// or an alias of that name (ADR-0045). Null when it isn't one, to be bound as a type; a <c>static let</c> of the
    /// enum that isn't a constant is reported.
    /// </summary>
    private FieldSymbol? CaseNamedBy(TypeSyntax type, out bool hadErrors)
    {
        hadErrors = false;
        if (type is not QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier.IsMissing: false } name } qualified)
        {
            return null;
        }
        var left = _scope.BindNamespaceOrType(qualified.Left);
        if (left is null or ErrorTypeSymbol)
        {
            hadErrors = true;
            return null;
        }
        if (left is not NamedTypeSymbol enumType || !(enumType.TypeKind == TypeKind.Enum || enumType is SourceNamedTypeSymbol { IsDataEnum: true })
            || enumType.GetMembers(name.Identifier.Text).OfType<FieldSymbol>().FirstOrDefault(f => ReferenceEquals(f.Type, enumType)) is not { } field)
        {
            return null;
        }
        if (!CheckAccessible(field, name))
        {
            hadErrors = true;
            return null;
        }
        if (!IsCaseName(field))
        {
            Report(DiagnosticDescriptors.NotConstant, name, "A case after 'is'", ConstantEvaluator.NotConstantReason(new BoundFieldAccess(name, null, field)));
            hadErrors = true;
            return null;
        }
        return field;
    }

    /// <summary>
    /// Whether an array of a Nyxel enum can start as all zeros (ADR-0024): a flags enum's 0 is "none", and any other
    /// needs a case whose value is 0. C#'s enums can, as in C#.
    /// </summary>
    private static bool EnumLacksZero(TypeSymbol element) =>
        element is SourceNamedTypeSymbol { TypeKind: TypeKind.Enum, IsFlags: false } enumType
        && enumType.Cases.All(c => c.ConstantValue is not null) && !enumType.Cases.Any(c => IsZero(c.ConstantValue));
}
