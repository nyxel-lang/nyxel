using System.Collections.Immutable;
using System.Globalization;
using Nyxel.Compiler.Binding;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Lowering;

// match (docs/design/lowering.md, "match"): a statement becomes a C# switch statement, a value a switch expression.
// Patterns C# checks against the arms before them are written as C# patterns; the binder has reported every arm that
// C# would find covered already. The others (comparisons, ranges, 'and', 'not') go into a 'when', which C# doesn't
// look into. Locals are named so that C#'s rule against a name in a block and a block inside it holds.
internal sealed partial class Lowerer
{
    private const int SwitchPrecedence = 14;

    // The loops being lowered, innermost last, with how many switch statements were open around each: a Nyxel
    // 'break' inside a switch statement leaves the loop, which C#'s 'break' wouldn't, so it becomes a goto.
    private readonly List<LoopFrame> _loops = [];
    private int _switchDepth;

    private sealed class LoopFrame(int switchDepth)
    {
        public int SwitchDepth { get; } = switchDepth;

        public string? BreakLabel { get; set; }
    }

    private void EnterLoop() => _loops.Add(new LoopFrame(_switchDepth));

    /// <summary>After a loop: the label its breaks from inside a switch go to, when there were any.</summary>
    private void ExitLoop()
    {
        var frame = _loops[^1];
        _loops.RemoveAt(_loops.Count - 1);
        if (frame.BreakLabel is { } label)
        {
            _writer.WriteHidden($"{label}: ;");
        }
    }

    private string BreakText()
    {
        if (_loops.Count == 0 || _switchDepth <= _loops[^1].SwitchDepth)
        {
            return "break;";
        }
        var frame = _loops[^1];
        frame.BreakLabel ??= Temporary("break");
        return $"goto {frame.BreakLabel};";
    }

    // Statements ---------------------------------------------------------------------------------------------------

    private void LowerMatchStatement(BoundMatchStatement match) =>
        LowerSwitch((MatchExpressionSyntax)match.Syntax, match.Value, match.Arms, arm => LowerStatement(arm.Body!), arm => FlowAnalysis.CanComplete(arm.Body!));

    /// <summary>
    /// <c>let t = match x { case A -&gt; 1  case _ -&gt; return }</c>: C# can't leave from inside an expression, so the
    /// match becomes a switch statement before the statement, each arm setting a temporary or leaving; the statement
    /// then uses the temporary. The binder allows this only as a whole statement's value, under conversions at most.
    /// </summary>
    private BoundExpression LowerStatementValue(BoundStatement statement, BoundExpression value)
    {
        var conversions = new Stack<BoundConversion>();
        var inner = value;
        while (inner is BoundConversion conversion)
        {
            conversions.Push(conversion);
            inner = conversion.Operand;
        }
        if (inner is not BoundMatchExpression { Leaves: true } match)
        {
            return LowerCoalesceJump(statement, value);
        }
        var temporary = Temporary("value");
        _writer.WriteHidden($"{TypeName(match.Type)} {temporary};");
        LowerSwitch((MatchExpressionSyntax)match.Syntax, match.Value, match.Arms,
            arm =>
            {
                if (arm.Body is { } jump)
                {
                    LowerStatement(jump);
                }
                else
                {
                    _writer.WriteMapped(_source, ((MatchArmSyntax)arm.Syntax).Body.Span, $"{temporary} = {ArmValue(arm, match.Type)};");
                }
            },
            arm => arm.Body is null);

        BoundExpression result = new BoundTemporary(match.Syntax, temporary, match.Type);
        while (conversions.TryPop(out var conversion))
        {
            result = new BoundConversion(conversion.Syntax, result, conversion.ConversionKind, conversion.Type, conversion.IsExplicit);
        }
        return result;
    }

    /// <summary>
    /// A switch statement: one section per arm, a <c>default</c> that throws when the arms end without <c>case _</c>
    /// (a value named by no case of an enum, ADR-0024; the same exception as C#'s switch expression).
    /// </summary>
    private void LowerSwitch(MatchExpressionSyntax syntax, BoundExpression value, ImmutableArray<BoundMatchArm> arms,
        Action<BoundMatchArm> lowerBody, Func<BoundMatchArm, bool> canComplete)
    {
        _writer.WriteMapped(_source, Header(syntax.MatchKeyword, syntax.Expression), $"switch ({Expression(value)})");
        _writer.WriteHidden("{");
        _writer.Indent();
        _switchDepth++;
        string? temporary = null;
        var hasDefault = false;
        foreach (var arm in arms)
        {
            var armSyntax = (MatchArmSyntax)arm.Syntax;
            string label;
            if (arm.Pattern is BoundDiscardPattern && arm.When is null)
            {
                label = "default:";
                hasDefault = true;
            }
            else
            {
                var pattern = arm.Pattern is BoundDiscardPattern ? "var _" : ArmPattern(arm, value.Type, ref temporary);
                label = $"case {pattern}:";
            }
            _writer.WriteMapped(_source, Header(armSyntax.Keyword, (SyntaxNode?)armSyntax.WhenClause ?? armSyntax.Pattern), label);
            _writer.Indent();
            lowerBody(arm);
            if (canComplete(arm))
            {
                _writer.WriteHidden("break;");
            }
            _writer.Unindent();
        }
        if (!hasDefault)
        {
            _writer.WriteHidden("default:");
            _writer.Indent();
            _writer.WriteHidden($"throw {Unmatched(value, reevaluate: true)};");
            _writer.Unindent();
        }
        _switchDepth--;
        _writer.Unindent();
        _writer.WriteHidden("}");
    }

    /// <summary>
    /// The exception for a value no arm matched, with the value when it can be read again without running anything
    /// (a local, a parameter, <c>self</c>).
    /// </summary>
    private string Unmatched(BoundExpression value, bool reevaluate)
    {
        var argument = reevaluate && value is BoundLocalAccess or BoundParameterAccess or BoundSelf ? Expression(value) : "";
        return $"new global::System.Runtime.CompilerServices.SwitchExpressionException({argument})";
    }

    // Expressions --------------------------------------------------------------------------------------------------

    /// <summary>
    /// A switch expression. One that no arm matches throws C#'s <c>SwitchExpressionException</c> by itself, which is
    /// what a value named by no case of an enum gets (ADR-0024).
    /// </summary>
    private (string Text, int Precedence) MatchExpression(BoundMatchExpression match)
    {
        if (match.Leaves)
        {
            throw new InvalidOperationException("A match whose arms leave inside a larger expression; binding should have reported it.");
        }
        string? temporary = null;
        var arms = match.Arms.Select(arm => $"{ArmPattern(arm, match.Value.Type, ref temporary)} => {ArmValue(arm, match.Type)}");
        return ($"{Expression(match.Value, UnaryPrecedence)} switch {{ {string.Join(", ", arms)} }}", SwitchPrecedence);
    }

    /// <summary>
    /// The value of an arm, or the <c>throw</c> in its place. A reference conversion is written as a cast: C# gives a
    /// switch expression the arms' common type, and an arm of a derived type would otherwise decide it.
    /// </summary>
    private string ArmValue(BoundMatchArm arm, TypeSymbol type) => arm switch
    {
        { Body: BoundThrow thrown } => $"throw {Expression(thrown.Exception)}",
        { Value: BoundConversion { IsExplicit: false, ConversionKind: ConversionKind.ImplicitReference or ConversionKind.Boxing } conversion } =>
            $"({TypeName(type)}){Expression(conversion.Operand, UnaryPrecedence)}",
        _ => Expression(arm.Value!),
    };

    /// <summary>
    /// The pattern of an arm with its <c>when</c>, for a case label or a switch expression arm: a C# pattern when C#
    /// checks it against the arms before it; otherwise the value is taken whole (<c>var t</c>) and tested in the
    /// <c>when</c>, where C# doesn't compare it with the other arms.
    /// </summary>
    private string ArmPattern(BoundMatchArm arm, TypeSymbol input, ref string? temporary)
    {
        var when = arm.When is { } condition ? Expression(condition, LogicalAndPrecedence + 1) : null;
        if (MethodBinder.IsVisiblePattern(arm.Pattern))
        {
            var pattern = PatternText(arm.Pattern, input, 0);
            return when is null ? pattern : $"{pattern} when {when}";
        }
        temporary ??= Temporary("match");
        var test = $"{temporary} is {PatternText(arm.Pattern, input, 0)}";
        return $"var {temporary} when {(when is null ? test : $"{test} && {when}")}";
    }

    private const int LogicalAndPrecedence = 6;

    // Precedence of patterns in C#: or < and < not < primary.
    private const int OrPatternPrecedence = 1;
    private const int AndPatternPrecedence = 2;
    private const int NotPatternPrecedence = 3;

    private string PatternText(BoundPattern pattern, TypeSymbol input, int parentPrecedence)
    {
        var (text, precedence) = pattern switch
        {
            BoundDiscardPattern => ("_", 4),
            BoundNullPattern => ("null", 4),
            BoundNotNullPattern => ("not null", NotPatternPrecedence),
            BoundConstantPattern constant => (PatternConstant(constant.Value), 4),
            BoundCasePattern casePattern => (CasePatternText(casePattern), 4),
            // A type the value always is (other than by null) is 'not null'. To C# 15 a type on a struct enum's value
            // would test its case's type (ADR-0046).
            BoundTypePattern typePattern when Conversions.ClassifyImplicit(input.NonNullable, typePattern.Type)
                is ConversionKind.Identity or ConversionKind.ImplicitReference or ConversionKind.Boxing =>
                input.IsValueType && !input.IsNullable ? ("_", 4) : ("not null", NotPatternPrecedence),
            BoundTypePattern typePattern => (TypeName(typePattern.Type), 4),
            BoundRelationalPattern relational => ($"{RelationalText(relational.OperatorKind)} {PatternConstant(relational.Value)}", 4),
            BoundRangePattern range => ($">= {PatternConstant(range.Lower)} and {(range.IncludesUpper ? "<=" : "<")} {PatternConstant(range.Upper)}", AndPatternPrecedence),
            BoundNotPattern not => ($"not {PatternText(not.Pattern, input, NotPatternPrecedence)}", NotPatternPrecedence),
            BoundBinaryPattern { IsAnd: true } and => ($"{PatternText(and.Left, input, AndPatternPrecedence)} and {PatternText(and.Right, input, AndPatternPrecedence + 1)}", AndPatternPrecedence),
            BoundBinaryPattern or => ($"{PatternText(or.Left, input, OrPatternPrecedence)} or {PatternText(or.Right, input, OrPatternPrecedence + 1)}", OrPatternPrecedence),
            _ => throw new InvalidOperationException($"{pattern.GetType().Name} can't be lowered; binding should have reported an error."),
        };
        return precedence < parentPrecedence ? $"({text})" : text;
    }

    private static string RelationalText(BinaryOperatorKind operatorKind) => operatorKind switch
    {
        BinaryOperatorKind.Less => "<",
        BinaryOperatorKind.LessOrEqual => "<=",
        BinaryOperatorKind.Greater => ">",
        _ => ">=",
    };

    /// <summary>
    /// A constant in a pattern: an enum's by its name; a number as written in C#, where a constant converts to the
    /// type matched by itself (<c>5</c> for a <c>byte</c>, not the cast <c>(byte)5</c>).
    /// </summary>
    private string PatternConstant(BoundExpression value)
    {
        while (value is BoundConversion { ConversionKind: ConversionKind.Boxing or ConversionKind.ImplicitReference } conversion)
        {
            value = conversion.Operand;
        }
        if (value.Type.TypeKind == TypeKind.Enum)
        {
            return Expression(value, UnaryPrecedence);
        }
        return value.ConstantValue switch
        {
            sbyte or byte or short or ushort => string.Format(CultureInfo.InvariantCulture, "{0}", value.ConstantValue),
            var constant => Literal(constant).Text,
        };
    }

    /// <summary>
    /// A case by C#'s means (ADR-0046): a constant of an enum without data (an alias is a const of its extension
    /// class, ADR-0045); the type of a struct enum's or class enum's case, its data taken out by its <c>Deconstruct</c>.
    /// </summary>
    private string CasePatternText(BoundCasePattern pattern)
    {
        switch (pattern.Case)
        {
            case SourceEnumCaseSymbol { ContainingType.IsDataEnum: true } dataCase:
                var data = pattern.Data.IsDefaultOrEmpty ? "" : $"({string.Join(", ", pattern.Data.Select(d => d is null ? "_" : $"var {LocalName(d)}"))})";
                return CaseTypeName(dataCase) + data;
            case SourceFieldSymbol alias:
                return $"{ExtensionsName(alias.ContainingType)}.{Escape(alias.Name)}";
            default:
                return $"{TypeName(pattern.Case.ContainingType)}.{Escape(pattern.Case.Name)}";
        }
    }

    // Names of locals --------------------------------------------------------------------------------------------

    /// <summary>
    /// The C# names of a body's locals. C# doesn't allow a name in a block when a block inside it has a local of that
    /// name, even an earlier one that is gone (CS0136); Nyxel only looks at the names visible (ADR-0036). So a local
    /// whose name a block inside its own already used gets a new one: <c>a</c> then <c>a_2</c>. Names C# has around
    /// the body (<paramref name="taken"/>: a setter's <c>value</c>) are renamed wherever they appear; a new name is
    /// none of the body's locals and <paramref name="parameters"/>.
    /// </summary>
    private static Dictionary<LocalSymbol, string> PlanLocalNames(BoundBlock body, IReadOnlyCollection<string> taken, IEnumerable<string> parameters)
    {
        var names = new Dictionary<LocalSymbol, string>();
        var used = new HashSet<string>(taken.Concat(parameters));
        foreach (var local in body.DescendantsAndSelf().SelectMany(DeclaredLocals))
        {
            used.Add(local.Name);
        }
        // Per open scope: the names declared in it, and those of the scopes inside it that are closed already.
        var scopes = new List<(HashSet<string> Own, HashSet<string> Inner)> { ([.. taken], []) };
        Walk(body);
        return names;

        void Walk(BoundNode? node)
        {
            switch (node)
            {
                case null:
                    return;
                case BoundBlock block:
                    InScope(() => Visit(block.Statements));
                    return;
                case BoundForRange range:
                    InScope(() =>
                    {
                        Walk(range.From);
                        Walk(range.To);
                        Declare(range.Variable);
                        Walk(range.Body);
                    });
                    return;
                case BoundForEach each:
                    Walk(each.Collection);
                    InScope(() =>
                    {
                        Declare(each.Variable);
                        Walk(each.Body);
                    });
                    return;
                case BoundMatchArm arm:
                    InScope(() =>
                    {
                        foreach (var local in DeclaredLocals(arm))
                        {
                            Declare(local);
                        }
                        Walk(arm.When);
                        Walk(arm.Body);
                        Walk(arm.Value);
                    });
                    return;
                case BoundLocalDeclaration declaration:
                    Walk(declaration.Initializer);
                    Declare(declaration.Local);
                    return;
                default:
                    Visit(node.GetChildren());
                    return;
            }
        }

        void Visit(IEnumerable<BoundNode?> nodes)
        {
            foreach (var node in nodes)
            {
                Walk(node);
            }
        }

        void InScope(Action walk)
        {
            scopes.Add(([], []));
            walk();
            var (own, inner) = scopes[^1];
            scopes.RemoveAt(scopes.Count - 1);
            scopes[^1].Inner.UnionWith(own);
            scopes[^1].Inner.UnionWith(inner);
        }

        // A name clashes with one a closed block inside this one declared, or one declared around it (only C#'s own,
        // as Nyxel reports a visible one). Sibling blocks may share names.
        void Declare(LocalSymbol local)
        {
            var name = local.Name;
            if (scopes[^1].Inner.Contains(name) || scopes.Any(s => s.Own.Contains(name)))
            {
                name = Enumerable.Range(2, used.Count + 1).Select(i => $"{local.Name}_{i}").First(n => !used.Contains(n));
                used.Add(name);
                names[local] = name;
            }
            scopes[^1].Own.Add(name);
        }
    }

    /// <summary>The locals a node declares itself: a <c>let</c> / <c>var</c>, a loop's variable, the data of an arm's case.</summary>
    private static IEnumerable<LocalSymbol> DeclaredLocals(BoundNode node) => node switch
    {
        BoundLocalDeclaration declaration => [declaration.Local],
        BoundForRange range => [range.Variable],
        BoundForEach each => [each.Variable],
        BoundMatchArm { Pattern: BoundCasePattern casePattern } => casePattern.Data.IsDefault ? [] : casePattern.Data.OfType<LocalSymbol>(),
        _ => [],
    };
}
