namespace Nyxel.Compiler.Diagnostics;

/// <summary>
/// Every diagnostic the compiler can report. NYX1xxx are lexical and syntax errors (ADR-0019); a new one takes the
/// largest NYX1 id so far plus one, whatever group it goes in. Keep this list and docs/design/diagnostics.md in
/// sync; a test checks that every id here is documented.
/// </summary>
public static class DiagnosticDescriptors
{
    // Lexical.

    public static readonly DiagnosticDescriptor UnexpectedCharacter =
        Error("NYX1001", "Unexpected character '{0}'.");

    public static readonly DiagnosticDescriptor UnterminatedString =
        Error("NYX1002", "Missing the closing {0} before the end of the line.");

    public static readonly DiagnosticDescriptor InvalidEscapeSequence =
        Error("NYX1003", "Invalid escape sequence '{0}'.");

    public static readonly DiagnosticDescriptor NumericSuffix =
        Error("NYX1004", "Numeric literals have no type suffix; remove '{0}'. The type comes from where the literal is used.");

    public static readonly DiagnosticDescriptor InvalidNumber =
        Error("NYX1005", "Invalid numeric literal '{0}'.");

    public static readonly DiagnosticDescriptor IntegerTooLarge =
        Error("NYX1006", "Integer literal '{0}' is too large.");

    public static readonly DiagnosticDescriptor BlockComment =
        Error("NYX1007", "Nyxel has no block comments; start each line with '//'.");

    public static readonly DiagnosticDescriptor VerbatimString =
        Error("NYX1008", "Nyxel has no verbatim strings (@\"...\"); use a raw string \"\"\"...\"\"\", where '\\' is an ordinary character.");

    public static readonly DiagnosticDescriptor CharacterLiteralLength =
        Error("NYX1009", "A character literal must contain exactly one character; use a string (\"...\") for text.");

    public static readonly DiagnosticDescriptor LeadingDotNumber =
        Error("NYX1010", "Write '0{0}' instead of '{0}'.");

    public static readonly DiagnosticDescriptor UnclosedInterpolation =
        Error("NYX1011", "'{{' in an interpolated string is not closed; write '{{{{' for a literal brace.");

    public static readonly DiagnosticDescriptor UnescapedCloseBrace =
        Error("NYX1012", "Write '}}}}' for a literal '}}' in an interpolated string.");

    public static readonly DiagnosticDescriptor RangeDotDot =
        Error("NYX1013", "Nyxel has no '..'; write '..<' to exclude the end or '...' to include it.");

    public static readonly DiagnosticDescriptor UnterminatedRawString =
        Error("NYX1014", "The raw string is not closed; put the closing {0} on a line of its own.");

    public static readonly DiagnosticDescriptor RawStringQuotes =
        Error("NYX1015", "This raw string starts with {0} quotes, so it can't contain {1} quotes in a row; start and end it with more quotes.");

    public static readonly DiagnosticDescriptor RawStringIndentation =
        Error("NYX1016", "Each line of a multi-line raw string must start with the same whitespace as the line of its closing quotes.");

    public static readonly DiagnosticDescriptor EmptyRawString =
        Error("NYX1017", "A multi-line raw string needs at least one line between its opening and closing quotes; write \"\" for an empty string.");

    public static readonly DiagnosticDescriptor RawStringBraces =
        Error("NYX1018", "The raw string contains '{0}' in its text; start it with {1} to allow that.");

    public static readonly DiagnosticDescriptor RawStringHoleClose =
        Error("NYX1019", "End the interpolation with '{0}' on the same line: as many braces as there are '$' before the string.");

    public static readonly DiagnosticDescriptor DollarsWithoutRawString =
        Error("NYX1020", "Several '$' only go before a raw string (\"\"\"...\"\"\"); write a single '$'.");

    // Syntax.

    public static readonly DiagnosticDescriptor Expected =
        Error("NYX1101", "Expected {0}.");

    public static readonly DiagnosticDescriptor UnexpectedToken =
        Error("NYX1102", "Unexpected {0}.");

    public static readonly DiagnosticDescriptor OperatorAtEndOfLine =
        Error("NYX1103", "'{0}' can't end a line; move it to the start of the next line.");

    public static readonly DiagnosticDescriptor OpenBraceOnNextLine =
        Error("NYX1104", "'{{' must be on the same line as the code before it.");

    public static readonly DiagnosticDescriptor KeywordOnNextLine =
        Error("NYX1105", "'{0}' must be on the same line as the '}}' before it.");

    public static readonly DiagnosticDescriptor ExpectedLineBreak =
        Error("NYX1106", "Expected the end of the line; each {0} goes on its own line.");

    public static readonly DiagnosticDescriptor InvalidLineStart =
        Error("NYX1107", "A line can't start with {0}. A line continues the previous one only when it starts with '.', '?.' or a binary operator, or when the previous line ends with '->' or an assignment operator such as '=' or '+='.");

    public static readonly DiagnosticDescriptor ModifierOrder =
        Error("NYX1108", "Write the modifiers in the order '{0}'.");

    public static readonly DiagnosticDescriptor DuplicateModifier =
        Error("NYX1109", "Modifier '{0}' is repeated.");

    public static readonly DiagnosticDescriptor NamespaceNotFirst =
        Error("NYX1110", "The namespace declaration must come before imports and declarations.");

    public static readonly DiagnosticDescriptor DuplicateNamespace =
        Error("NYX1111", "A file has at most one namespace declaration.");

    public static readonly DiagnosticDescriptor ImportAfterDeclaration =
        Error("NYX1112", "Imports must come before declarations.");

    public static readonly DiagnosticDescriptor TopLevelMember =
        Error("NYX1113", "Only types and extensions can be declared at the top level of a file; move this {0} into a type.");

    public static readonly DiagnosticDescriptor NestedType =
        Error("NYX1114", "Types can't be declared inside other types; move this declaration to the top level of the file.");

    public static readonly DiagnosticDescriptor FieldNeedsType =
        Error("NYX1115", "Field '{0}' must declare its type, for example '{0}: int'.");

    public static readonly DiagnosticDescriptor ParameterNeedsType =
        Error("NYX1116", "Parameter '{0}' must declare its type, for example '{0}: int'.");

    public static readonly DiagnosticDescriptor SelfNotFirst =
        Error("NYX1117", "'self' can only be the first parameter.");

    public static readonly DiagnosticDescriptor VarParameter =
        Error("NYX1118", "Only 'self' can be declared 'var'; copy the parameter into a 'var' local to change it.");

    public static readonly DiagnosticDescriptor SelfWithType =
        Error("NYX1119", "'self' is written without a type.");

    public static readonly DiagnosticDescriptor TryWithoutHandler =
        Error("NYX1120", "'try' needs at least one 'catch' or 'finally'.");

    public static readonly DiagnosticDescriptor ChainedRange =
        Error("NYX1121", "A range can't be an operand of another range.");

    public static readonly DiagnosticDescriptor AssignmentAsValue =
        Error("NYX1122", "Assignment is a statement and has no value; did you mean '=='?");

    public static readonly DiagnosticDescriptor MatchArmStatement =
        Error("NYX1123", "The right side of '->' is an expression or a block; put the {0} in braces: '-> {{ ... }}'.");

    public static readonly DiagnosticDescriptor TupleElementName =
        Error("NYX1124", "Give each tuple element a name, as in '(Min: int, Max: int)'.");

    public static readonly DiagnosticDescriptor TupleElementCount =
        Error("NYX1125", "A tuple has at least two elements.");

    public static readonly DiagnosticDescriptor DeconstructionElement =
        Error("NYX1126", "Write plain names when taking a tuple apart; {0}.");

    public static readonly DiagnosticDescriptor RangeValue =
        Error("NYX1127", "A range is not a value; write it in 'for', in 'case', or in a slice such as 'items[1..<3]'.");

    public static readonly DiagnosticDescriptor RangeMethod =
        Error("NYX1128", "'{0}' can't be used on a range; in 'for', a range can be followed by 'Reversed()' and 'StepBy(n)'.");

    public static readonly DiagnosticDescriptor RangeEnd =
        Error("NYX1129", "Write both ends of the range; only a slice can leave one out, as in 'items[..<3]' or 'items[2...]'.");

    public static readonly DiagnosticDescriptor EnumUnderlyingType =
        Error("NYX1175", "An enum's underlying type is one integer type: byte, sbyte, short, ushort, int, uint, long or ulong.");

    public static readonly DiagnosticDescriptor EnumWithDataValues =
        Error("NYX1176", "Only an enum whose cases carry no data can {0}.");

    public static readonly DiagnosticDescriptor EnumCaseValue =
        Error("NYX1177", "Give '{0}' a value too; either every case of an enum has a value or none does.");

    public static readonly DiagnosticDescriptor FlagsCaseValue =
        Error("NYX1178", "Give '{0}' a value; every case of a flags enum has one, usually a single bit such as 1, 2, 4 or 8.");

    public static readonly DiagnosticDescriptor StructEnumWithoutData =
        Error("NYX1179", "'struct enum' is for enums whose cases carry data; an enum without data is already a value type, so write 'enum'.");

    // Habits from C# and other languages, each with the Nyxel way to write it.

    public static readonly DiagnosticDescriptor SymbolicLogicalOperator =
        Error("NYX1150", "Use '{0}' instead of '{1}'.");

    public static readonly DiagnosticDescriptor IncrementOperator =
        Error("NYX1151", "Nyxel has no '{0}'; write '{1}'.");

    public static readonly DiagnosticDescriptor FatArrow =
        Error("NYX1152", "Nyxel has no '=>'; match arms use '->' and lambdas are written 'func(x) = expression'.");

    public static readonly DiagnosticDescriptor Semicolon =
        Error("NYX1153", "Statements end at the end of the line; remove ';'.");

    public static readonly DiagnosticDescriptor CastSyntax =
        Error("NYX1154", "Nyxel has no '({0})x' cast; write 'x as {0}'.");

    public static readonly DiagnosticDescriptor NamedArgumentColon =
        Error("NYX1155", "Named arguments and tuple elements use '=': write '{0} = ...'.");

    public static readonly DiagnosticDescriptor IsWithName =
        Error("NYX1156", "'is' does not introduce a name: a checked local variable or parameter is narrowed in place. Remove '{0}'.");

    public static readonly DiagnosticDescriptor Ternary =
        Error("NYX1157", "Nyxel has no '?:'; write 'if condition {{ a }} else {{ b }}'.");

    public static readonly DiagnosticDescriptor ForceUnwrap =
        Error("NYX1158", "Nyxel has no '!' to assert a value is not null; write '?? throw new InvalidOperationException(\"why\")'.");

    public static readonly DiagnosticDescriptor UsingVar =
        Error("NYX1159", "Write 'using {0} = ...' without '{1}'.");

    public static readonly DiagnosticDescriptor UsingBlock =
        Error("NYX1160", "Nyxel has no 'using (...) {{ }}'; write 'using name = ...', which is released at the end of the enclosing block.");

    public static readonly DiagnosticDescriptor UsingDirective =
        Error("NYX1161", "Use 'import {0}' to import a namespace.");

    public static readonly DiagnosticDescriptor CStyleFor =
        Error("NYX1162", "Nyxel has no C-style 'for'; write 'for i in 0..<count'.");

    public static readonly DiagnosticDescriptor PatternComma =
        Error("NYX1163", "Combine patterns with 'or', for example 'case 1 or 2'.");

    public static readonly DiagnosticDescriptor ArrayTypeBrackets =
        Error("NYX1164", "Write '{0}<{1}>' instead of '{1}[{2}]'.");

    public static readonly DiagnosticDescriptor ArrayCreationBrackets =
        Error("NYX1165", "Write 'new {0}<{1}>({2})' instead of 'new {1}[{2}]'.");

    public static readonly DiagnosticDescriptor StandaloneBlock =
        Error("NYX1166", "A block can't stand on its own; to end a scope or release a 'using' early, move the statements into a function of their own.");

    public static readonly DiagnosticDescriptor Initializer =
        Error("NYX1167", "Nyxel has no initializers in braces after 'new'; list the elements in a collection literal such as '[1, 2]', or set the fields after creating the value.");

    public static readonly DiagnosticDescriptor TupleElementOrder =
        Error("NYX1168", "Write '{0}: {1}' instead of '{1} {0}'.");

    public static readonly DiagnosticDescriptor LoopLabel =
        Error("NYX1169", "Nyxel has no loop labels; move the loops into a function of their own and leave them with 'return'.");

    public static readonly DiagnosticDescriptor SliceToEnd =
        Error("NYX1170", "Write '{0}...' to slice to the end.");

    public static readonly DiagnosticDescriptor IndexFromEnd =
        Error("NYX1171", "Nyxel doesn't count from the end with '^'; write 'items[items.Count - 1]' ('Length' for arrays and strings).");

    public static readonly DiagnosticDescriptor CollectionSpread =
        Error("NYX1172", "A collection literal can't spread another collection; create it, then add the items with 'AddRange'.");

    public static readonly DiagnosticDescriptor DictionaryLiteral =
        Error("NYX1173", "Nyxel has no dictionary literal yet; create the dictionary, then set each entry with 'd[key] = value'.");

    public static readonly DiagnosticDescriptor ArrayRank =
        Error("NYX1174", "Arrays have at most three dimensions: 'array<T>', 'array2d<T>' and 'array3d<T>'.");

    public static readonly DiagnosticDescriptor IsCaseData =
        Error("NYX1180", "'is' only checks which case a value is; take out the data with 'match', as in 'case Burn(amount, seconds) -> ...'.");

    public static readonly DiagnosticDescriptor FlagsAttribute =
        Error("NYX1181", "Write 'flags enum' instead of '[Flags]'.");

    private static DiagnosticDescriptor Error(string id, string messageFormat) =>
        new(id, DiagnosticSeverity.Error, messageFormat);
}
