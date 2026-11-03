using System.Collections.Immutable;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Binding;

// The bound tree (docs/design/semantic-analysis.md, "绑定树"): the shape of the syntax tree, with every name resolved
// to its symbol, every expression typed, and what is implicit in the source written out (conversions, the receiver
// of a member, which overload was chosen). Lowering reads only this tree.

internal abstract class BoundNode(SyntaxNode syntax)
{
    public SyntaxNode Syntax { get; } = syntax;
}

internal abstract class BoundStatement(SyntaxNode syntax) : BoundNode(syntax);

internal abstract class BoundExpression(SyntaxNode syntax, TypeSymbol type) : BoundNode(syntax)
{
    public TypeSymbol Type { get; } = type;

    /// <summary>The value when it is known at compile time: literals and folded literal arithmetic.</summary>
    public virtual object? ConstantValue => null;

    public bool HasErrors => Type.IsError;
}

// Statements ---------------------------------------------------------------------------------------------------

internal sealed class BoundBlock(SyntaxNode syntax, ImmutableArray<BoundStatement> statements) : BoundStatement(syntax)
{
    public ImmutableArray<BoundStatement> Statements { get; } = statements;
}

internal sealed class BoundLocalDeclaration(SyntaxNode syntax, LocalSymbol local, BoundExpression initializer) : BoundStatement(syntax)
{
    public LocalSymbol Local { get; } = local;

    public BoundExpression Initializer { get; } = initializer;
}

internal sealed class BoundExpressionStatement(SyntaxNode syntax, BoundExpression expression) : BoundStatement(syntax)
{
    public BoundExpression Expression { get; } = expression;
}

/// <summary><c>target = value</c>; the value is already converted to the target's type.</summary>
internal sealed class BoundAssignment(SyntaxNode syntax, BoundExpression target, BoundExpression value) : BoundStatement(syntax)
{
    public BoundExpression Target { get; } = target;

    public BoundExpression Value { get; } = value;
}

/// <summary><c>target += value</c> and the other compound forms, with the semantics of C#.</summary>
internal sealed class BoundCompoundAssignment(SyntaxNode syntax, BoundExpression target, BinaryOperatorKind operatorKind, BoundExpression value) : BoundStatement(syntax)
{
    public BoundExpression Target { get; } = target;

    public BinaryOperatorKind OperatorKind { get; } = operatorKind;

    public BoundExpression Value { get; } = value;
}

internal sealed class BoundIf(SyntaxNode syntax, BoundExpression condition, BoundStatement then, BoundStatement? @else) : BoundStatement(syntax)
{
    public BoundExpression Condition { get; } = condition;

    public BoundStatement Then { get; } = then;

    public BoundStatement? Else { get; } = @else;
}

internal sealed class BoundWhile(SyntaxNode syntax, BoundExpression condition, BoundBlock body) : BoundStatement(syntax)
{
    public BoundExpression Condition { get; } = condition;

    public BoundBlock Body { get; } = body;
}

/// <summary><c>for i in from..&lt;to</c>, or <c>from...to</c> when the end is included.</summary>
internal sealed class BoundForRange(SyntaxNode syntax, LocalSymbol variable, BoundExpression from, BoundExpression to, bool includesEnd, BoundBlock body)
    : BoundStatement(syntax)
{
    public LocalSymbol Variable { get; } = variable;

    public BoundExpression From { get; } = from;

    public BoundExpression To { get; } = to;

    public bool IncludesEnd { get; } = includesEnd;

    public BoundBlock Body { get; } = body;
}

/// <summary><c>for item in collection</c>: arrays, strings and anything that is an <c>IEnumerable&lt;T&gt;</c>.</summary>
internal sealed class BoundForEach(SyntaxNode syntax, LocalSymbol variable, BoundExpression collection, BoundBlock body) : BoundStatement(syntax)
{
    public LocalSymbol Variable { get; } = variable;

    public BoundExpression Collection { get; } = collection;

    public BoundBlock Body { get; } = body;
}

internal sealed class BoundReturn(SyntaxNode syntax, BoundExpression? value) : BoundStatement(syntax)
{
    public BoundExpression? Value { get; } = value;
}

internal sealed class BoundBreak(SyntaxNode syntax) : BoundStatement(syntax);

internal sealed class BoundContinue(SyntaxNode syntax) : BoundStatement(syntax);

// Expressions --------------------------------------------------------------------------------------------------

internal sealed class BoundLiteral(SyntaxNode syntax, object? value, TypeSymbol type) : BoundExpression(syntax, type)
{
    public object? Value { get; } = value;

    public override object? ConstantValue => Value;
}

internal sealed class BoundLocalAccess(SyntaxNode syntax, LocalSymbol local) : BoundExpression(syntax, local.Type)
{
    public LocalSymbol Local { get; } = local;
}

internal sealed class BoundParameterAccess(SyntaxNode syntax, ParameterSymbol parameter) : BoundExpression(syntax, parameter.Type)
{
    public ParameterSymbol Parameter { get; } = parameter;
}

internal sealed class BoundSelf(SyntaxNode syntax, NamedTypeSymbol type) : BoundExpression(syntax, type);

/// <summary>A field; the receiver is null for a static field.</summary>
internal sealed class BoundFieldAccess(SyntaxNode syntax, BoundExpression? receiver, FieldSymbol field) : BoundExpression(syntax, field.Type)
{
    public BoundExpression? Receiver { get; } = receiver;

    public FieldSymbol Field { get; } = field;

    public override object? ConstantValue => Field.IsConst ? Field.ConstantValue : null;
}

internal sealed class BoundPropertyAccess(SyntaxNode syntax, BoundExpression? receiver, PropertySymbol property) : BoundExpression(syntax, property.Type)
{
    public BoundExpression? Receiver { get; } = receiver;

    public PropertySymbol Property { get; } = property;
}

internal sealed class BoundIndexerAccess(SyntaxNode syntax, BoundExpression receiver, PropertySymbol indexer, ImmutableArray<BoundArgument> arguments)
    : BoundExpression(syntax, indexer.Type)
{
    public BoundExpression Receiver { get; } = receiver;

    public PropertySymbol Indexer { get; } = indexer;

    public ImmutableArray<BoundArgument> Arguments { get; } = arguments;
}

internal sealed class BoundArrayAccess(SyntaxNode syntax, BoundExpression array, ImmutableArray<BoundExpression> indices, TypeSymbol elementType)
    : BoundExpression(syntax, elementType)
{
    public BoundExpression Array { get; } = array;

    public ImmutableArray<BoundExpression> Indices { get; } = indices;
}

/// <summary>
/// An argument of a call, in source order (C# evaluates arguments in source order too), with the parameter it is
/// for and its name when the source named it.
/// </summary>
internal sealed record BoundArgument(ParameterSymbol Parameter, BoundExpression Value, string? Name);

/// <summary>A call; the receiver is null for a static function.</summary>
internal sealed class BoundCall(SyntaxNode syntax, BoundExpression? receiver, MethodSymbol method, ImmutableArray<BoundArgument> arguments)
    : BoundExpression(syntax, method.ReturnType)
{
    public BoundExpression? Receiver { get; } = receiver;

    public MethodSymbol Method { get; } = method;

    public ImmutableArray<BoundArgument> Arguments { get; } = arguments;
}

/// <summary><c>new T(args)</c>; the constructor is null for a struct created without arguments, which is all zeros.</summary>
internal sealed class BoundObjectCreation(SyntaxNode syntax, NamedTypeSymbol type, MethodSymbol? constructor, ImmutableArray<BoundArgument> arguments)
    : BoundExpression(syntax, type)
{
    public MethodSymbol? Constructor { get; } = constructor;

    public ImmutableArray<BoundArgument> Arguments { get; } = arguments;
}

/// <summary><c>new array&lt;T&gt;(n)</c>, <c>new array2d&lt;T&gt;(w, h)</c> (ADR-0020).</summary>
internal sealed class BoundArrayCreation(SyntaxNode syntax, ArrayTypeSymbol type, ImmutableArray<BoundExpression> lengths) : BoundExpression(syntax, type)
{
    public ImmutableArray<BoundExpression> Lengths { get; } = lengths;
}

internal enum UnaryOperatorKind
{
    Negate,
    Plus,
    BitwiseNot,
    LogicalNot,
}

internal sealed class BoundUnary(SyntaxNode syntax, UnaryOperatorKind operatorKind, BoundExpression operand, TypeSymbol type) : BoundExpression(syntax, type)
{
    public UnaryOperatorKind OperatorKind { get; } = operatorKind;

    public BoundExpression Operand { get; } = operand;
}

internal enum BinaryOperatorKind
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    LeftShift,
    RightShift,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Equal,
    NotEqual,
    BitwiseAnd,
    BitwiseOr,
    ExclusiveOr,
    LogicalAnd,
    LogicalOr,
}

/// <summary>A binary operation; both operands are already converted to the operator's operand types.</summary>
internal sealed class BoundBinary(SyntaxNode syntax, BinaryOperatorKind operatorKind, BoundExpression left, BoundExpression right, TypeSymbol type)
    : BoundExpression(syntax, type)
{
    public BinaryOperatorKind OperatorKind { get; } = operatorKind;

    public BoundExpression Left { get; } = left;

    public BoundExpression Right { get; } = right;
}

internal enum ConversionKind
{
    Identity,
    ImplicitNumeric,
    ImplicitReference,
    Boxing,
    ExplicitNumeric,
}

/// <summary>A conversion; <see cref="IsExplicit"/> for <c>x as T</c>, otherwise one that happens where it is needed.</summary>
internal sealed class BoundConversion(SyntaxNode syntax, BoundExpression operand, ConversionKind conversionKind, TypeSymbol type, bool isExplicit)
    : BoundExpression(syntax, type)
{
    public BoundExpression Operand { get; } = operand;

    public ConversionKind ConversionKind { get; } = conversionKind;

    public bool IsExplicit { get; } = isExplicit;
}

/// <summary>One piece of <c>$"..."</c>: text, or a hole with its optional alignment and format.</summary>
internal sealed record BoundInterpolationPart(string? Text, BoundExpression? Value, BoundExpression? Alignment, string? Format);

internal sealed class BoundInterpolatedString(SyntaxNode syntax, ImmutableArray<BoundInterpolationPart> parts, TypeSymbol stringType)
    : BoundExpression(syntax, stringType)
{
    public ImmutableArray<BoundInterpolationPart> Parts { get; } = parts;
}

/// <summary>A type name where a member access starts: <c>Console</c> in <c>Console.WriteLine</c>. Not a value.</summary>
internal sealed class BoundTypeExpression(SyntaxNode syntax, TypeSymbol type) : BoundExpression(syntax, type);

/// <summary>A namespace name in front of a type: <c>System</c> in <c>System.Console</c>. Not a value.</summary>
internal sealed class BoundNamespaceExpression(SyntaxNode syntax, NamespaceSymbol ns) : BoundExpression(syntax, ErrorTypeSymbol.Instance)
{
    public NamespaceSymbol Namespace { get; } = ns;
}

/// <summary>The functions a name refers to before a call picks one. Not a value.</summary>
internal sealed class BoundMethodGroup(SyntaxNode syntax, BoundExpression? receiver, string name, ImmutableArray<MethodSymbol> methods)
    : BoundExpression(syntax, ErrorTypeSymbol.Instance)
{
    public BoundExpression? Receiver { get; } = receiver;

    public string Name { get; } = name;

    public ImmutableArray<MethodSymbol> Methods { get; } = methods;
}

/// <summary>An expression that had an error, already reported. Its type is the error type, so nothing reports again.</summary>
internal sealed class BoundBadExpression(SyntaxNode syntax, ImmutableArray<BoundNode> children) : BoundExpression(syntax, ErrorTypeSymbol.Instance)
{
    public ImmutableArray<BoundNode> Children { get; } = children;
}
