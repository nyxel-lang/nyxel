using System.Collections.Immutable;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;
using FlowState = System.Collections.Immutable.ImmutableDictionary<Nyxel.Compiler.Symbols.Symbol, Nyxel.Compiler.Symbols.TypeSymbol>;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

// match (ADR-0009, ADR-0010, ADR-0024, ADR-0025, ADR-0026; docs/design/semantic-analysis.md, "match"). Each arm is
// bound in a scope of its own, starting from the flow state after the value matched; 'case is T' and 'case not null'
// narrow a local or parameter matched. CheckArms then follows the arms in order: an arm that no value can reach is an
// error, as in C#, and so is a match that leaves a value out.
internal sealed partial class MethodBinder
{
    /// <summary><c>match</c> as a statement: each arm is a statement, a block or a statement that leaves.</summary>
    private BoundStatement BindMatchStatement(MatchExpressionSyntax syntax)
    {
        var value = BindValue(syntax.Expression);
        var start = _flow;
        FlowState? end = null;
        var arms = ImmutableArray.CreateBuilder<BoundMatchArm>(syntax.Arms.Count);
        foreach (var armSyntax in syntax.Arms)
        {
            _flow = start;
            PushScope();
            var (pattern, when) = BindArmHead(armSyntax, value);
            var body = BindArmStatement(armSyntax.Body);
            PopScope();
            if (FlowAnalysis.CanComplete(body))
            {
                end = Join(end, _flow);
            }
            arms.Add(new BoundMatchArm(armSyntax, pattern, when, body, null));
            start = AfterArm(start, value, pattern, when);
        }
        CheckArms(syntax, value, arms);
        _flow = end ?? start;
        return new BoundMatchStatement(syntax, value, arms.ToImmutable());
    }

    private BoundStatement BindArmStatement(SyntaxNode body)
    {
        switch (body)
        {
            case BlockSyntax block:
                return BindBlock(block);
            case JumpExpressionSyntax jump:
                return BindArmJump(jump) ?? new BoundExpressionStatement(jump, Bad(jump));
            case AssignmentStatementSyntax assignment:
                return BindAssignment(assignment);
            case ExpressionSyntax expression:
                return BindExpressionStatement(expression, expression);
            case StatementSyntax statement:
                return BindStatement(statement);
            default:
                return new BoundExpressionStatement(body, Bad(body));
        }
    }

    /// <summary>
    /// A leave written as an arm. <c>throw</c> goes anywhere; <c>return</c>, <c>break</c> and <c>continue</c> in an arm
    /// of a match that gives a value only where the match is a whole statement's value (as for <c>?? return</c>).
    /// </summary>
    private BoundStatement? BindArmJump(JumpExpressionSyntax syntax, MatchExpressionSyntax? valueMatch = null)
    {
        switch (syntax.Kind)
        {
            case SyntaxKind.ThrowExpression:
                return BindThrow(syntax, syntax.Expression);
            case SyntaxKind.GiveExpression:
                NotSupported(syntax.Keyword, "'give'");
                return null;
        }
        if (valueMatch is not null && !IsStatementValue(valueMatch))
        {
            NotSupported(syntax.Keyword, $"'{syntax.Keyword.Text}' in an arm of a 'match' inside a larger expression");
            return null;
        }
        if (syntax.Kind == SyntaxKind.ReturnExpression)
        {
            return BindReturn(syntax, syntax.Keyword, syntax.Expression);
        }
        if (_loopDepth == 0)
        {
            Report(DiagnosticDescriptors.JumpOutsideLoop, syntax, syntax.Keyword.Text);
        }
        return syntax.Kind == SyntaxKind.BreakExpression ? new BoundBreak(syntax) : new BoundContinue(syntax);
    }

    /// <summary>
    /// <c>match</c> as a value: each arm is a value or a leave. The type is the one expected when every arm converts
    /// to it, otherwise the arms' common type; arms of numbers alone follow it (ADR-0013: a literal follows the typed
    /// values next to it).
    /// </summary>
    private BoundExpression BindMatchExpression(MatchExpressionSyntax syntax, TypeSymbol? expected)
    {
        var value = BindValue(syntax.Expression);
        var start = _flow;
        FlowState? end = null;
        var parts = new List<(MatchArmSyntax Syntax, BoundPattern Pattern, BoundExpression? When, BoundStatement? Jump, BoundExpression? Value)>();
        foreach (var armSyntax in syntax.Arms)
        {
            _flow = start;
            PushScope();
            var (pattern, when) = BindArmHead(armSyntax, value);
            BoundStatement? jump = null;
            BoundExpression? armValue = null;
            switch (armSyntax.Body)
            {
                case JumpExpressionSyntax jumpSyntax:
                    jump = BindArmJump(jumpSyntax, syntax);
                    armValue = jump is null ? Bad(jumpSyntax) : null;
                    break;
                case ExpressionSyntax expression when IsLiteralOnly(expression):
                    // Bound once the type is known.
                    end = Join(end, _flow);
                    break;
                case ExpressionSyntax expression:
                    armValue = BindValue(expression, expected);
                    end = Join(end, _flow);
                    break;
                default:
                    // A block or a statement, reported by the parser (NYX1229).
                    armValue = Bad(armSyntax.Body);
                    break;
            }
            PopScope();
            parts.Add((armSyntax, pattern, when, jump, armValue));
            start = AfterArm(start, value, pattern, when);
        }

        var type = MatchType(syntax, parts.Select(p => p.Value).OfType<BoundExpression>().ToList(), expected);
        var literals = new Dictionary<MatchArmSyntax, BoundExpression>();
        if (type is null)
        {
            // Only numbers: they have the type expected, or their own (int, double), as an operation of literals.
            foreach (var part in parts.Where(p => p.Jump is null && p.Value is null))
            {
                literals[part.Syntax] = BindValue((ExpressionSyntax)part.Syntax.Body, expected);
            }
            type = MatchType(syntax, [.. literals.Values], expected) ?? ReportNoArmValue(syntax, expected);
        }
        var arms = ImmutableArray.CreateBuilder<BoundMatchArm>(parts.Count);
        var reported = false;
        foreach (var (armSyntax, pattern, when, jump, armValue) in parts)
        {
            var body = armSyntax.Body;
            var bound = jump is not null ? null
                : armValue ?? (literals.TryGetValue(armSyntax, out var literal) && (literal.HasErrors || ReferenceEquals(literal.Type, type))
                    ? literal
                    : BindValue((ExpressionSyntax)body, type));
            // A number where the other arms are text: the arms have no type in common, as in C#.
            if (bound is not null && armValue is null && expected is null && !bound.HasErrors && !type.IsError
                && Conversions.ClassifyImplicit(bound.Type, type) is null)
            {
                if (!reported)
                {
                    Report(DiagnosticDescriptors.NoCommonArmType, syntax.MatchKeyword, $"'{SymbolDisplay.FormatType(type)}', '{SymbolDisplay.FormatType(bound.Type)}'");
                    reported = true;
                }
                bound = Bad(body, bound);
            }
            arms.Add(new BoundMatchArm(armSyntax, pattern, when, jump, bound is null ? null : Convert(bound, type, body)));
        }
        CheckArms(syntax, value, arms);
        _flow = end ?? start;
        return new BoundMatchExpression(syntax, value, arms.ToImmutable(), type);
    }

    /// <summary>
    /// The type of a match from the values of its arms (<paramref name="values"/>; leaves left out): the expected type
    /// when all of them convert to it, otherwise the one of their types the others convert to. <c>null</c> makes it a
    /// <c>T?</c>. Null when there are no values, or they have no type in common (reported).
    /// </summary>
    private TypeSymbol? MatchType(MatchExpressionSyntax syntax, List<BoundExpression> values, TypeSymbol? expected)
    {
        if (values.Count == 0)
        {
            return null;
        }
        if (values.Any(v => v.HasErrors))
        {
            return expected ?? ErrorTypeSymbol.Instance;
        }
        if (expected is { IsError: false } && values.All(v => Conversions.ClassifyImplicit(v.Type, expected) is not null))
        {
            return expected;
        }
        var typed = values.Select(v => v.Type).Where(t => t is not NullTypeSymbol).ToList();
        var common = typed.FirstOrDefault(candidate => typed.All(t => Conversions.ClassifyImplicit(t, candidate) is not null));
        if (common is not null && typed.Count < values.Count && !common.IsNullable)
        {
            common = _context.Table.GetNullableType(common);
        }
        if (common is null)
        {
            var types = string.Join(", ", values.Select(v => $"'{SymbolDisplay.FormatType(v.Type)}'").Distinct());
            Report(DiagnosticDescriptors.NoCommonArmType, syntax.MatchKeyword, types);
            return ErrorTypeSymbol.Instance;
        }
        return common;
    }

    /// <summary>A match whose arms all leave: its type can only come from where it is used.</summary>
    private TypeSymbol ReportNoArmValue(MatchExpressionSyntax syntax, TypeSymbol? expected)
    {
        if (expected is not null)
        {
            return expected;
        }
        Report(DiagnosticDescriptors.NoCommonArmType, syntax.MatchKeyword, "no arm gives a value");
        return ErrorTypeSymbol.Instance;
    }

    /// <summary>
    /// Where the next arm starts (ADR-0048): once an arm without <c>when</c> has matched null (<c>case null</c>, also
    /// inside <c>or</c>), the value matched isn't null in the arms after it, nor after the match when it gets there
    /// through them, as after <c>if x == null { return }</c>. Only null: no type says "not a Slime".
    /// </summary>
    private static FlowState AfterArm(FlowState start, BoundExpression value, BoundPattern pattern, BoundExpression? when) =>
        when is null && value.Type.IsNullable && Alternatives(pattern).Any(p => p is BoundNullPattern)
            ? Narrow(start, value, value.Type.NonNullable)
            : start;

    /// <summary>
    /// The pattern and <c>when</c> of an arm, in a scope the caller pushed: the locals of a case's data are declared in
    /// it. <c>case is T</c> and <c>case not null</c> narrow the value matched when it is a local or parameter (ADR-0010),
    /// and <c>when</c> is bound where the pattern matched; the arm's body starts where <c>when</c> holds.
    /// </summary>
    private (BoundPattern Pattern, BoundExpression? When) BindArmHead(MatchArmSyntax arm, BoundExpression value)
    {
        var input = value.HasErrors ? ErrorTypeSymbol.Instance : value.Type;
        var pattern = BindPattern(arm.Pattern, input, topLevel: true);
        switch (pattern)
        {
            case BoundTypePattern typePattern:
                _flow = Narrow(_flow, value, NarrowedByIs(value.Type, typePattern.Type));
                break;
            case BoundNotNullPattern:
                _flow = Narrow(_flow, value, value.Type.NonNullable);
                break;
        }
        if (arm.WhenClause is not { } clause)
        {
            return (pattern, null);
        }
        var (condition, whenTrue, _) = BindCondition(clause.Condition);
        _flow = whenTrue;
        return (pattern, condition);
    }

    // Patterns -----------------------------------------------------------------------------------------------------

    private BoundPattern BindPattern(PatternSyntax syntax, TypeSymbol input, bool topLevel)
    {
        switch (syntax)
        {
            case DiscardPatternSyntax:
                return new BoundDiscardPattern(syntax);
            case ConstantPatternSyntax { Expression: var expression } when IsNullLiteral(expression):
                return CheckPatternNull(syntax, input, "case null") ? new BoundNullPattern(syntax) : new BoundBadPattern(syntax);
            case NotPatternSyntax { Pattern: ConstantPatternSyntax { Expression: var expression } } when IsNullLiteral(expression):
                return CheckPatternNull(syntax, input, "case not null") ? new BoundNotNullPattern(syntax) : new BoundBadPattern(syntax);
            case ConstantPatternSyntax constant:
                return BindConstantPattern(constant, input);
            case RelationalPatternSyntax relational:
                return BindRelationalPattern(relational, input);
            case RangePatternSyntax range:
                return BindRangePattern(range, input);
            case TypePatternSyntax type:
                return BindTypePattern(type, input, topLevel);
            case CasePatternSyntax casePattern:
                return BindCasePattern(casePattern, input, topLevel);
            case NotPatternSyntax not:
                var inner = BindPattern(not.Pattern, input, topLevel: false);
                return inner is BoundBadPattern ? inner : new BoundNotPattern(syntax, inner);
            case BinaryPatternSyntax binary:
                var left = BindPattern(binary.Left, input, topLevel: false);
                var right = BindPattern(binary.Right, input, topLevel: false);
                return left is BoundBadPattern || right is BoundBadPattern
                    ? new BoundBadPattern(syntax)
                    : new BoundBinaryPattern(syntax, binary.Kind == SyntaxKind.AndPattern, left, right);
            default:
                NotSupported(syntax, $"'{syntax}'");
                return new BoundBadPattern(syntax);
        }
    }

    /// <summary><c>case null</c> needs a value that can be null: a <c>T?</c> or a reference type (ADR-0041).</summary>
    private bool CheckPatternNull(SyntaxNode syntax, TypeSymbol input, string text)
    {
        if (input.IsError || input.IsNullable || !input.IsValueType)
        {
            return !input.IsError;
        }
        Report(DiagnosticDescriptors.NeverNull, syntax, SymbolDisplay.FormatType(input), text);
        return false;
    }

    /// <summary>
    /// <c>case 0</c>, <c>case "boss"</c>, <c>case Limits.Max</c>: a constant (ADR-0038) of the type matched, which also
    /// types a number. A case of the enum matched is written by its name alone (<c>case Fire</c>).
    /// </summary>
    private BoundPattern BindConstantPattern(ConstantPatternSyntax syntax, TypeSymbol input)
    {
        var expected = input.IsError ? null : input.NonNullable;
        var value = BindValue(syntax.Expression, expected);
        if (value.HasErrors || input.IsError)
        {
            return new BoundBadPattern(syntax);
        }
        if (syntax.Expression is MemberAccessExpressionSyntax { Name: var name } && ReferenceEquals(value.Type, expected)
            && (IsEnumCase(value) || value is BoundEnumCase))
        {
            Report(DiagnosticDescriptors.QualifiedCaseInMatch, syntax, name.Identifier.Text);
            return new BoundBadPattern(syntax);
        }
        if (value.ConstantValue is null)
        {
            Report(DiagnosticDescriptors.NotConstant, syntax.Expression, "A 'case' value", ConstantEvaluator.NotConstantReason(value));
            return new BoundBadPattern(syntax);
        }
        var converted = Convert(value, expected!, syntax.Expression);
        return converted.HasErrors ? new BoundBadPattern(syntax) : new BoundConstantPattern(syntax, converted);
    }

    /// <summary>A constant compared with <c>&lt;</c> and the like: numbers and characters, as C#'s relational patterns.</summary>
    private BoundExpression? BindOrderedConstant(ExpressionSyntax syntax, TypeSymbol input, SyntaxNode location, string operatorText)
    {
        var target = input.NonNullable;
        if (target.TypeKind == TypeKind.Enum)
        {
            NotSupported(location, "Comparing an enum in 'case'");
            return null;
        }
        if (!Conversions.IsNumeric(target.SpecialType))
        {
            Report(DiagnosticDescriptors.OperatorNotDefined, location, operatorText, SymbolDisplay.FormatType(input), SymbolDisplay.FormatType(input));
            return null;
        }
        var value = BindValue(syntax, target);
        if (value.HasErrors)
        {
            return null;
        }
        if (value.ConstantValue is null)
        {
            Report(DiagnosticDescriptors.NotConstant, syntax, "A 'case' value", ConstantEvaluator.NotConstantReason(value));
            return null;
        }
        var converted = Convert(value, target, syntax);
        return converted.HasErrors ? null : converted;
    }

    private BoundPattern BindRelationalPattern(RelationalPatternSyntax syntax, TypeSymbol input)
    {
        if (input.IsError)
        {
            return new BoundBadPattern(syntax);
        }
        var operatorKind = syntax.OperatorToken.Kind switch
        {
            SyntaxKind.LessThanToken => BinaryOperatorKind.Less,
            SyntaxKind.LessThanEqualsToken => BinaryOperatorKind.LessOrEqual,
            SyntaxKind.GreaterThanToken => BinaryOperatorKind.Greater,
            _ => BinaryOperatorKind.GreaterOrEqual,
        };
        return BindOrderedConstant(syntax.Expression, input, syntax.OperatorToken, syntax.OperatorToken.Text) is { } value
            ? new BoundRelationalPattern(syntax, operatorKind, value)
            : new BoundBadPattern(syntax);
    }

    private BoundPattern BindRangePattern(RangePatternSyntax syntax, TypeSymbol input)
    {
        if (input.IsError)
        {
            return new BoundBadPattern(syntax);
        }
        var lower = BindOrderedConstant(syntax.Lower, input, syntax.OperatorToken, syntax.OperatorToken.Text);
        var upper = lower is null ? null : BindOrderedConstant(syntax.Upper, input, syntax.OperatorToken, syntax.OperatorToken.Text);
        if (lower is null || upper is null)
        {
            return new BoundBadPattern(syntax);
        }
        var includesUpper = syntax.OperatorToken.Kind == SyntaxKind.DotDotDotToken;
        // Both are constants of the type matched, so of one .NET type.
        var order = ((IComparable)lower.ConstantValue!).CompareTo(upper.ConstantValue);
        if (order > 0 || (order == 0 && !includesUpper))
        {
            Report(DiagnosticDescriptors.ArmNeverReached, syntax, $"'{syntax}' contains no value");
            return new BoundBadPattern(syntax);
        }
        return new BoundRangePattern(syntax, lower, upper, includesUpper);
    }

    /// <summary><c>case is Enemy</c>: only as the whole pattern, where it can narrow (ADR-0010).</summary>
    private BoundPattern BindTypePattern(TypePatternSyntax syntax, TypeSymbol input, bool topLevel)
    {
        if (!topLevel)
        {
            NotSupported(syntax, "'is' inside 'and', 'or' or 'not'");
            return new BoundBadPattern(syntax);
        }
        if (syntax.Type is NullableTypeSyntax nullable)
        {
            Report(DiagnosticDescriptors.IsNullableType, syntax.Type, nullable.ElementType.ToString());
            return new BoundBadPattern(syntax);
        }
        var type = _scope.BindType(syntax.Type);
        if (type.IsError || input.IsError)
        {
            return new BoundBadPattern(syntax);
        }
        if (!Conversions.CanBe(input, type))
        {
            Report(DiagnosticDescriptors.IsNeverTrue, syntax, SymbolDisplay.FormatType(input), SymbolDisplay.FormatType(type));
            return new BoundBadPattern(syntax);
        }
        return new BoundTypePattern(syntax, type);
    }

    /// <summary>
    /// <c>case Fire</c>, <c>case Burn(amount, _)</c>: a case of the enum matched, by its name alone (ADR-0024, ADR-0048).
    /// The data goes into new <c>let</c> locals by position, as in a deconstruction (ADR-0021); <c>_</c> leaves an item out.
    /// </summary>
    private BoundPattern BindCasePattern(CasePatternSyntax syntax, TypeSymbol input, bool topLevel)
    {
        var name = syntax.Identifier.Text;
        var enumType = input.NonNullable as NamedTypeSymbol;
        if (input.IsError || syntax.Identifier.IsMissing)
        {
            DeclareCaseData(syntax, null);
            return new BoundBadPattern(syntax);
        }
        if (enumType is null || !IsMatchedEnum(enumType))
        {
            Report(DiagnosticDescriptors.NotACase, syntax.Identifier, name, SymbolDisplay.FormatType(input),
                ": only an enum's cases are written by name alone; a constant is written with its type ('case Limits.Max'), a type check with 'is' ('case is Enemy')");
            DeclareCaseData(syntax, null);
            return new BoundBadPattern(syntax);
        }
        var field = enumType.GetMembers(name).OfType<FieldSymbol>().FirstOrDefault(f => ReferenceEquals(f.Type, enumType));
        if (field is null)
        {
            var cases = string.Join(", ", CasesOf(enumType).Select(c => c.Name));
            Report(DiagnosticDescriptors.NotACase, syntax.Identifier, name, SymbolDisplay.FormatType(enumType), $": its cases are {cases}");
            DeclareCaseData(syntax, null);
            return new BoundBadPattern(syntax);
        }
        if (!CheckAccessible(field, syntax.Identifier))
        {
            DeclareCaseData(syntax, null);
            return new BoundBadPattern(syntax);
        }
        if (!IsCaseName(field))
        {
            Report(DiagnosticDescriptors.NotConstant, syntax.Identifier, "A 'case' value", ConstantEvaluator.NotConstantReason(new BoundFieldAccess(syntax.Identifier, null, field)));
            DeclareCaseData(syntax, null);
            return new BoundBadPattern(syntax);
        }
        if (syntax.Fields is not { } fields)
        {
            return new BoundCasePattern(syntax, field, []);
        }
        if (field is not SourceEnumCaseSymbol { HasData: true } dataCase)
        {
            Report(DiagnosticDescriptors.CaseHasNoData, syntax, name);
            DeclareCaseData(syntax, null);
            return new BoundBadPattern(syntax);
        }
        if (!topLevel)
        {
            NotSupported(fields, "Taking out a case's data inside 'and', 'or' or 'not'");
            DeclareCaseData(syntax, null);
            return new BoundBadPattern(syntax);
        }
        if (fields.Fields.Count != dataCase.Items.Length)
        {
            var items = dataCase.Items.Length == 1 ? "1 data item" : $"{dataCase.Items.Length} data items";
            var names = string.Join(", ", dataCase.Items.Select(i => i.Name));
            var example = $"{name}({string.Join(", ", dataCase.Items.Select(i => Naming.CamelCase(i.Name)))})";
            Report(DiagnosticDescriptors.CaseDataCount, fields, name, $"{items} ({names})", example);
            DeclareCaseData(syntax, dataCase);
            return new BoundBadPattern(syntax);
        }
        return new BoundCasePattern(syntax, field, DeclareCaseData(syntax, dataCase));
    }

    /// <summary>
    /// The locals of a case pattern's data, typed by the case's items by position (the error type past them, or when
    /// <paramref name="dataCase"/> is null), so that the arm still binds after an error.
    /// </summary>
    private ImmutableArray<LocalSymbol?> DeclareCaseData(CasePatternSyntax syntax, SourceEnumCaseSymbol? dataCase)
    {
        if (syntax.Fields is not { } fields)
        {
            return [];
        }
        var locals = ImmutableArray.CreateBuilder<LocalSymbol?>(fields.Fields.Count);
        for (var i = 0; i < fields.Fields.Count; i++)
        {
            var identifier = fields.Fields[i].Identifier;
            if (identifier.Kind == SyntaxKind.UnderscoreToken)
            {
                locals.Add(null);
                continue;
            }
            var type = dataCase is not null && i < dataCase.Items.Length ? dataCase.Items[i].Type : ErrorTypeSymbol.Instance;
            locals.Add(DeclareLocal(identifier, type, isReadOnly: true));
        }
        return locals.MoveToImmutable();
    }

    /// <summary>An enum whose cases a match can list: Nyxel's four kinds and .NET's.</summary>
    private static bool IsMatchedEnum(TypeSymbol type) => type.TypeKind == TypeKind.Enum || type is SourceNamedTypeSymbol { IsDataEnum: true };

    /// <summary>The cases of an enum in order: for a .NET enum, its constants (an alias is one of them).</summary>
    private static IEnumerable<FieldSymbol> CasesOf(NamedTypeSymbol enumType) => enumType is SourceNamedTypeSymbol source
        ? source.Cases
        : enumType.GetMembers().OfType<FieldSymbol>().Where(f => f.IsConst && ReferenceEquals(f.Type, enumType));

    /// <summary>A flags enum: Nyxel's <c>flags enum</c>, or a .NET enum marked <c>[Flags]</c> (ADR-0024).</summary>
    private static bool IsFlagsEnum(TypeSymbol type) => type switch
    {
        SourceNamedTypeSymbol source => source.IsFlags,
        MetadataNamedTypeSymbol { TypeKind: TypeKind.Enum } metadata =>
            metadata.RoslynSymbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute"),
        _ => false,
    };

    // Which values the arms cover ------------------------------------------------------------------------------------

    /// <summary>
    /// The values a pattern C# can see stands for: C# reports an arm that its earlier arms already cover (CS8510,
    /// CS8120), so every such arm is reported here first. Comparisons, ranges, <c>and</c> and <c>not</c> are lowered
    /// into <c>when</c>, which C# doesn't look into: they cover nothing here, as there.
    /// </summary>
    private sealed record Alternative(SyntaxNode Syntax, AlternativeKind Kind, object? Key = null, TypeSymbol? Type = null);

    private enum AlternativeKind
    {
        Everything,
        Null,
        NonNull,
        Value,
        Type,
        Opaque,
    }

    /// <summary>A constant as the arms compare it: by its type and value, so <c>Gate</c> is <c>Door</c> (ADR-0045).</summary>
    private readonly record struct ValueKey(TypeSymbol Type, object Value)
    {
        public bool Equals(ValueKey other) => ReferenceEquals(Type, other.Type) && Equals(Value, other.Value);

        public override int GetHashCode() => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Type), Value);
    }

    private sealed class Coverage
    {
        public (int Line, string Text)? Null;
        public (int Line, string Text)? NonNull;
        public readonly Dictionary<ValueKey, (int Line, string Text)> Values = [];
        public readonly List<(TypeSymbol Type, int Line, string Text)> Types = [];
    }

    /// <summary>The parts of a pattern that matching any one of is enough: the sides of <c>or</c>, when C# sees them.</summary>
    private static IEnumerable<BoundPattern> Alternatives(BoundPattern pattern) =>
        pattern is BoundBinaryPattern { IsAnd: false } or && IsVisiblePattern(pattern)
            ? Alternatives(or.Left).Concat(Alternatives(or.Right))
            : [pattern];

    /// <summary>
    /// Whether lowering writes the pattern as a C# pattern, which C# then checks against the arms before it; the
    /// others go into a <c>when</c> (docs/design/lowering.md, "match").
    /// </summary>
    internal static bool IsVisiblePattern(BoundPattern pattern) => pattern switch
    {
        BoundDiscardPattern or BoundNullPattern or BoundNotNullPattern or BoundConstantPattern or BoundCasePattern or BoundTypePattern => true,
        BoundBinaryPattern { IsAnd: false } or => IsVisiblePattern(or.Left) && IsVisiblePattern(or.Right),
        _ => false,
    };

    private static Alternative AlternativeOf(BoundPattern pattern, TypeSymbol input) => pattern switch
    {
        _ when !IsVisiblePattern(pattern) => new(pattern.Syntax, AlternativeKind.Opaque),
        BoundDiscardPattern => new(pattern.Syntax, AlternativeKind.Everything),
        BoundNullPattern => new(pattern.Syntax, AlternativeKind.Null),
        BoundNotNullPattern => new(pattern.Syntax, AlternativeKind.NonNull),
        // 'is T' of a type the value always is: every value but null.
        BoundTypePattern typePattern when IsSubtype(input.NonNullable, typePattern.Type) => new(pattern.Syntax, AlternativeKind.NonNull),
        BoundTypePattern typePattern => new(pattern.Syntax, AlternativeKind.Type, Type: typePattern.Type),
        BoundCasePattern { Case: SourceEnumCaseSymbol { ContainingType.IsDataEnum: true } dataCase } => new(pattern.Syntax, AlternativeKind.Value, dataCase, dataCase.ContainingType),
        BoundCasePattern casePattern => new(pattern.Syntax, AlternativeKind.Value, casePattern.Case.ConstantValue, casePattern.Case.ContainingType),
        BoundConstantPattern { Value: BoundConversion { ConversionKind: ConversionKind.Boxing or ConversionKind.ImplicitReference } conversion } =>
            new(pattern.Syntax, AlternativeKind.Value, conversion.Operand.ConstantValue, conversion.Operand.Type),
        BoundConstantPattern constant => new(pattern.Syntax, AlternativeKind.Value, constant.Value.ConstantValue, constant.Value.Type),
        _ => new(pattern.Syntax, AlternativeKind.Opaque),
    };

    /// <summary>A value of <paramref name="type"/> is always a <paramref name="of"/>: the same type, a base type or interface, or <c>object</c>.</summary>
    private static bool IsSubtype(TypeSymbol type, TypeSymbol of) =>
        Conversions.ClassifyImplicit(type, of) is ConversionKind.Identity or ConversionKind.ImplicitReference or ConversionKind.Boxing;

    /// <summary>
    /// Follows the arms in order (ADR-0009): an arm that the arms before it (those without <c>when</c>) already cover
    /// is never reached, an error as in C#; so is a value listed twice inside one arm, or an alias with its case
    /// (ADR-0040). At the end every value has to be covered: each case of an enum, or <c>case _</c>; <c>null</c> too
    /// for a <c>T?</c>.
    /// </summary>
    private void CheckArms(MatchExpressionSyntax syntax, BoundExpression value, ImmutableArray<BoundMatchArm>.Builder arms)
    {
        if (value.HasErrors || arms.Any(a => a.Pattern is BoundBadPattern))
        {
            return;
        }
        var input = value.Type;
        var coverage = new Coverage();
        foreach (var arm in arms)
        {
            var line = LineOf(arm.Syntax);
            var alternatives = Alternatives(arm.Pattern).Select(p => AlternativeOf(p, input)).ToList();
            var reasons = alternatives.Select(a => NeverReached(a, coverage, input)).ToList();
            if (reasons.All(r => r is not null))
            {
                Report(DiagnosticDescriptors.ArmNeverReached, arm.Pattern.Syntax, reasons[0]!);
                continue;
            }
            // Within one arm: 'case Fire or Fire', 'case Enter or Return' of a .NET enum with an alias.
            var within = new Coverage();
            foreach (var alternative in alternatives)
            {
                if (NeverReached(alternative, within, input) is { } reason && alternatives.Count > 1)
                {
                    Report(DiagnosticDescriptors.ArmNeverReached, alternative.Syntax, reason);
                }
                Add(within, alternative, line, input);
            }
            if (arm.When is null)
            {
                foreach (var alternative in alternatives)
                {
                    Add(coverage, alternative, line, input);
                }
            }
        }
        if (MissingValues(coverage, input) is { } missing)
        {
            Report(DiagnosticDescriptors.MatchNotExhaustive, syntax.MatchKeyword, SymbolDisplay.FormatType(input), missing);
        }
    }

    private int LineOf(SyntaxNode node) => Tree.Text.GetLinePosition(node.Span.Start).Line + 1;

    /// <summary>Why no value reaches the alternative after what <paramref name="coverage"/> covers; null when one does.</summary>
    private static string? NeverReached(Alternative alternative, Coverage coverage, TypeSymbol input)
    {
        // C# lets null through to a value of a reference type, even one declared without '?' (C# code may pass it).
        var nullPossible = input.IsNullable || !input.IsValueType;
        var everyValue = coverage.NonNull is not { } nonNull ? null
            : nonNull.Text.Length == 0 ? "the arms before it already match every value"
            : $"'case {nonNull.Text}' on line {nonNull.Line} already matches every value{(nullPossible && coverage.Null is null ? " but null" : "")}";
        switch (alternative.Kind)
        {
            case AlternativeKind.Everything or AlternativeKind.Opaque:
                if (coverage.NonNull is null || (nullPossible && coverage.Null is null))
                {
                    return null;
                }
                return coverage.Null is { } both && both == coverage.NonNull ? everyValue : "the arms before it already match every value";
            case AlternativeKind.Null:
                return coverage.Null is { } covered ? $"'case {covered.Text}' on line {covered.Line} already matches null" : null;
            case AlternativeKind.NonNull:
                return everyValue;
            case AlternativeKind.Value:
                if (everyValue is not null)
                {
                    return everyValue;
                }
                if (coverage.Values.TryGetValue(new ValueKey(alternative.Type!, alternative.Key!), out var same))
                {
                    var text = alternative.Syntax.ToString();
                    return same.Text == text
                        ? $"'{text}' is matched on line {same.Line} already"
                        : $"'{text}' is the same value as '{same.Text}' on line {same.Line}";
                }
                return CoveringType(coverage, alternative.Type!) is { } typeCovered
                    ? $"'case {typeCovered.Text}' on line {typeCovered.Line} already matches every '{SymbolDisplay.FormatType(alternative.Type!)}'"
                    : null;
            default:
                if (everyValue is not null)
                {
                    return everyValue;
                }
                return CoveringType(coverage, alternative.Type!) is { } covering
                    ? $"'case {covering.Text}' on line {covering.Line} already matches every '{SymbolDisplay.FormatType(alternative.Type!)}'"
                    : null;
        }
    }

    private static (TypeSymbol Type, int Line, string Text)? CoveringType(Coverage coverage, TypeSymbol type) =>
        coverage.Types.Where(t => IsSubtype(type, t.Type)).Select(t => ((TypeSymbol, int, string)?)t).FirstOrDefault();

    private static void Add(Coverage coverage, Alternative alternative, int line, TypeSymbol input)
    {
        var text = alternative.Syntax.ToString();
        switch (alternative.Kind)
        {
            case AlternativeKind.Everything:
                coverage.Null ??= (line, text);
                coverage.NonNull ??= (line, text);
                break;
            case AlternativeKind.Null:
                coverage.Null ??= (line, text);
                break;
            case AlternativeKind.NonNull:
                coverage.NonNull ??= (line, text);
                break;
            case AlternativeKind.Value:
                coverage.Values.TryAdd(new ValueKey(alternative.Type!, alternative.Key!), (line, text));
                // A bool, or a struct enum or class enum, has nothing past its cases.
                if (coverage.NonNull is null && FiniteValues(input.NonNullable) is { } all && all.All(k => coverage.Values.ContainsKey(k)))
                {
                    coverage.NonNull = (line, "");
                }
                break;
            case AlternativeKind.Type:
                coverage.Types.Add((alternative.Type!, line, text));
                break;
        }
    }

    /// <summary>Every value of a type that has no values past those it names: <c>bool</c>, and the cases of a <c>struct enum</c> / <c>class enum</c>.</summary>
    private static List<ValueKey>? FiniteValues(TypeSymbol type) => type switch
    {
        { SpecialType: SpecialType.System_Boolean } => [new(type, true), new(type, false)],
        SourceNamedTypeSymbol { IsDataEnum: true } dataEnum => [.. dataEnum.Cases.Select(c => new ValueKey(dataEnum, c))],
        _ => null,
    };

    /// <summary>What the arms leave out, as the end of NYX2091's message; null when they cover every value.</summary>
    private static string? MissingValues(Coverage coverage, TypeSymbol input)
    {
        var missing = new List<string>();
        var type = input.NonNullable;
        var finite = true;
        if (coverage.NonNull is null)
        {
            switch (type)
            {
                case { SpecialType: SpecialType.System_Boolean }:
                    missing.AddRange(FiniteValues(type)!.Where(k => !coverage.Values.ContainsKey(k)).Select(k => (bool)k.Value ? "'case true'" : "'case false'"));
                    break;
                case SourceNamedTypeSymbol { IsDataEnum: true } dataEnum:
                    missing.AddRange(dataEnum.Cases.Where(c => !coverage.Values.ContainsKey(new ValueKey(dataEnum, c))).Select(c => $"'case {c.Name}'"));
                    break;
                case NamedTypeSymbol { TypeKind: TypeKind.Enum } enumType when !IsFlagsEnum(enumType):
                    // Every value named once (an alias of .NET's is the same value); one named by no case throws (ADR-0024).
                    missing.AddRange(CasesOf(enumType)
                        .Where(c => c.ConstantValue is not null)
                        .DistinctBy(c => c.ConstantValue)
                        .Where(c => !coverage.Values.ContainsKey(new ValueKey(enumType, c.ConstantValue!)))
                        .Select(c => $"'case {c.Name}'"));
                    break;
                default:
                    finite = false;
                    break;
            }
        }
        if (!finite)
        {
            var rest = IsFlagsEnum(type)
                ? "add 'case _' (a flags enum's values combine, so they can't all be listed)"
                : "add 'case _' for the values not listed";
            return rest;
        }
        if (input.IsNullable && coverage.Null is null)
        {
            missing.Add("'case null'");
        }
        if (missing.Count == 0)
        {
            return null;
        }
        var list = missing.Count == 1 ? missing[0] : $"{string.Join(", ", missing.Take(missing.Count - 1))} and {missing[^1]}";
        return $"add {list}, or 'case _' for the rest";
    }
}
