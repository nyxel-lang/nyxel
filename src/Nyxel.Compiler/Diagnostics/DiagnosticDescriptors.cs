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

    public static readonly DiagnosticDescriptor InterpolationSpansLines =
        Error("NYX1234", "Close the interpolation on the line where it starts; put a longer expression in a 'let' first and write its name between the braces.");

    public static readonly DiagnosticDescriptor EnumWithDataNeedsStorage =
        Error("NYX1235", "'{0}' has cases with data; say where its values live: 'struct enum {0}' (a value, copied on assignment, no allocation) or 'class enum {0}' (an object on the heap, which can be recursive).");

    public static readonly DiagnosticDescriptor ClassEnumWithoutData =
        Error("NYX1236", "'class enum' is for cases with data; '{0}' has none, so it's already a value: write 'enum {0}' ('struct enum {0}' to implement interfaces).");

    public static readonly DiagnosticDescriptor StaticWithSelf =
        Error("NYX1237", "'{0}' has 'self', so it's an instance method; remove 'static'.");

    public static readonly DiagnosticDescriptor FunctionWithoutSelfOrStatic =
        Error("NYX1238", "'{0}' has no 'self'; write 'static func {0}' for a static function, or add 'self' as the first parameter for an instance method.");

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
        Error("NYX1123", "A {0} can't follow '->' directly; {1}.");

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
        Error("NYX1176", "Only an 'enum' whose cases carry no data can {0}; an enum with data and a 'struct enum' can't.");

    public static readonly DiagnosticDescriptor EnumCaseValue =
        Error("NYX1177", "Give '{0}' a value too; either every case of an enum has a value or none does.");

    public static readonly DiagnosticDescriptor FlagsCaseValue =
        Error("NYX1178", "Give '{0}' a value; every case of a flags enum has one, usually a single bit such as 1, 2, 4 or 8.");

    public static readonly DiagnosticDescriptor StructEnumWithoutData =
        Error("NYX1179", "'struct enum' is for enums whose cases carry data or that implement interfaces; an enum without either is already a value type, so write 'enum'.");

    public static readonly DiagnosticDescriptor EvalStatement =
        Error("NYX1182", "An 'eval' block gives a value and can't stand on its own; use the value, as in 'let x = eval {{ ... }}'. To end a scope early, move the statements into a function of their own.");

    public static readonly DiagnosticDescriptor GiveOutsideEval =
        Error("NYX1183", "'give' gives the value of an 'eval' block; it can only be used inside one, and not from inside a lambda.");

    public static readonly DiagnosticDescriptor ReturnInEval =
        Error("NYX1184", "An 'eval' block can't 'return'; give its value with 'give'. To leave the function, give null and write '?? return' after the block.");

    public static readonly DiagnosticDescriptor JumpOutOfEval =
        Error("NYX1185", "'{0}' can't leave an 'eval' block; inside one it can only be used for a loop in the same block.");

    public static readonly DiagnosticDescriptor EvalWithoutGive =
        Error("NYX1186", "End an 'eval' block with 'give': write 'give {0}'.");

    public static readonly DiagnosticDescriptor BlockBranchesAsValue =
        Error("NYX1187", "When '{0}' gives a value, its branches are written with '->', as in '{1}'. To run statements first, use 'eval {{ ... give value }}'.");

    public static readonly DiagnosticDescriptor ArrowBranchesAsStatement =
        Error("NYX1188", "This '{0}' gives a value that is not used; as a statement, write its branches in braces: '{1}'.");

    public static readonly DiagnosticDescriptor MixedBranches =
        Error("NYX1189", "Write the branches of one '{0}' the same way: all with '->' when it gives a value, all in braces when it is a statement.");

    public static readonly DiagnosticDescriptor MatchBlockArmAsValue =
        Error("NYX1190", "When '{0}' gives a value, each {1} is one expression; to run statements first, write '-> eval {{ ... give value }}'.");

    public static readonly DiagnosticDescriptor ArrowBranchesNeedParentheses =
        Error("NYX1191", "Put this '{0}' in parentheses; as an operand or inside another branch it is not clear where it ends.");

    public static readonly DiagnosticDescriptor IfValueWithoutElse =
        Error("NYX1192", "An 'if' that gives a value needs an 'else': 'if condition -> a else -> b'.");

    public static readonly DiagnosticDescriptor MatchElse =
        Error("NYX1193", "Write 'case _' for the default arm of a 'match'; 'else' belongs to 'if' only.");

    public static readonly DiagnosticDescriptor DiscardAsName =
        Error("NYX1194", "'_' can't be a name; it marks a value that is not needed, as in 'let (_, max) = ...', 'out _' or 'case _'.");

    public static readonly DiagnosticDescriptor DiscardAsValue =
        Error("NYX1195", "'_' is not a value; it marks a value that is not needed, as in 'out _' or 'case _'.");

    public static readonly DiagnosticDescriptor PrivateMember =
        Error("NYX1196", "Remove 'private': members are private unless marked 'public' or 'protected'. 'private' is only written on an accessor ('private set') or on an init ('private init').");

    public static readonly DiagnosticDescriptor SetterExpressionBody =
        Error("NYX1197", "A setter has no expression body; write 'set(value) {{ ... }}'.");

    public static readonly DiagnosticDescriptor WithNotAfterNew =
        Error("NYX1198", "'with (...)' sets members of a value being created and only follows 'new Type(...)'; to change an existing value, assign to its members.");

    public static readonly DiagnosticDescriptor WithMemberName =
        Error("NYX1199", "Each item in 'with (...)' names the member it sets: 'Member = value'.");

    public static readonly DiagnosticDescriptor IndexerWithoutParameters =
        Error("NYX1201", "An indexer has at least one parameter, as in 'property self[index: int]: T'.");

    public static readonly DiagnosticDescriptor IndexerNotAllowed =
        Error("NYX1202", "An indexer can't {0}.");

    public static readonly DiagnosticDescriptor ParameterNotAllowed =
        Error("NYX1203", "A parameter of an {0} can't {1}.");

    public static readonly DiagnosticDescriptor EnumCaseAfterMember =
        Error("NYX1205", "Write all the cases of an enum before its other members.");

    public static readonly DiagnosticDescriptor EnumMemberKind =
        Error("NYX1206", "An enum's values are its cases, so it can't have {0}; it can have functions and computed properties.");

    public static readonly DiagnosticDescriptor EnumMemberModifier =
        Error("NYX1207", "An enum can't be inherited, so its members can't be '{0}'.");

    public static readonly DiagnosticDescriptor EnumVarSelf =
        Error("NYX1208", "An enum value doesn't change after it is created; write 'self' instead of 'var self'.");

    public static readonly DiagnosticDescriptor EnumInterface =
        Error("NYX1209", "An enum without data is a C# enum, which can't implement interfaces; write 'struct enum' to implement '{0}'.");

    public static readonly DiagnosticDescriptor OperatorNotPublic =
        Error("NYX1210", "Write 'public operator {0}': an operator is always public, and a member without 'public' reads as private.");

    public static readonly DiagnosticDescriptor OperatorModifier =
        Error("NYX1211", "An operator can't be '{0}'{1}.");

    public static readonly DiagnosticDescriptor EqualityOperatorDeclaration =
        Error("NYX1212", "'{0}' can't be declared: '==' compares the contents of a struct or an enum and the reference of a class, and '!=' is its opposite.");

    public static readonly DiagnosticDescriptor DerivedComparisonOperator =
        Error("NYX1213", "Declare only 'operator <'; '>', '<=' and '>=' are generated from it.");

    public static readonly DiagnosticDescriptor OperatorNotDeclarable =
        Error("NYX1214", "'{0}' can't be declared as an operator; the operators are + - * / % & | ^ ~ << >> and <, and 'as' for a conversion.");

    public static readonly DiagnosticDescriptor OperatorParameterCount =
        Error("NYX1215", "'operator {0}' takes {1}.");

    public static readonly DiagnosticDescriptor OperatorSelf =
        Error("NYX1216", "An operator has no 'self'; its operands are all parameters, as in 'operator +(a: Cell, b: Cell)'.");

    public static readonly DiagnosticDescriptor OperatorReturnType =
        Error("NYX1217", "An operator returns a value; write its type after the parameters, as in 'operator {0}(...) -> Type'.");

    public static readonly DiagnosticDescriptor OperatorPlacement =
        Error("NYX1218", "{0} can't be declared in {1}.");

    public static readonly DiagnosticDescriptor GeneratedEqualityMember =
        Error("NYX1219", "A {0} can't declare '{1}': its 'Equals' and 'GetHashCode' are generated and compare the contents.");

    public static readonly DiagnosticDescriptor GeneratedCompareTo =
        Error("NYX1220", "'CompareTo' is generated from 'operator <'; remove this 'CompareTo' or the operator.");

    public static readonly DiagnosticDescriptor VarianceModifier =
        Error("NYX1223", "Nyxel doesn't declare variance yet; write '{0}' without '{1}'. .NET's variant interfaces such as IEnumerable<T> still convert as in C#.");

    public static readonly DiagnosticDescriptor ExtensionTypeParameterUnused =
        Error("NYX1224", "'{0}' isn't used in the extended type '{1}'; a type parameter only some members use goes on them, as in 'func Name<{0}>(...)'.");

    public static readonly DiagnosticDescriptor DuplicateImportAlias =
        Error("NYX1227", "'{0}' already names another import of this file.");

    public static readonly DiagnosticDescriptor AwaitCaseKeyword =
        Error("NYX1228", "Start each line of 'await {0} {{ }}' with 'case', as in 'case self.FadeIn()'.");

    public static readonly DiagnosticDescriptor AwaitInCase =
        Error("NYX1229", "'await {0}' waits for each case itself; drop this 'await'.");

    public static readonly DiagnosticDescriptor AwaitCaseAssignment =
        Error("NYX1230", "A case can't assign to an existing variable or member: which case finishes first isn't known. Name the result with 'case let name = ...' and assign it in the branch, or after the block.");

    public static readonly DiagnosticDescriptor AwaitGroupCaseCount =
        Error("NYX1231", "'await {0}' needs at least two cases; to wait for one thing, write 'await' before it.");

    public static readonly DiagnosticDescriptor AwaitCaseNameWithoutBranch =
        Error("NYX1232", "'{0}' is only visible in the branch of its case; use it after '->', or take the results out as the value of the block: 'let ... = await {1} {{ ... }}'.");

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
        Error("NYX1157", "Nyxel has no '?:'; write 'if condition -> a else -> b'.");

    public static readonly DiagnosticDescriptor ForceUnwrap =
        Error("NYX1158", "Nyxel has no '!' to assert a value is not null; write '?? throw new InvalidOperationException(\"why\")'.");

    public static readonly DiagnosticDescriptor UsingVar =
        Error("NYX1159", "Write '{0} = ...' without '{1}'.");

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
        Error("NYX1166", "A block can't stand on its own. To compute a value in a scope of its own, write 'let x = eval {{ ... give value }}'; otherwise move the statements into a function of their own.");

    public static readonly DiagnosticDescriptor Initializer =
        Error("NYX1167", "Nyxel has no initializers in braces after 'new'; {0}.");

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

    public static readonly DiagnosticDescriptor InitCallAsMember =
        Error("NYX1200", "Another init is called with 'init {0}(...)', or 'init {0}.Name(...)' for a named one; it is the first statement of an init.");

    public static readonly DiagnosticDescriptor EventAccessors =
        Error("NYX1204", "Events have no 'add' / 'remove' accessors; to forward another object's event, subscribe to it and 'emit' this one from the handler.");

    public static readonly DiagnosticDescriptor CSharpOperatorSyntax =
        Error("NYX1221", "C# operator syntax; write 'public operator {0}({1}) -> {2}'.");

    public static readonly DiagnosticDescriptor CSharpConversionSyntax =
        Error("NYX1222", "Write a conversion as 'public operator as({0}) -> {1}' and convert with 'value as {1}'{2}.");

    public static readonly DiagnosticDescriptor ImportAliasSyntax =
        Error("NYX1225", "Write a namespace alias as 'import {0} under {1}', then its types as '{1}.Name'.");

    public static readonly DiagnosticDescriptor StaticImport =
        Error("NYX1226", "Nyxel has no static imports; write the type name before each member, as in '{0}.Name'.");

    public static readonly DiagnosticDescriptor ForAwait =
        Error("NYX1233", "Write 'await for {0} in ...': a statement that waits starts with 'await', as 'await using' does.");

    // Semantic analysis (docs/design/semantic-analysis.md).

    public static readonly DiagnosticDescriptor NotSupportedYet =
        Error("NYX2001", "{0} is not supported by the compiler yet.");

    public static readonly DiagnosticDescriptor UndefinedName =
        Error("NYX2002", "There is no local variable or parameter named '{0}'.");

    public static readonly DiagnosticDescriptor MemberNeedsReceiver =
        Error("NYX2003", "'{0}' is a member of '{1}'; write '{2}.{0}'.");

    public static readonly DiagnosticDescriptor TypeNotFound =
        Error("NYX2004", "There is no type named '{0}'; check the spelling and the imports.");

    public static readonly DiagnosticDescriptor NamespaceNotFound =
        Error("NYX2005", "There is no namespace named '{0}'.");

    public static readonly DiagnosticDescriptor AmbiguousType =
        Error("NYX2006", "'{0}' could be '{1}' or '{2}'; write the full name, or import one of the namespaces under an alias.");

    public static readonly DiagnosticDescriptor MemberNotFound =
        Error("NYX2007", "'{1}' has no member named '{0}'.");

    public static readonly DiagnosticDescriptor CannotConvert =
        Error("NYX2008", "A value of type '{0}' can't be used where '{1}' is expected.");

    public static readonly DiagnosticDescriptor NoValue =
        Error("NYX2009", "'{0}' returns nothing, so there is no value to use.");

    public static readonly DiagnosticDescriptor TypeIsNotValue =
        Error("NYX2010", "'{0}' is a type, not a value.");

    public static readonly DiagnosticDescriptor NamespaceIsNotValue =
        Error("NYX2011", "'{0}' is a namespace, not a value.");

    public static readonly DiagnosticDescriptor FunctionIsNotValue =
        Error("NYX2012", "'{0}' is a function; call it with '(...)'.");

    public static readonly DiagnosticDescriptor NoMatchingOverload =
        Error("NYX2013", "No overload of '{0}' takes ({1}).");

    public static readonly DiagnosticDescriptor AmbiguousCall =
        Error("NYX2014", "The call could be '{0}' or '{1}'; convert an argument with 'as' to choose.");

    public static readonly DiagnosticDescriptor NoSuchParameter =
        Error("NYX2015", "'{0}' has no parameter named '{1}'.");

    public static readonly DiagnosticDescriptor StaticMemberOnValue =
        Error("NYX2016", "'{0}' is static; write '{1}.{0}'.");

    public static readonly DiagnosticDescriptor InstanceMemberOnType =
        Error("NYX2017", "'{0}' needs a value of '{1}'; write it after a value, not after the type name.");

    public static readonly DiagnosticDescriptor CreateWithNew =
        Error("NYX2018", "'{0}' is a type; create a value with 'new {0}(...)'.");

    public static readonly DiagnosticDescriptor NotCallable =
        Error("NYX2019", "This is not a function and can't be called.");

    public static readonly DiagnosticDescriptor SelfNotAvailable =
        Error("NYX2020", "'self' is only available in functions that take 'self' and in 'init'.");

    public static readonly DiagnosticDescriptor OperatorNotDefined =
        Error("NYX2021", "Operator '{0}' can't be applied to '{1}' and '{2}'.");

    public static readonly DiagnosticDescriptor UnaryOperatorNotDefined =
        Error("NYX2022", "Operator '{0}' can't be applied to '{1}'.");

    public static readonly DiagnosticDescriptor DecimalLiteralForInteger =
        Error("NYX2023", "'{0}' is not a whole number, but '{1}' is expected here.");

    public static readonly DiagnosticDescriptor ConstantOutOfRange =
        Error("NYX2024", "The value {0} doesn't fit in '{1}'.");

    public static readonly DiagnosticDescriptor ConstantDivisionByZero =
        Error("NYX2025", "Division by zero.");

    public static readonly DiagnosticDescriptor AssignToLet =
        Error("NYX2026", "'{0}' is declared with 'let' and can't be assigned again; declare it with 'var'.");

    public static readonly DiagnosticDescriptor AssignToParameter =
        Error("NYX2027", "Parameters can't be assigned; copy '{0}' into a 'var' first.");

    public static readonly DiagnosticDescriptor NotAssignable =
        Error("NYX2028", "This can't be assigned to: only variables, fields, properties with a setter and elements can.");

    public static readonly DiagnosticDescriptor PropertyHasNoSetter =
        Error("NYX2029", "'{0}' can't be set.");

    public static readonly DiagnosticDescriptor PropertyHasNoGetter =
        Error("NYX2030", "'{0}' can't be read.");

    public static readonly DiagnosticDescriptor NameAlreadyDeclared =
        Error("NYX2031", "'{0}' is already declared on line {1}; choose another name.");

    public static readonly DiagnosticDescriptor DuplicateTypeName =
        Error("NYX2032", "The namespace '{1}' already has a type named '{0}'.");

    public static readonly DiagnosticDescriptor DuplicateMemberName =
        Error("NYX2033", "'{1}' already has a member named '{0}'; functions can't be overloaded.");

    public static readonly DiagnosticDescriptor NotAllPathsReturn =
        Error("NYX2034", "Not every path through '{0}' returns a value; end it with 'return'.");

    public static readonly DiagnosticDescriptor ReturnValueFromVoid =
        Error("NYX2035", "'{0}' returns nothing; write 'return' without a value.");

    public static readonly DiagnosticDescriptor ReturnWithoutValue =
        Error("NYX2036", "'{0}' returns '{1}'; write 'return' with a value.");

    public static readonly DiagnosticDescriptor JumpOutsideLoop =
        Error("NYX2037", "'{0}' can only be used inside a loop.");

    public static readonly DiagnosticDescriptor ExpressionNotStatement =
        Error("NYX2038", "This value isn't used; only calls and 'new' can stand alone as statements.");

    public static readonly DiagnosticDescriptor NotIterable =
        Error("NYX2039", "A value of type '{0}' can't be walked with 'for ... in'.");

    public static readonly DiagnosticDescriptor RangeNotIntegral =
        Error("NYX2040", "A range in 'for' needs whole numbers, not '{0}'.");

    public static readonly DiagnosticDescriptor CannotCreate =
        Error("NYX2041", "Can't create a value of '{0}': {1}.");

    public static readonly DiagnosticDescriptor AsNotAllowed =
        Error("NYX2042", "'{0}' can't be converted to '{1}' with 'as'.");

    public static readonly DiagnosticDescriptor AsMightFail =
        Error("NYX2043", "'as' can't convert '{0}' to '{1}' because it might fail; check it with 'if x is {1}' instead.");

    public static readonly DiagnosticDescriptor Inaccessible =
        Error("NYX2044", "'{0}' is {1} and can't be used here.");

    public static readonly DiagnosticDescriptor NoEntryPoint =
        Error("NYX2045", "The program has no entry point; add 'static func Main()' to a type.");

    public static readonly DiagnosticDescriptor MultipleEntryPoints =
        Error("NYX2046", "The program has more than one 'Main': '{0}' and '{1}'.");

    public static readonly DiagnosticDescriptor BadEntryPointSignature =
        Error("NYX2047", "Write 'Main' as 'static func Main()' or 'static func Main(args: array<string>)', with '-> int' to return an exit code.");

    public static readonly DiagnosticDescriptor AsyncEntryPoint =
        Error("NYX2048", "'Main' can't be 'async' yet: coroutines need a host to drive them.");

    public static readonly DiagnosticDescriptor FieldNeedsInitialValue =
        Error("NYX2049", "'{0}' has no initial value; give it one here, or set it in an 'init'.");

    public static readonly DiagnosticDescriptor FieldNotSetInInit =
        Error("NYX2050", "This 'init' doesn't set '{0}' on every path.");

    public static readonly DiagnosticDescriptor IndexCount =
        Error("NYX2051", "'{0}' takes {1} index(es), not {2}.");

    public static readonly DiagnosticDescriptor NotIndexable =
        Error("NYX2052", "A value of type '{0}' can't be indexed with '[...]'.");

    public static readonly DiagnosticDescriptor InternalError =
        Error("NYX2053", "Internal compiler error: the generated C# doesn't compile ({0}: {1}). Please report it.");

    public static readonly DiagnosticDescriptor IntegerLiteralDivisionInDecimal =
        Error("NYX2054", "'{0}' divides whole numbers, which truncates, inside a decimal calculation; write '{1}' to divide as decimals.");

    public static readonly DiagnosticDescriptor MaybeNull =
        Error("NYX2057", "'{0}' may be null; {1}.");

    public static readonly DiagnosticDescriptor MaybeNullConversion =
        Error("NYX2058", "A value of type '{0}' may be null and can't be used where '{1}' is expected; {2}.");

    public static readonly DiagnosticDescriptor NotNullable =
        Error("NYX2059", "'{0}' can't be null; write '{0}?' for a value that may be null.");

    public static readonly DiagnosticDescriptor NullNeedsType =
        Error("NYX2060", "The type of '{0}' can't come from 'null'; write it, as in '{0}: Enemy? = null'.");

    public static readonly DiagnosticDescriptor IsNeverTrue =
        Error("NYX2061", "A value of type '{0}' is never a '{1}'.");

    public static readonly DiagnosticDescriptor IsNullableType =
        Error("NYX2062", "'is' checks for a value that isn't null; write 'is {0}' without the '?'.");

    public static readonly DiagnosticDescriptor NeverNull =
        Error("NYX2063", "A value of type '{0}' is never null, so '{1}' has nothing to check.");

    public static readonly DiagnosticDescriptor ArrayElementsStartNull =
        Error("NYX2064", "'new {0}(...)' would start every element as null, which '{1}' doesn't allow; make the elements '{1}?', or give each one with 'element = func(i) = ...'.");

    public static readonly DiagnosticDescriptor NotException =
        Error("NYX2065", "'throw' needs an exception, and '{0}' isn't an 'Exception'.");

    private static DiagnosticDescriptor Error(string id, string messageFormat) =>
        new(id, DiagnosticSeverity.Error, messageFormat);
}
