namespace Nyxel.Compiler.Diagnostics;

/// <summary>
/// Every diagnostic the compiler can report. NYX1xxx are lexical and syntax errors (ADR-0019). Keep this list and
/// docs/design/diagnostics.md in sync; a test checks that every id here is documented.
/// </summary>
public static class DiagnosticDescriptors
{
    // Lexical: NYX1001-NYX1099.

    public static readonly DiagnosticDescriptor UnexpectedCharacter =
        Error("NYX1001", "Unexpected character '{0}'.");

    public static readonly DiagnosticDescriptor UnterminatedString =
        Error("NYX1002", "The string is not closed before the end of the line; add the closing '\"'.");

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

    public static readonly DiagnosticDescriptor UnsupportedStringForm =
        Error("NYX1008", "{0} are not supported; use a regular \"...\" or interpolated $\"...\" string.");

    public static readonly DiagnosticDescriptor CharacterLiteral =
        Error("NYX1009", "Character literals are not supported yet; use a string.");

    public static readonly DiagnosticDescriptor LeadingDotNumber =
        Error("NYX1010", "Write '0{0}' instead of '{0}'.");

    public static readonly DiagnosticDescriptor UnclosedInterpolation =
        Error("NYX1011", "'{{' in an interpolated string is not closed; write '{{{{' for a literal brace.");

    public static readonly DiagnosticDescriptor UnescapedCloseBrace =
        Error("NYX1012", "Write '}}}}' for a literal '}}' in an interpolated string.");

    public static readonly DiagnosticDescriptor RangeDotDot =
        Error("NYX1013", "Nyxel has no '..'; write '..<' to exclude the end or '...' to include it.");

    // Syntax: NYX1101-NYX1199.

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
        Error("NYX1107", "A line can't start with {0}. A line continues the previous one only when it starts with '.', '?.' or a binary operator, or when the previous line ends with '=' or '->'.");

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

    public static readonly DiagnosticDescriptor ArrayType =
        Error("NYX1124", "Array types ('T[]') are not supported yet; use List<T>.");

    // Habits from C# and other languages, each with the Nyxel way to write it: NYX1150-NYX1199.

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
        Error("NYX1155", "Named arguments use '=': write '{0} = ...'.");

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

    private static DiagnosticDescriptor Error(string id, string messageFormat) =>
        new(id, DiagnosticSeverity.Error, messageFormat);
}
