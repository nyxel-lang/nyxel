using System.Collections.Frozen;

namespace Nyxel.Compiler.Syntax;

/// <summary>Static knowledge about tokens: keyword spellings, operator precedence, modifier order.</summary>
public static class SyntaxFacts
{
    private static readonly FrozenDictionary<string, SyntaxKind> Keywords = new Dictionary<string, SyntaxKind>
    {
        ["let"] = SyntaxKind.LetKeyword,
        ["var"] = SyntaxKind.VarKeyword,
        ["func"] = SyntaxKind.FuncKeyword,
        ["init"] = SyntaxKind.InitKeyword,
        ["property"] = SyntaxKind.PropertyKeyword,
        ["event"] = SyntaxKind.EventKeyword,
        ["class"] = SyntaxKind.ClassKeyword,
        ["struct"] = SyntaxKind.StructKeyword,
        ["interface"] = SyntaxKind.InterfaceKeyword,
        ["enum"] = SyntaxKind.EnumKeyword,
        ["case"] = SyntaxKind.CaseKeyword,
        ["extension"] = SyntaxKind.ExtensionKeyword,
        ["namespace"] = SyntaxKind.NamespaceKeyword,
        ["import"] = SyntaxKind.ImportKeyword,
        ["public"] = SyntaxKind.PublicKeyword,
        ["private"] = SyntaxKind.PrivateKeyword,
        ["protected"] = SyntaxKind.ProtectedKeyword,
        ["static"] = SyntaxKind.StaticKeyword,
        ["virtual"] = SyntaxKind.VirtualKeyword,
        ["override"] = SyntaxKind.OverrideKeyword,
        ["abstract"] = SyntaxKind.AbstractKeyword,
        ["sealed"] = SyntaxKind.SealedKeyword,
        ["async"] = SyntaxKind.AsyncKeyword,
        ["await"] = SyntaxKind.AwaitKeyword,
        ["launch"] = SyntaxKind.LaunchKeyword,
        ["emit"] = SyntaxKind.EmitKeyword,
        ["if"] = SyntaxKind.IfKeyword,
        ["else"] = SyntaxKind.ElseKeyword,
        ["match"] = SyntaxKind.MatchKeyword,
        ["when"] = SyntaxKind.WhenKeyword,
        ["for"] = SyntaxKind.ForKeyword,
        ["in"] = SyntaxKind.InKeyword,
        ["while"] = SyntaxKind.WhileKeyword,
        ["break"] = SyntaxKind.BreakKeyword,
        ["continue"] = SyntaxKind.ContinueKeyword,
        ["return"] = SyntaxKind.ReturnKeyword,
        ["throw"] = SyntaxKind.ThrowKeyword,
        ["try"] = SyntaxKind.TryKeyword,
        ["catch"] = SyntaxKind.CatchKeyword,
        ["finally"] = SyntaxKind.FinallyKeyword,
        ["using"] = SyntaxKind.UsingKeyword,
        ["new"] = SyntaxKind.NewKeyword,
        ["self"] = SyntaxKind.SelfKeyword,
        ["super"] = SyntaxKind.SuperKeyword,
        ["true"] = SyntaxKind.TrueKeyword,
        ["false"] = SyntaxKind.FalseKeyword,
        ["null"] = SyntaxKind.NullKeyword,
        ["and"] = SyntaxKind.AndKeyword,
        ["or"] = SyntaxKind.OrKeyword,
        ["not"] = SyntaxKind.NotKeyword,
        ["is"] = SyntaxKind.IsKeyword,
        ["as"] = SyntaxKind.AsKeyword,
        ["out"] = SyntaxKind.OutKeyword,
        ["ref"] = SyntaxKind.RefKeyword,
        ["bool"] = SyntaxKind.BoolKeyword,
        ["byte"] = SyntaxKind.ByteKeyword,
        ["sbyte"] = SyntaxKind.SByteKeyword,
        ["short"] = SyntaxKind.ShortKeyword,
        ["ushort"] = SyntaxKind.UShortKeyword,
        ["int"] = SyntaxKind.IntKeyword,
        ["uint"] = SyntaxKind.UIntKeyword,
        ["long"] = SyntaxKind.LongKeyword,
        ["ulong"] = SyntaxKind.ULongKeyword,
        ["float"] = SyntaxKind.FloatKeyword,
        ["double"] = SyntaxKind.DoubleKeyword,
        ["decimal"] = SyntaxKind.DecimalKeyword,
        ["char"] = SyntaxKind.CharKeyword,
        ["string"] = SyntaxKind.StringKeyword,
        ["object"] = SyntaxKind.ObjectKeyword,
        ["array"] = SyntaxKind.ArrayKeyword,
        ["array2d"] = SyntaxKind.Array2DKeyword,
        ["array3d"] = SyntaxKind.Array3DKeyword,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<SyntaxKind, string> KeywordTexts =
        Keywords.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>
    /// Words that are keywords only in one position (property accessors, just before <c>enum</c>) and identifiers
    /// everywhere else.
    /// </summary>
    public const string GetContextualKeyword = "get";

    public const string SetContextualKeyword = "set";

    public const string FlagsContextualKeyword = "flags";

    public static SyntaxKind GetKeywordKind(string text) =>
        Keywords.TryGetValue(text, out var kind) ? kind : SyntaxKind.IdentifierToken;

    public static IEnumerable<string> GetKeywordTexts() => Keywords.Keys;

    public static bool IsKeyword(SyntaxKind kind) => KeywordTexts.ContainsKey(kind);

    public static bool IsPredefinedType(SyntaxKind kind) =>
        kind is >= SyntaxKind.BoolKeyword and <= SyntaxKind.ObjectKeyword;

    /// <summary><c>byte</c> through <c>ulong</c>: the types an enum can have underneath (ADR-0024).</summary>
    public static bool IsIntegerType(SyntaxKind kind) =>
        kind is >= SyntaxKind.ByteKeyword and <= SyntaxKind.ULongKeyword;

    /// <summary><c>array</c>, <c>array2d</c> or <c>array3d</c> (ADR-0020, ADR-0022).</summary>
    public static bool IsArrayKeyword(SyntaxKind kind) =>
        kind is SyntaxKind.ArrayKeyword or SyntaxKind.Array2DKeyword or SyntaxKind.Array3DKeyword;

    /// <summary>The array keyword for a number of dimensions from 1 to 3.</summary>
    public static SyntaxKind GetArrayKeyword(int rank) => rank switch
    {
        2 => SyntaxKind.Array2DKeyword,
        3 => SyntaxKind.Array3DKeyword,
        _ => SyntaxKind.ArrayKeyword,
    };

    /// <summary>The range operators: <c>..&lt;</c>, <c>...</c>, and C#'s <c>..</c>, kept to be reported.</summary>
    public static bool IsRangeOperator(SyntaxKind kind) =>
        kind is SyntaxKind.DotDotLessThanToken or SyntaxKind.DotDotDotToken or SyntaxKind.DotDotToken;

    /// <summary>The fixed text of a token kind, or null for identifiers, literals and other variable-text tokens.</summary>
    public static string? GetText(SyntaxKind kind)
    {
        if (KeywordTexts.TryGetValue(kind, out var keyword))
        {
            return keyword;
        }
        return kind switch
        {
            SyntaxKind.OpenBraceToken => "{",
            SyntaxKind.CloseBraceToken => "}",
            SyntaxKind.OpenParenToken => "(",
            SyntaxKind.CloseParenToken => ")",
            SyntaxKind.OpenBracketToken => "[",
            SyntaxKind.CloseBracketToken => "]",
            SyntaxKind.CommaToken => ",",
            SyntaxKind.DotToken => ".",
            SyntaxKind.QuestionDotToken => "?.",
            SyntaxKind.ColonToken => ":",
            SyntaxKind.QuestionToken => "?",
            SyntaxKind.QuestionQuestionToken => "??",
            SyntaxKind.QuestionQuestionEqualsToken => "??=",
            SyntaxKind.MinusGreaterThanToken => "->",
            SyntaxKind.DotDotLessThanToken => "..<",
            SyntaxKind.DotDotDotToken => "...",
            SyntaxKind.DotDotToken => "..",
            SyntaxKind.EqualsToken => "=",
            SyntaxKind.EqualsEqualsToken => "==",
            SyntaxKind.ExclamationEqualsToken => "!=",
            SyntaxKind.LessThanToken => "<",
            SyntaxKind.LessThanEqualsToken => "<=",
            SyntaxKind.GreaterThanToken => ">",
            SyntaxKind.GreaterThanEqualsToken => ">=",
            SyntaxKind.LessThanLessThanToken => "<<",
            SyntaxKind.GreaterThanGreaterThanToken => ">>",
            SyntaxKind.PlusToken => "+",
            SyntaxKind.MinusToken => "-",
            SyntaxKind.AsteriskToken => "*",
            SyntaxKind.SlashToken => "/",
            SyntaxKind.PercentToken => "%",
            SyntaxKind.AmpersandToken => "&",
            SyntaxKind.BarToken => "|",
            SyntaxKind.CaretToken => "^",
            SyntaxKind.TildeToken => "~",
            SyntaxKind.PlusEqualsToken => "+=",
            SyntaxKind.MinusEqualsToken => "-=",
            SyntaxKind.AsteriskEqualsToken => "*=",
            SyntaxKind.SlashEqualsToken => "/=",
            SyntaxKind.PercentEqualsToken => "%=",
            SyntaxKind.AmpersandEqualsToken => "&=",
            SyntaxKind.BarEqualsToken => "|=",
            SyntaxKind.CaretEqualsToken => "^=",
            SyntaxKind.LessThanLessThanEqualsToken => "<<=",
            SyntaxKind.GreaterThanGreaterThanEqualsToken => ">>=",
            SyntaxKind.AmpersandAmpersandToken => "&&",
            SyntaxKind.BarBarToken => "||",
            SyntaxKind.ExclamationToken => "!",
            SyntaxKind.PlusPlusToken => "++",
            SyntaxKind.MinusMinusToken => "--",
            SyntaxKind.EqualsGreaterThanToken => "=>",
            SyntaxKind.SemicolonToken => ";",
            SyntaxKind.InterpolatedStringStartToken => "$\"",
            SyntaxKind.InterpolatedStringEndToken => "\"",
            _ => null,
        };
    }

    /// <summary>How a token kind is named in "expected ..." messages.</summary>
    public static string GetDisplayText(SyntaxKind kind) => kind switch
    {
        SyntaxKind.IdentifierToken => "a name",
        SyntaxKind.EndOfFileToken => "the end of the file",
        SyntaxKind.NumericLiteralToken => "a number",
        SyntaxKind.StringLiteralToken => "a string",
        _ => GetText(kind) is { } text ? $"'{text}'" : kind.ToString(),
    };

    // Precedence, higher binds tighter (ADR-0019): ?? < or < and < | < ^ < & < == != < relational, is < range
    // < shift < additive < multiplicative < as < unary. && and || only reach the parser to be reported, and take
    // the place of 'and' and 'or'.
    public const int UnaryPrecedence = 14;

    public static int GetBinaryPrecedence(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AsKeyword => 13,
        SyntaxKind.AsteriskToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken => 12,
        SyntaxKind.PlusToken or SyntaxKind.MinusToken => 11,
        SyntaxKind.LessThanLessThanToken or SyntaxKind.GreaterThanGreaterThanToken => 10,
        SyntaxKind.DotDotLessThanToken or SyntaxKind.DotDotDotToken or SyntaxKind.DotDotToken => 9,
        SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanToken
            or SyntaxKind.GreaterThanEqualsToken or SyntaxKind.IsKeyword => 8,
        SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken => 7,
        SyntaxKind.AmpersandToken => 6,
        SyntaxKind.CaretToken => 5,
        SyntaxKind.BarToken => 4,
        SyntaxKind.AndKeyword or SyntaxKind.AmpersandAmpersandToken => 3,
        SyntaxKind.OrKeyword or SyntaxKind.BarBarToken => 2,
        SyntaxKind.QuestionQuestionToken => 1,
        _ => 0,
    };

    public const int RangePrecedence = 9;

    public static SyntaxKind GetBinaryExpressionKind(SyntaxKind operatorKind) => operatorKind switch
    {
        SyntaxKind.AsteriskToken => SyntaxKind.MultiplyExpression,
        SyntaxKind.SlashToken => SyntaxKind.DivideExpression,
        SyntaxKind.PercentToken => SyntaxKind.ModuloExpression,
        SyntaxKind.PlusToken => SyntaxKind.AddExpression,
        SyntaxKind.MinusToken => SyntaxKind.SubtractExpression,
        SyntaxKind.LessThanLessThanToken => SyntaxKind.LeftShiftExpression,
        SyntaxKind.GreaterThanGreaterThanToken => SyntaxKind.RightShiftExpression,
        SyntaxKind.LessThanToken => SyntaxKind.LessThanExpression,
        SyntaxKind.LessThanEqualsToken => SyntaxKind.LessThanOrEqualExpression,
        SyntaxKind.GreaterThanToken => SyntaxKind.GreaterThanExpression,
        SyntaxKind.GreaterThanEqualsToken => SyntaxKind.GreaterThanOrEqualExpression,
        SyntaxKind.EqualsEqualsToken => SyntaxKind.EqualsExpression,
        SyntaxKind.ExclamationEqualsToken => SyntaxKind.NotEqualsExpression,
        SyntaxKind.AmpersandToken => SyntaxKind.BitwiseAndExpression,
        SyntaxKind.CaretToken => SyntaxKind.ExclusiveOrExpression,
        SyntaxKind.BarToken => SyntaxKind.BitwiseOrExpression,
        SyntaxKind.AndKeyword or SyntaxKind.AmpersandAmpersandToken => SyntaxKind.LogicalAndExpression,
        SyntaxKind.OrKeyword or SyntaxKind.BarBarToken => SyntaxKind.LogicalOrExpression,
        SyntaxKind.QuestionQuestionToken => SyntaxKind.CoalesceExpression,
        _ => throw new ArgumentOutOfRangeException(nameof(operatorKind), operatorKind, null),
    };

    public static bool IsAssignmentOperator(SyntaxKind kind) => kind is SyntaxKind.EqualsToken
        or SyntaxKind.PlusEqualsToken or SyntaxKind.MinusEqualsToken or SyntaxKind.AsteriskEqualsToken
        or SyntaxKind.SlashEqualsToken or SyntaxKind.PercentEqualsToken or SyntaxKind.AmpersandEqualsToken
        or SyntaxKind.BarEqualsToken or SyntaxKind.CaretEqualsToken or SyntaxKind.LessThanLessThanEqualsToken
        or SyntaxKind.GreaterThanGreaterThanEqualsToken or SyntaxKind.QuestionQuestionEqualsToken;

    public static bool IsModifier(SyntaxKind kind) => GetModifierRank(kind) >= 0;

    /// <summary>
    /// Position of a modifier in the fixed order of ADR-0019: visibility, static, abstract / sealed / virtual,
    /// override, async. -1 for tokens that are not modifiers.
    /// </summary>
    public static int GetModifierRank(SyntaxKind kind) => kind switch
    {
        SyntaxKind.PublicKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.PrivateKeyword => 0,
        SyntaxKind.StaticKeyword => 1,
        SyntaxKind.AbstractKeyword or SyntaxKind.SealedKeyword or SyntaxKind.VirtualKeyword => 2,
        SyntaxKind.OverrideKeyword => 3,
        SyntaxKind.AsyncKeyword => 4,
        _ => -1,
    };
}
