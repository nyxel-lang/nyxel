using System.Collections.Immutable;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Diagnostics;

/// <summary>
/// Collects diagnostics in report order: those of one source text when created with it (the lexer and parser), or
/// of a whole compilation, each with its own location (binding).
/// </summary>
internal sealed class DiagnosticBag(SourceText? text = null)
{
    private readonly List<Diagnostic> _diagnostics = [];

    public int Count => _diagnostics.Count;

    public void Report(DiagnosticDescriptor descriptor, TextSpan span, params object[] args) =>
        Report(descriptor, new Location(text ?? throw new InvalidOperationException("This bag has no source text; report with a location."), span), args);

    public void Report(DiagnosticDescriptor descriptor, Location location, params object[] args) =>
        _diagnostics.Add(new Diagnostic(descriptor, location, args));

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _diagnostics.AddRange(diagnostics);

    public ImmutableArray<Diagnostic> ToImmutable() =>
        [.. _diagnostics.OrderBy(d => d.Location.Span.Start)];
}
