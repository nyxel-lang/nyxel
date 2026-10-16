using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Syntax;

internal sealed partial class Parser
{
    private enum MemberContext
    {
        TopLevel,
        Type,
        Extension,
        Enum,
    }

    private enum ParameterContext
    {
        Function,
        Lambda,
        EnumCase,
        Event,
        Indexer,
    }

    // File structure (ADR-0012): namespace, imports, declarations, in this order. Misplaced namespaces and imports
    // are reported and kept as skipped trivia, so the typed tree stays in the fixed order.
    private CompilationUnitSyntax ParseCompilationUnit()
    {
        NamespaceDeclarationSyntax? @namespace = null;
        var imports = new List<ImportDirectiveSyntax>();
        var members = new List<MemberDeclarationSyntax>();

        while (Current.Kind != SyntaxKind.EndOfFileToken)
        {
            var start = _position;
            var errors = ErrorCount;
            AllowLineBreak();
            switch (Current.Kind)
            {
                case SyntaxKind.NamespaceKeyword:
                    {
                        var declaration = new NamespaceDeclarationSyntax(NextToken(), ParseQualifiedName());
                        if (@namespace != null)
                        {
                            Report(DiagnosticDescriptors.DuplicateNamespace, declaration.Span);
                            SkipNode(declaration);
                        }
                        else if (imports.Count > 0 || members.Count > 0)
                        {
                            Report(DiagnosticDescriptors.NamespaceNotFirst, declaration.Span);
                            SkipNode(declaration);
                        }
                        else
                        {
                            @namespace = declaration;
                        }
                        break;
                    }
                case SyntaxKind.ImportKeyword:
                case SyntaxKind.UsingKeyword:
                    {
                        var keyword = NextToken();
                        var name = ParseQualifiedName();
                        if (keyword.Kind == SyntaxKind.UsingKeyword)
                        {
                            Report(DiagnosticDescriptors.UsingDirective, keyword.Span, name.ToString());
                        }
                        var import = new ImportDirectiveSyntax(keyword, name);
                        if (members.Count > 0)
                        {
                            Report(DiagnosticDescriptors.ImportAfterDeclaration, import.Span);
                            SkipNode(import);
                        }
                        else
                        {
                            imports.Add(import);
                        }
                        break;
                    }
                default:
                    if (ParseMemberDeclaration(MemberContext.TopLevel) is { } member)
                    {
                        members.Add(member);
                    }
                    break;
            }
            ExpectEndOfStatement(errors, "declaration");
            if (_position == start)
            {
                SkipCurrentToken();
            }
        }

        var unit = new CompilationUnitSyntax(@namespace, MakeList(imports), MakeList(members), NextToken());
        CheckRanges(unit);
        CheckEvalBlocks(unit);
        return unit;
    }

    /// <summary>A dotted name in a namespace or import: <c>System.Collections.Generic</c>.</summary>
    private NameSyntax ParseQualifiedName()
    {
        NameSyntax name = new IdentifierNameSyntax(ExpectIdentifier());
        while (At(SyntaxKind.DotToken))
        {
            var dot = NextToken();
            CheckOperatorAtLineEnd(dot);
            name = new QualifiedNameSyntax(name, dot, new IdentifierNameSyntax(ExpectIdentifier()));
        }
        return name;
    }

    // Members --------------------------------------------------------------------------------------------------

    /// <summary>Parses one declaration with its modifiers, or reports and skips the line and returns null.</summary>
    private MemberDeclarationSyntax? ParseMemberDeclaration(MemberContext context)
    {
        SkipFlagsAttribute();
        var first = Current;
        var modifiers = ParseModifiers();
        // Members are private by default; 'private' is only written on accessors (ADR-0026).
        if (modifiers.FirstOrDefault(m => m.Kind == SyntaxKind.PrivateKeyword) is { } privateKeyword)
        {
            Report(DiagnosticDescriptors.PrivateMember, privateKeyword.Span);
        }
        MemberDeclarationSyntax? member = Current.Kind switch
        {
            _ when modifiers.Count > 0 && AtLineBreak => null,
            _ when AtEnumDeclaration() => ParseEnumDeclaration(modifiers),
            SyntaxKind.ClassKeyword or SyntaxKind.StructKeyword or SyntaxKind.InterfaceKeyword => ParseTypeDeclaration(modifiers),
            SyntaxKind.ExtensionKeyword => ParseExtensionDeclaration(modifiers),
            SyntaxKind.LetKeyword or SyntaxKind.VarKeyword => ParseFieldDeclaration(modifiers),
            SyntaxKind.FuncKeyword => ParseFunctionDeclaration(modifiers),
            SyntaxKind.InitKeyword => ParseInitDeclaration(modifiers),
            SyntaxKind.PropertyKeyword => ParsePropertyDeclaration(modifiers),
            SyntaxKind.EventKeyword => ParseEventDeclaration(modifiers),
            _ => null,
        };

        if (member == null)
        {
            ReportExpected(context == MemberContext.Enum ? "'case' or a declaration" : "a declaration", atLineStart: modifiers.Count == 0);
            SkipConsumed(modifiers);
            if (modifiers.Count == 0)
            {
                SkipToEndOfLine();
            }
            return null;
        }

        var isType = member.Kind is SyntaxKind.ClassDeclaration or SyntaxKind.StructDeclaration
            or SyntaxKind.InterfaceDeclaration or SyntaxKind.EnumDeclaration or SyntaxKind.ExtensionDeclaration;
        if (context == MemberContext.TopLevel && !isType)
        {
            var what = member.Kind switch
            {
                SyntaxKind.FieldDeclaration => "field",
                SyntaxKind.FunctionDeclaration => "function",
                SyntaxKind.InitDeclaration => "init",
                SyntaxKind.PropertyDeclaration => "property",
                SyntaxKind.IndexerDeclaration => "indexer",
                _ => "event",
            };
            Report(DiagnosticDescriptors.TopLevelMember, first.Span, what);
        }
        else if (context != MemberContext.TopLevel && isType)
        {
            Report(DiagnosticDescriptors.NestedType, first.Span);
        }
        else if (context == MemberContext.Extension && member is IndexerDeclarationSyntax)
        {
            Report(DiagnosticDescriptors.IndexerNotAllowed, first.Span, "be declared in an extension yet; C# 14 extensions don't support indexers");
        }
        return member;
    }

    /// <summary>C#'s <c>[Flags]</c> on the line before an enum: say to write <c>flags enum</c> (ADR-0024).</summary>
    private void SkipFlagsAttribute()
    {
        if (Current.Kind == SyntaxKind.OpenBracketToken && Peek(1).Kind == SyntaxKind.IdentifierToken
            && Peek(1).Text == "Flags" && Peek(2).Kind == SyntaxKind.CloseBracketToken)
        {
            Report(DiagnosticDescriptors.FlagsAttribute, TextSpan.FromBounds(Current.Span.Start, Peek(2).Span.End));
            for (var i = 0; i < 3; i++)
            {
                SkipCurrentToken();
            }
        }
    }

    /// <summary><c>enum</c>, or <c>flags</c> / <c>struct</c> followed by <c>enum</c> on the same line.</summary>
    private bool AtEnumDeclaration()
    {
        var kindWord = Current.Kind == SyntaxKind.StructKeyword
            || (Current.Kind == SyntaxKind.IdentifierToken && Current.Text == SyntaxFacts.FlagsContextualKeyword);
        return Current.Kind == SyntaxKind.EnumKeyword
            || (kindWord && Peek(1).Kind == SyntaxKind.EnumKeyword && !Peek(1).HasLeadingLineBreak);
    }

    /// <summary>Modifiers in the fixed order of ADR-0019; the order and repeats are checked here.</summary>
    private SyntaxList<SyntaxToken> ParseModifiers()
    {
        var modifiers = new List<SyntaxToken>();
        var orderReported = false;
        while (SyntaxFacts.IsModifier(Current.Kind) && (modifiers.Count == 0 || !AtLineBreak))
        {
            var modifier = NextToken();
            if (modifiers.Any(m => m.Kind == modifier.Kind))
            {
                Report(DiagnosticDescriptors.DuplicateModifier, modifier.Span, modifier.Text);
            }
            else if (!orderReported && modifiers.Any(m => SyntaxFacts.GetModifierRank(m.Kind) > SyntaxFacts.GetModifierRank(modifier.Kind)))
            {
                orderReported = true;
                var all = ScanModifierTexts(modifiers, modifier);
                Report(DiagnosticDescriptors.ModifierOrder, modifier.Span, all);
            }
            modifiers.Add(modifier);
        }
        return MakeList(modifiers);
    }

    /// <summary>All modifiers of the declaration (including those not read yet), sorted into the right order.</summary>
    private string ScanModifierTexts(List<SyntaxToken> read, SyntaxToken current)
    {
        var kinds = read.Select(m => m.Kind).Append(current.Kind).ToList();
        for (var offset = 0; SyntaxFacts.IsModifier(Peek(offset).Kind) && !Peek(offset).HasLeadingLineBreak; offset++)
        {
            kinds.Add(Peek(offset).Kind);
        }
        return string.Join(' ', kinds.Distinct().OrderBy(SyntaxFacts.GetModifierRank).Select(k => SyntaxFacts.GetText(k)));
    }

    private TypeDeclarationSyntax ParseTypeDeclaration(SyntaxList<SyntaxToken> modifiers)
    {
        var keyword = NextToken();
        var kind = keyword.Kind switch
        {
            SyntaxKind.ClassKeyword => SyntaxKind.ClassDeclaration,
            SyntaxKind.StructKeyword => SyntaxKind.StructDeclaration,
            _ => SyntaxKind.InterfaceDeclaration,
        };
        var identifier = ExpectIdentifier();
        var typeParameters = At(SyntaxKind.LessThanToken) ? ParseTypeParameterList() : null;
        var baseList = At(SyntaxKind.ColonToken) ? ParseBaseList() : null;
        var (open, members, close) = ParseMemberBody(MemberContext.Type);
        return new TypeDeclarationSyntax(kind, modifiers, keyword, identifier, typeParameters, baseList, open, members, close);
    }

    private BaseListSyntax ParseBaseList()
    {
        var colon = NextToken();
        var types = new List<SyntaxNode> { ParseType() };
        while (At(SyntaxKind.CommaToken))
        {
            types.Add(NextToken());
            types.Add(ParseType());
        }
        return new BaseListSyntax(colon, Separated<TypeSyntax>(types));
    }

    private ExtensionDeclarationSyntax ParseExtensionDeclaration(SyntaxList<SyntaxToken> modifiers)
    {
        var keyword = NextToken();
        var type = ParseType();
        var (open, members, close) = ParseMemberBody(MemberContext.Extension);
        return new ExtensionDeclarationSyntax(modifiers, keyword, type, open, members, close);
    }

    private (SyntaxToken Open, SyntaxList<MemberDeclarationSyntax> Members, SyntaxToken Close) ParseMemberBody(MemberContext context)
    {
        var open = ExpectOpenBrace();
        var saved = _bracketDepth;
        _bracketDepth = 0;
        var members = new List<MemberDeclarationSyntax>();
        if (!open.IsMissing)
        {
            while (Current.Kind is not (SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken))
            {
                var start = _position;
                var errors = ErrorCount;
                AllowLineBreak();
                if (ParseMemberDeclaration(context) is { } member)
                {
                    members.Add(member);
                }
                ExpectEndOfStatement(errors, "declaration");
                if (_position == start)
                {
                    SkipCurrentToken();
                }
            }
        }
        var close = ExpectCloseBrace(open);
        _bracketDepth = saved;
        return (open, MakeList(members), close);
    }

    private EnumDeclarationSyntax ParseEnumDeclaration(SyntaxList<SyntaxToken> modifiers)
    {
        var kindKeyword = Current.Kind == SyntaxKind.EnumKeyword ? null : NextToken();
        var keyword = NextToken();
        var identifier = ExpectIdentifier();
        var baseList = At(SyntaxKind.ColonToken) ? ParseBaseList() : null;
        var open = ExpectOpenBrace();
        var saved = _bracketDepth;
        _bracketDepth = 0;
        var cases = new List<EnumCaseDeclarationSyntax>();
        var members = new List<MemberDeclarationSyntax>();
        if (!open.IsMissing)
        {
            while (Current.Kind is not (SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken))
            {
                var start = _position;
                var errors = ErrorCount;
                AllowLineBreak();
                var isCase = Current.Kind == SyntaxKind.CaseKeyword;
                if (isCase)
                {
                    var caseKeyword = NextToken();
                    var name = ExpectIdentifier();
                    var parameters = At(SyntaxKind.OpenParenToken) ? ParseParameterList(ParameterContext.EnumCase) : null;
                    var @case = new EnumCaseDeclarationSyntax(caseKeyword, name, parameters, TryParseInitializer());
                    // The tree keeps the cases first (ADR-0029); a case after the members is reported and skipped.
                    if (members.Count > 0)
                    {
                        Report(DiagnosticDescriptors.EnumCaseAfterMember, @case.Span);
                        SkipNode(@case);
                    }
                    else
                    {
                        cases.Add(@case);
                    }
                }
                else if (ParseMemberDeclaration(MemberContext.Enum) is { } member)
                {
                    CheckEnumMember(member);
                    members.Add(member);
                }
                ExpectEndOfStatement(errors, isCase ? "case" : "declaration");
                if (_position == start)
                {
                    SkipCurrentToken();
                }
            }
        }
        var close = ExpectCloseBrace(open);
        _bracketDepth = saved;
        var declaration = new EnumDeclarationSyntax(
            modifiers, kindKeyword, keyword, identifier, baseList, open, MakeList(cases), MakeList(members), close);
        CheckEnum(declaration);
        return declaration;
    }

    /// <summary>
    /// An enum has functions and computed properties (ADR-0029): its values are its cases, which don't change and
    /// can't be inherited.
    /// </summary>
    private void CheckEnumMember(MemberDeclarationSyntax member)
    {
        var storage = member switch
        {
            FieldDeclarationSyntax => "fields",
            InitDeclarationSyntax => "an init",
            EventDeclarationSyntax => "events",
            IndexerDeclarationSyntax => "an indexer",
            PropertyDeclarationSyntax property when property.Initializer != null
                || property.AccessorList.Accessors.Any(a => a.Body == null && a.ExpressionBody == null) => "auto properties",
            _ => null,
        };
        if (storage != null)
        {
            Report(DiagnosticDescriptors.EnumMemberKind, member.Span, storage);
        }
        foreach (var modifier in member.Modifiers.Where(m => m.Kind is SyntaxKind.ProtectedKeyword or SyntaxKind.VirtualKeyword
            or SyntaxKind.AbstractKeyword or SyntaxKind.SealedKeyword or SyntaxKind.OverrideKeyword))
        {
            Report(DiagnosticDescriptors.EnumMemberModifier, modifier.Span, modifier.Text);
        }
        if (member is FunctionDeclarationSyntax { ParameterList.Parameters: [SelfParameterSyntax { VarKeyword: { } varKeyword }, ..] })
        {
            Report(DiagnosticDescriptors.EnumVarSelf, varKeyword.Span);
        }
    }

    /// <summary>
    /// The rules of ADR-0024 and ADR-0029 that the declaration alone shows. A plain enum without data is a C# enum:
    /// only it has an underlying type, case values or 'flags', and it implements no interfaces. Its values are all
    /// given or none (a flags enum gives all). A 'struct enum' has data or implements interfaces.
    /// </summary>
    private void CheckEnum(EnumDeclarationSyntax declaration)
    {
        var hasData = declaration.Cases.Any(c => c.ParameterList != null);
        var baseTypes = declaration.BaseList?.Types ?? SeparatedSyntaxList<TypeSyntax>.Empty;
        // A named type after the colon is an interface; an underlying type is a keyword such as 'byte'.
        var hasInterfaces = baseTypes.Any(t => t is not PredefinedTypeSyntax);
        var isCSharpEnum = !hasData && !declaration.IsStruct;
        if (declaration.IsStruct && !hasData && !hasInterfaces)
        {
            Report(DiagnosticDescriptors.StructEnumWithoutData, declaration.KindKeyword!.Span);
            isCSharpEnum = true;    // check the rest as the 'enum' it should be
        }
        for (var i = 0; i < baseTypes.Count; i++)
        {
            var type = baseTypes[i];
            var isIntegerType = type is PredefinedTypeSyntax { Keyword.Kind: var kind } && SyntaxFacts.IsIntegerType(kind);
            if (!isCSharpEnum)
            {
                // Other types are interfaces, which semantic analysis checks.
                if (isIntegerType)
                {
                    Report(DiagnosticDescriptors.EnumWithDataValues, type.Span, "have an underlying type");
                }
            }
            else if (type is not PredefinedTypeSyntax)
            {
                Report(DiagnosticDescriptors.EnumInterface, type.Span, type.ToString());
            }
            else if (i > 0 || !isIntegerType)
            {
                Report(DiagnosticDescriptors.EnumUnderlyingType, type.Span);
            }
        }
        if (!isCSharpEnum)
        {
            if (declaration.IsFlags)
            {
                Report(DiagnosticDescriptors.EnumWithDataValues, declaration.KindKeyword!.Span, "be a flags enum");
            }
            foreach (var @case in declaration.Cases.Where(c => c.EqualsValue != null))
            {
                Report(DiagnosticDescriptors.EnumWithDataValues, @case.EqualsValue!.Span, "give its cases values");
            }
            return;
        }
        if (declaration.IsFlags || declaration.Cases.Any(c => c.EqualsValue != null))
        {
            var descriptor = declaration.IsFlags ? DiagnosticDescriptors.FlagsCaseValue : DiagnosticDescriptors.EnumCaseValue;
            foreach (var @case in declaration.Cases.Where(c => c.EqualsValue == null && !c.Identifier.IsMissing))
            {
                Report(descriptor, @case.Identifier.Span, @case.Identifier.Text);
            }
        }
    }

    private FieldDeclarationSyntax ParseFieldDeclaration(SyntaxList<SyntaxToken> modifiers)
    {
        var keyword = NextToken();
        var identifier = ExpectIdentifier();
        var typeAnnotation = TryParseTypeAnnotation();
        if (typeAnnotation == null && !identifier.IsMissing)
        {
            Report(DiagnosticDescriptors.FieldNeedsType, identifier.Span, identifier.Text);
        }
        var initializer = TryParseInitializer();
        return new FieldDeclarationSyntax(modifiers, keyword, identifier, typeAnnotation, initializer);
    }

    private FunctionDeclarationSyntax ParseFunctionDeclaration(SyntaxList<SyntaxToken> modifiers)
    {
        var funcKeyword = NextToken();
        var identifier = ExpectIdentifier();
        var typeParameters = At(SyntaxKind.LessThanToken) ? ParseTypeParameterList() : null;
        var parameters = ParseParameterList(ParameterContext.Function);
        var returnType = TryParseReturnType();
        var (body, expressionBody) = ParseFunctionBody(required: false);
        return new FunctionDeclarationSyntax(modifiers, funcKeyword, identifier, typeParameters, parameters, returnType, body, expressionBody);
    }

    /// <summary>
    /// A block, or <c>= expression</c> (also for lambdas). A '{' on the next line is reported but still read as the
    /// body: nothing else can start with '{' there. Without a body: nothing, or an error when one is required.
    /// </summary>
    private (BlockSyntax? Body, ExpressionBodySyntax? ExpressionBody) ParseFunctionBody(bool required)
    {
        if (Current.Kind == SyntaxKind.OpenBraceToken)
        {
            return (ParseBlock(), null);
        }
        if (At(SyntaxKind.EqualsToken) || At(SyntaxKind.EqualsGreaterThanToken))
        {
            if (Current.Kind == SyntaxKind.EqualsGreaterThanToken)
            {
                Report(DiagnosticDescriptors.FatArrow, Current.Span);
            }
            var equals = NextToken();
            AllowLineBreak();
            return (null, new ExpressionBodySyntax(equals, ParseExpression()));
        }
        if (required)
        {
            ReportExpected("'{' or '='");
            return (null, new ExpressionBodySyntax(Missing(SyntaxKind.EqualsToken), MissingExpression()));
        }
        return (null, null);
    }

    private InitDeclarationSyntax ParseInitDeclaration(SyntaxList<SyntaxToken> modifiers)
    {
        var initKeyword = NextToken();
        var identifier = TryEat(SyntaxKind.IdentifierToken);
        var parameters = ParseParameterList(ParameterContext.Function);
        return new InitDeclarationSyntax(modifiers, initKeyword, identifier, parameters, ParseBlock());
    }

    private MemberDeclarationSyntax ParsePropertyDeclaration(SyntaxList<SyntaxToken> modifiers)
    {
        var propertyKeyword = NextToken();
        if (At(SyntaxKind.SelfKeyword))
        {
            return ParseIndexerDeclaration(modifiers, propertyKeyword);
        }
        var identifier = ExpectIdentifier();
        var typeAnnotation = ExpectTypeAnnotation();
        var initializer = TryParseInitializer();
        var accessors = ParseAccessorList();
        return new PropertyDeclarationSyntax(modifiers, propertyKeyword, identifier, typeAnnotation, initializer, accessors);
    }

    /// <summary><c>property self[x: int, y: int]: T { accessors }</c> (ADR-0029).</summary>
    private IndexerDeclarationSyntax ParseIndexerDeclaration(SyntaxList<SyntaxToken> modifiers, SyntaxToken propertyKeyword)
    {
        var selfKeyword = NextToken();
        var parameters = ParseBracketedParameterList();
        var typeAnnotation = ExpectTypeAnnotation();
        if (TryParseInitializer() is { } initializer)
        {
            Report(DiagnosticDescriptors.IndexerNotAllowed, initializer.Span, "have an initial value; its accessors have bodies");
            SkipNode(initializer);
        }
        var accessors = ParseAccessorList();
        if (modifiers.FirstOrDefault(m => m.Kind == SyntaxKind.StaticKeyword) is { } staticKeyword)
        {
            Report(DiagnosticDescriptors.IndexerNotAllowed, staticKeyword.Span, "be 'static'");
        }
        return new IndexerDeclarationSyntax(modifiers, propertyKeyword, selfKeyword, parameters, typeAnnotation, accessors);
    }

    private BracketedParameterListSyntax ParseBracketedParameterList()
    {
        var open = Expect(SyntaxKind.OpenBracketToken);
        if (open.IsMissing)
        {
            return new BracketedParameterListSyntax(open, SeparatedSyntaxList<BaseParameterSyntax>.Empty, Missing(SyntaxKind.CloseBracketToken));
        }
        _bracketDepth++;
        var parameters = ParseSeparatedList(SyntaxKind.CloseBracketToken, index => ParseParameter(ParameterContext.Indexer, index));
        var close = Expect(SyntaxKind.CloseBracketToken);
        _bracketDepth--;
        if (parameters.Count == 0 && !close.IsMissing)
        {
            Report(DiagnosticDescriptors.IndexerWithoutParameters, TextSpan.FromBounds(open.Span.Start, close.Span.End));
        }
        return new BracketedParameterListSyntax(open, parameters, close);
    }

    private AccessorListSyntax ParseAccessorList()
    {
        var open = ExpectOpenBrace();
        var saved = _bracketDepth;
        _bracketDepth = 0;
        var accessors = new List<AccessorDeclarationSyntax>();
        if (!open.IsMissing)
        {
            while (Current.Kind is not (SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken))
            {
                var start = _position;
                var errors = ErrorCount;
                AllowLineBreak();
                if (ParseAccessor() is { } accessor)
                {
                    accessors.Add(accessor);
                }
                ExpectEndOfStatement(errors, "accessor");
                if (_position == start)
                {
                    SkipCurrentToken();
                }
            }
        }
        var close = ExpectCloseBrace(open);
        _bracketDepth = saved;
        return new AccessorListSyntax(open, MakeList(accessors), close);
    }

    private AccessorDeclarationSyntax? ParseAccessor()
    {
        var modifiers = ParseModifiers();
        var isGet = Current.Kind == SyntaxKind.IdentifierToken && Current.Text == SyntaxFacts.GetContextualKeyword;
        var isSet = Current.Kind == SyntaxKind.IdentifierToken && Current.Text == SyntaxFacts.SetContextualKeyword;
        if ((!isGet && !isSet) || (modifiers.Count > 0 && AtLineBreak))
        {
            ReportExpected("'get' or 'set'", atLineStart: modifiers.Count == 0);
            SkipConsumed(modifiers);
            if (!AtStatementEnd)
            {
                SkipToEndOfLine();
            }
            return null;
        }
        var keyword = NextToken();
        SetterParameterSyntax? parameter = null;
        if (isSet && At(SyntaxKind.OpenParenToken))
        {
            var openParen = NextToken();
            parameter = new SetterParameterSyntax(openParen, ExpectIdentifier(), Expect(SyntaxKind.CloseParenToken));
        }
        // A block, or '= expression' as for a function (ADR-0026); a setter's body is a statement, so only a block.
        var (body, expressionBody) = ParseFunctionBody(required: false);
        if (isSet && expressionBody != null)
        {
            Report(DiagnosticDescriptors.SetterExpressionBody, expressionBody.EqualsToken.Span);
        }
        return new AccessorDeclarationSyntax(
            isGet ? SyntaxKind.GetAccessorDeclaration : SyntaxKind.SetAccessorDeclaration, modifiers, keyword, parameter, body, expressionBody);
    }

    private EventDeclarationSyntax ParseEventDeclaration(SyntaxList<SyntaxToken> modifiers)
    {
        var eventKeyword = NextToken();
        var identifier = ExpectIdentifier();
        var parameters = ParseParameterList(ParameterContext.Event);
        // The 'add' / 'remove' accessors of C# (ADR-0029): reported and skipped whole.
        if (At(SyntaxKind.OpenBraceToken))
        {
            Report(DiagnosticDescriptors.EventAccessors, Current.Span);
            SkipBalanced();
        }
        return new EventDeclarationSyntax(modifiers, eventKeyword, identifier, parameters);
    }

    // Parameters -----------------------------------------------------------------------------------------------

    private ParameterListSyntax ParseParameterList(ParameterContext context)
    {
        var open = Expect(SyntaxKind.OpenParenToken);
        if (open.IsMissing)
        {
            return new ParameterListSyntax(open, SeparatedSyntaxList<BaseParameterSyntax>.Empty, Missing(SyntaxKind.CloseParenToken));
        }
        _bracketDepth++;
        var parameters = ParseSeparatedList(SyntaxKind.CloseParenToken, index => ParseParameter(context, index));
        var close = Expect(SyntaxKind.CloseParenToken);
        _bracketDepth--;
        return new ParameterListSyntax(open, parameters, close);
    }

    private BaseParameterSyntax ParseParameter(ParameterContext context, int index)
    {
        var isSelf = Current.Kind == SyntaxKind.SelfKeyword
            || (Current.Kind == SyntaxKind.VarKeyword && Peek(1).Kind == SyntaxKind.SelfKeyword);
        if (isSelf)
        {
            var varKeyword = TryEat(SyntaxKind.VarKeyword);
            var self = NextToken();
            if (context != ParameterContext.Function)
            {
                Report(DiagnosticDescriptors.UnexpectedToken, self.Span, "'self'");
            }
            else if (index > 0)
            {
                Report(DiagnosticDescriptors.SelfNotFirst, self.Span);
            }
            if (At(SyntaxKind.ColonToken))
            {
                Report(DiagnosticDescriptors.SelfWithType, Current.Span);
                SkipCurrentToken();
                SkipNode(ParseType());
            }
            return new SelfParameterSyntax(varKeyword, self);
        }

        if (Current.Kind is SyntaxKind.VarKeyword or SyntaxKind.LetKeyword)
        {
            Report(DiagnosticDescriptors.VarParameter, Current.Span);
            SkipCurrentToken();
        }

        var identifier = ExpectIdentifier();
        SyntaxToken? colon = null;
        SyntaxToken? refKind = null;
        TypeSyntax? type = null;
        if (At(SyntaxKind.ColonToken))
        {
            colon = NextToken();
            if (Current.Kind is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword or SyntaxKind.InKeyword)
            {
                refKind = NextToken();
            }
            type = ParseType();
        }
        else if (context != ParameterContext.Lambda && !identifier.IsMissing)
        {
            Report(DiagnosticDescriptors.ParameterNeedsType, identifier.Span, identifier.Text);
        }
        var @default = TryParseInitializer();
        var owner = context switch
        {
            ParameterContext.Event => "event",
            ParameterContext.Indexer => "indexer",
            _ => null,
        };
        if (owner != null && @default != null)
        {
            Report(DiagnosticDescriptors.ParameterNotAllowed, @default.Span, owner, "have a default value");
        }
        if (context == ParameterContext.Indexer && refKind?.Kind is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword)
        {
            Report(DiagnosticDescriptors.ParameterNotAllowed, refKind.Span, owner!, "be 'out' or 'ref'");
        }
        return new ParameterSyntax(identifier, colon, refKind, type, @default);
    }

    private TypeParameterListSyntax ParseTypeParameterList()
    {
        var lessThan = NextToken();
        var parameters = new List<SyntaxNode>();
        while (true)
        {
            var identifier = ExpectIdentifier();
            TypeParameterConstraintClauseSyntax? clause = null;
            if (At(SyntaxKind.ColonToken))
            {
                var colon = NextToken();
                var constraints = new List<SyntaxNode> { ParseTypeParameterConstraint() };
                while (At(SyntaxKind.AndKeyword))
                {
                    var and = NextToken();
                    CheckOperatorAtLineEnd(and);
                    constraints.Add(and);
                    constraints.Add(ParseTypeParameterConstraint());
                }
                clause = new TypeParameterConstraintClauseSyntax(colon, Separated<TypeParameterConstraintSyntax>(constraints));
            }
            parameters.Add(new TypeParameterSyntax(identifier, clause));
            if (!At(SyntaxKind.CommaToken))
            {
                break;
            }
            parameters.Add(NextToken());
        }
        var greaterThan = Expect(SyntaxKind.GreaterThanToken);
        return new TypeParameterListSyntax(lessThan, Separated<TypeParameterSyntax>(parameters), greaterThan);
    }

    private TypeParameterConstraintSyntax ParseTypeParameterConstraint()
    {
        if (At(SyntaxKind.ClassKeyword))
        {
            return new ClassOrStructConstraintSyntax(SyntaxKind.ClassConstraint, NextToken());
        }
        if (At(SyntaxKind.StructKeyword))
        {
            return new ClassOrStructConstraintSyntax(SyntaxKind.StructConstraint, NextToken());
        }
        if (At(SyntaxKind.NewKeyword))
        {
            var newKeyword = NextToken();
            return new ConstructorConstraintSyntax(newKeyword, Expect(SyntaxKind.OpenParenToken), Expect(SyntaxKind.CloseParenToken));
        }
        return new TypeConstraintSyntax(ParseType());
    }

    // Small clauses shared by declarations ---------------------------------------------------------------------

    /// <summary><c>: Type</c>, which a property and an indexer must have.</summary>
    private TypeAnnotationSyntax ExpectTypeAnnotation() =>
        TryParseTypeAnnotation()
        ?? new TypeAnnotationSyntax(Expect(SyntaxKind.ColonToken), new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken)));

    private TypeAnnotationSyntax? TryParseTypeAnnotation()
    {
        if (!At(SyntaxKind.ColonToken))
        {
            return null;
        }
        var colon = NextToken();
        return new TypeAnnotationSyntax(colon, ParseType());
    }

    /// <summary><c>= value</c>; a line may end after the '=' (ADR-0013).</summary>
    private EqualsValueClauseSyntax? TryParseInitializer()
    {
        if (!At(SyntaxKind.EqualsToken))
        {
            return null;
        }
        var equals = NextToken();
        AllowLineBreak();
        return new EqualsValueClauseSyntax(equals, ParseExpression());
    }

    /// <summary><c>-&gt; Type</c>; a line may end after the '-&gt;' (ADR-0013).</summary>
    private ReturnTypeClauseSyntax? TryParseReturnType()
    {
        if (!At(SyntaxKind.MinusGreaterThanToken))
        {
            return null;
        }
        var arrow = NextToken();
        AllowLineBreak();
        return new ReturnTypeClauseSyntax(arrow, ParseType());
    }

    /// <summary>
    /// A '{' that opens a body. On the next line it is reported (ADR-0013) but still taken: no construct starts a
    /// line with '{', so it can only belong here.
    /// </summary>
    private SyntaxToken ExpectOpenBrace()
    {
        if (Current.Kind == SyntaxKind.OpenBraceToken)
        {
            if (Current.HasLeadingLineBreak)
            {
                Report(DiagnosticDescriptors.OpenBraceOnNextLine, Current.Span);
            }
            return NextToken();
        }
        ReportExpected("'{'");
        return Missing(SyntaxKind.OpenBraceToken);
    }

    private SyntaxToken ExpectCloseBrace(SyntaxToken open)
    {
        if (open.IsMissing)
        {
            return Missing(SyntaxKind.CloseBraceToken);
        }
        AllowLineBreak();
        return Expect(SyntaxKind.CloseBraceToken);
    }
}
