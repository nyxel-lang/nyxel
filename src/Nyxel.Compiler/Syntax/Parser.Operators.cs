using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Syntax;

// Operators and conversions (ADR-0030).
internal sealed partial class Parser
{
    // Message fragments. Not in DiagnosticDescriptors: its catalog test reads every public static field there.
    private const string StaticOperatorNote = "; it has no 'self', so it is static already";
    private const string ImplicitConversionNote = "; Nyxel has no implicit conversions";

    private enum OperatorArity
    {
        Unary,
        Binary,
        UnaryOrBinary,
    }

    /// <summary>
    /// <c>public operator +(a: Cell, b: Cell) -&gt; Cell = ...</c> or a conversion <c>public operator as(value: int)
    /// -&gt; Fixed = ...</c>. Checks what the declaration alone shows: where it is declared, 'public' and no other
    /// modifier, that the operator can be declared, the number of parameters and no 'self', the return type, the
    /// body.
    /// </summary>
    private OperatorDeclarationSyntax ParseOperatorDeclaration(SyntaxList<SyntaxToken> modifiers, MemberContext context)
    {
        var operatorKeyword = NextToken();
        var operatorToken = ParseOperatorToken();
        var isConversion = operatorToken.Kind == SyntaxKind.AsKeyword;
        var misplaced = ReportOperatorPlacement(operatorKeyword, isConversion, context);
        if (!misplaced)
        {
            CheckOperatorModifiers(modifiers, operatorKeyword, operatorToken);
        }
        var arity = GetDeclarableArity(operatorToken.Kind);
        if (arity == null && !operatorToken.IsMissing)
        {
            ReportUndeclarableOperator(operatorToken.Kind, operatorToken.Text, operatorToken.Span);
        }
        var parameters = ParseParameterList(ParameterContext.Operator);
        if (arity is { } expected && !parameters.CloseParenToken.IsMissing)
        {
            CheckOperatorParameterCount(operatorToken, expected, parameters);
        }
        var returnType = TryParseReturnType();
        if (returnType == null)
        {
            Report(DiagnosticDescriptors.OperatorReturnType, new TextSpan(_previousTokenEnd, 0), operatorToken.Text);
        }
        // A misplaced one (in an interface, say) is reported once, without asking for a body too.
        var (body, expressionBody) = ParseFunctionBody(required: !misplaced);
        var kind = isConversion ? SyntaxKind.ConversionOperatorDeclaration : SyntaxKind.OperatorDeclaration;
        return new OperatorDeclarationSyntax(kind, modifiers, operatorKeyword, operatorToken, parameters, returnType, body, expressionBody);
    }

    /// <summary>
    /// Operators go in a class, a struct or an extension; conversions not in an extension (C# 14 doesn't support
    /// them there). At the top level the caller reports it. True when it is misplaced.
    /// </summary>
    private bool ReportOperatorPlacement(SyntaxToken operatorKeyword, bool isConversion, MemberContext context)
    {
        var what = isConversion ? "Conversions" : "Operators";
        var where = context switch
        {
            MemberContext.Interface => "an interface",
            MemberContext.Enum => "an enum yet",
            MemberContext.Extension when isConversion => "an extension; C# 14 extensions don't support them",
            _ => null,
        };
        if (where != null)
        {
            Report(DiagnosticDescriptors.OperatorPlacement, operatorKeyword.Span, what, where);
        }
        return where != null || context == MemberContext.TopLevel;
    }

    /// <summary>An operator is always public and static: it is written 'public' and nothing else.</summary>
    private void CheckOperatorModifiers(SyntaxList<SyntaxToken> modifiers, SyntaxToken operatorKeyword, SyntaxToken operatorToken)
    {
        // 'private' was reported with the modifiers (ADR-0026).
        foreach (var modifier in modifiers.Where(m => m.Kind is not (SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword)))
        {
            Report(DiagnosticDescriptors.OperatorModifier, modifier.Span, modifier.Text,
                modifier.Kind == SyntaxKind.StaticKeyword ? StaticOperatorNote : "");
        }
        if (!modifiers.Any(m => m.Kind == SyntaxKind.PublicKeyword))
        {
            Report(DiagnosticDescriptors.OperatorNotPublic, operatorKeyword.Span, operatorToken.IsMissing ? "+" : operatorToken.Text);
        }
    }

    /// <summary>
    /// The operator after the <c>operator</c> keyword, or a missing one. Takes any operator, so that one which can't
    /// be declared is reported with the reason; <c>&gt;&gt;</c> is composed from two '&gt;' as in expressions.
    /// </summary>
    private SyntaxToken ParseOperatorToken()
    {
        if (AtLineBreak || !IsOperatorCandidate(Current.Kind))
        {
            ReportExpected("an operator");
            return Missing(SyntaxKind.PlusToken);
        }
        if (IsAdjacent(SyntaxKind.GreaterThanToken, SyntaxKind.GreaterThanEqualsToken))
        {
            return EatComposed(SyntaxKind.GreaterThanGreaterThanEqualsToken);
        }
        if (IsAdjacent(SyntaxKind.GreaterThanToken, SyntaxKind.GreaterThanToken))
        {
            return EatComposed(SyntaxKind.GreaterThanGreaterThanToken);
        }
        return NextToken();
    }

    private static bool IsOperatorCandidate(SyntaxKind kind) =>
        kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken or SyntaxKind.SlashToken
            or SyntaxKind.PercentToken or SyntaxKind.AmpersandToken or SyntaxKind.BarToken or SyntaxKind.CaretToken
            or SyntaxKind.TildeToken or SyntaxKind.LessThanLessThanToken or SyntaxKind.LessThanToken
            or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken
            or SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken or SyntaxKind.ExclamationToken
            or SyntaxKind.AmpersandAmpersandToken or SyntaxKind.BarBarToken or SyntaxKind.PlusPlusToken
            or SyntaxKind.MinusMinusToken or SyntaxKind.QuestionQuestionToken or SyntaxKind.AndKeyword
            or SyntaxKind.OrKeyword or SyntaxKind.NotKeyword or SyntaxKind.IsKeyword or SyntaxKind.AsKeyword
            or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword
        || SyntaxFacts.IsRangeOperator(kind)
        || (SyntaxFacts.IsAssignmentOperator(kind) && kind != SyntaxKind.EqualsToken);

    /// <summary>
    /// The operators that can be declared and how many operands each takes; null for the others. '==' and '!='
    /// come with every type, '&gt;', '&lt;=' and '&gt;=' are generated from '&lt;', and 'and', 'or' and 'not'
    /// would lose their short-circuiting as functions.
    /// </summary>
    private static OperatorArity? GetDeclarableArity(SyntaxKind kind) => kind switch
    {
        SyntaxKind.TildeToken or SyntaxKind.AsKeyword => OperatorArity.Unary,
        SyntaxKind.PlusToken or SyntaxKind.MinusToken => OperatorArity.UnaryOrBinary,
        SyntaxKind.AsteriskToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken or SyntaxKind.AmpersandToken
            or SyntaxKind.BarToken or SyntaxKind.CaretToken or SyntaxKind.LessThanLessThanToken
            or SyntaxKind.GreaterThanGreaterThanToken or SyntaxKind.LessThanToken => OperatorArity.Binary,
        _ => null,
    };

    private void ReportUndeclarableOperator(SyntaxKind kind, string text, TextSpan span)
    {
        switch (kind)
        {
            case SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken:
                Report(DiagnosticDescriptors.EqualityOperatorDeclaration, span, text);
                break;
            case SyntaxKind.GreaterThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken:
                Report(DiagnosticDescriptors.DerivedComparisonOperator, span);
                break;
            default:
                Report(DiagnosticDescriptors.OperatorNotDeclarable, span, text);
                break;
        }
    }

    private void CheckOperatorParameterCount(SyntaxToken operatorToken, OperatorArity arity, ParameterListSyntax parameters)
    {
        var count = parameters.Parameters.Count;
        var ok = arity switch
        {
            OperatorArity.Unary => count == 1,
            OperatorArity.Binary => count == 2,
            _ => count is 1 or 2,
        };
        if (ok)
        {
            return;
        }
        var text = operatorToken.Text;
        var takes = arity switch
        {
            OperatorArity.Unary when operatorToken.Kind == SyntaxKind.AsKeyword => "one parameter, the value to convert",
            OperatorArity.Unary => "one parameter",
            OperatorArity.Binary => "two parameters",
            _ => $"one parameter ('{text}x') or two ('a {text} b')",
        };
        Report(DiagnosticDescriptors.OperatorParameterCount, parameters.Span, text, takes);
    }

    // C# operators and conversions -----------------------------------------------------------------------------

    /// <summary>
    /// C#'s operator and conversion declarations, reported with the Nyxel way to write them and then skipped whole:
    /// <c>public static Cell operator +(Cell a, Cell b) =&gt; ...</c>, <c>public static explicit operator
    /// float(Fixed f) { ... }</c>. False, without reading anything, when the line isn't one.
    /// </summary>
    private bool TrySkipCSharpOperator(SyntaxList<SyntaxToken> modifiers)
    {
        if (AtLineBreak)
        {
            return false;
        }
        var conversionWord = Current.Kind == SyntaxKind.IdentifierToken && Current.Text is "implicit" or "explicit"
            && Peek(1).Kind == SyntaxKind.OperatorKeyword && !Peek(1).HasLeadingLineBreak;
        var operatorOffset = conversionWord ? 1 : FindOperatorKeywordAfterType();
        if (operatorOffset < 0)
        {
            return false;
        }

        if (conversionWord)
        {
            var (target, open) = TokenTextUntilOpenParen(operatorOffset + 1);
            var span = TextSpan.FromBounds(Current.Span.Start, Peek(operatorOffset).Span.End);
            var note = Current.Text == "implicit" ? ImplicitConversionNote : "";
            Report(DiagnosticDescriptors.CSharpConversionSyntax, span, CSharpParametersAsNyxel(open) ?? "value: Source", target, note);
        }
        else
        {
            var symbolOffset = operatorOffset + 1;
            var symbol = Peek(symbolOffset);
            var composedShift = symbol.Kind == SyntaxKind.GreaterThanToken && Peek(symbolOffset + 1).Kind == SyntaxKind.GreaterThanToken
                && Peek(symbolOffset + 1).Position == symbol.Span.End;
            var symbolKind = composedShift ? SyntaxKind.GreaterThanGreaterThanToken : symbol.Kind;
            var symbolText = composedShift ? ">>" : symbol.Text;
            var symbolSpan = composedShift ? TextSpan.FromBounds(symbol.Span.Start, Peek(symbolOffset + 1).Span.End) : symbol.Span;
            if (GetDeclarableArity(symbolKind) != null)
            {
                var returnType = TokenTextBetween(0, operatorOffset);
                var open = symbolOffset + (composedShift ? 2 : 1);
                var span = TextSpan.FromBounds(Current.Span.Start, symbolSpan.End);
                Report(DiagnosticDescriptors.CSharpOperatorSyntax, span, symbolText,
                    CSharpParametersAsNyxel(open) ?? "name: Type, ...", returnType);
            }
            else if (IsOperatorCandidate(symbolKind) && !symbol.HasLeadingLineBreak)
            {
                ReportUndeclarableOperator(symbolKind, symbolText, symbolSpan);
            }
            else
            {
                return false;
            }
        }

        SkipConsumed(modifiers);
        SkipToEndOfLine();
        // A C# body often starts on the next line.
        if (Current.Kind == SyntaxKind.OpenBraceToken)
        {
            SkipBalanced();
        }
        return true;
    }

    /// <summary>
    /// The offset of an <c>operator</c> keyword after a C# return type on this line (<c>Cell operator</c>,
    /// <c>List&lt;int&gt; operator</c>), or -1.
    /// </summary>
    private int FindOperatorKeywordAfterType()
    {
        if (!CanStartTypeName(Current.Kind))
        {
            return -1;
        }
        for (var offset = 1; !Peek(offset).HasLeadingLineBreak; offset++)
        {
            var kind = Peek(offset).Kind;
            if (kind == SyntaxKind.OperatorKeyword)
            {
                return offset;
            }
            if (!CanStartTypeName(kind) && kind is not (SyntaxKind.DotToken or SyntaxKind.LessThanToken
                or SyntaxKind.GreaterThanToken or SyntaxKind.CommaToken or SyntaxKind.QuestionToken
                or SyntaxKind.OpenBracketToken or SyntaxKind.CloseBracketToken))
            {
                return -1;
            }
        }
        return -1;
    }

    private static bool CanStartTypeName(SyntaxKind kind) =>
        kind == SyntaxKind.IdentifierToken || SyntaxFacts.IsPredefinedType(kind) || SyntaxFacts.IsArrayKeyword(kind);

    /// <summary>The text of the tokens from <paramref name="start"/> up to a '(' on the line, and the '(' offset.</summary>
    private (string Text, int OpenParen) TokenTextUntilOpenParen(int start)
    {
        var end = start;
        while (Peek(end).Kind is not (SyntaxKind.OpenParenToken or SyntaxKind.EndOfFileToken) && !Peek(end).HasLeadingLineBreak)
        {
            end++;
        }
        return (TokenTextBetween(start, end), end);
    }

    private string TokenTextBetween(int start, int end) =>
        string.Concat(Enumerable.Range(start, end - start).Select(i => Peek(i).Kind == SyntaxKind.CommaToken ? ", " : Peek(i).Text));

    /// <summary>
    /// C# parameters <c>(Cell a, Cell b)</c> at <paramref name="openParen"/> as Nyxel's <c>a: Cell, b: Cell</c>, or
    /// null unless each one is a one-word type and a name.
    /// </summary>
    private string? CSharpParametersAsNyxel(int openParen)
    {
        if (Peek(openParen).Kind != SyntaxKind.OpenParenToken)
        {
            return null;
        }
        var parameters = new List<string>();
        for (var offset = openParen + 1; ; offset += 3)
        {
            var type = Peek(offset);
            var name = Peek(offset + 1);
            if (!CanStartTypeName(type.Kind) || name.Kind != SyntaxKind.IdentifierToken)
            {
                return null;
            }
            parameters.Add($"{name.Text}: {type.Text}");
            switch (Peek(offset + 2).Kind)
            {
                case SyntaxKind.CommaToken:
                    continue;
                case SyntaxKind.CloseParenToken:
                    return string.Join(", ", parameters);
                default:
                    return null;
            }
        }
    }

    // Members generated for every type (ADR-0030) --------------------------------------------------------------

    /// <summary>
    /// A struct or an enum compares its contents with generated <c>Equals</c> and <c>GetHashCode</c>; a type with
    /// <c>operator &lt;</c> gets a generated <c>CompareTo</c>. Declaring them too is reported.
    /// </summary>
    private void CheckGeneratedMembers(string typeKind, SyntaxList<MemberDeclarationSyntax> members)
    {
        var comparable = members.Any(m => m is OperatorDeclarationSyntax { OperatorToken.Kind: SyntaxKind.LessThanToken });
        foreach (var function in members.OfType<FunctionDeclarationSyntax>())
        {
            var name = function.Identifier.Text;
            if (typeKind != "class" && name is "Equals" or "GetHashCode")
            {
                Report(DiagnosticDescriptors.GeneratedEqualityMember, function.Identifier.Span, typeKind, name);
            }
            else if (comparable && name == "CompareTo")
            {
                Report(DiagnosticDescriptors.GeneratedCompareTo, function.Identifier.Span);
            }
        }
    }
}
