using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Binding;

// The values of struct enums and class enums (ADR-0024, ADR-0039, ADR-0046). Creating one with 'new' is in
// MethodBinder.Calls.cs (BindEnumCaseCreation), 'is' and '==' with the other enums in MethodBinder.Constants.cs.
internal sealed partial class MethodBinder
{
    /// <summary><c>AiState.Idle</c>: a case without data is a value (ADR-0024); one with data is created with <c>new</c>.</summary>
    private BoundExpression EnumCaseValue(MemberAccessExpressionSyntax syntax, SourceEnumCaseSymbol @case)
    {
        if (@case.HasData)
        {
            var items = string.Join(", ", @case.Items.Select(i => $"{i.Name} = ..."));
            Report(DiagnosticDescriptors.CaseWithDataNeedsNew, syntax, syntax.ToString(), items);
            return Bad(syntax);
        }
        return new BoundEnumCase(syntax, @case, []);
    }

    /// <summary>
    /// <c>hit.Amount</c>: the data of a case is taken out with <c>match</c> (ADR-0024), so that it is only read from a
    /// value of the case that has it. The message shows how, with the data as a local named in camelCase.
    /// </summary>
    private bool ReportCaseDataAsMember(MemberAccessExpressionSyntax syntax, TypeSymbol type, string name)
    {
        if (type is not SourceNamedTypeSymbol { IsDataEnum: true } enumType
            || enumType.Cases.FirstOrDefault(c => c.Items.Any(i => i.Name == name)) is not { } @case)
        {
            return false;
        }
        var names = string.Join(", ", @case.Items.Select(i => i.Name == name ? Naming.CamelCase(name) : "_"));
        Report(DiagnosticDescriptors.CaseDataIsNotMember, syntax.Name, name, @case.Name, enumType.Name, names);
        return true;
    }
}
