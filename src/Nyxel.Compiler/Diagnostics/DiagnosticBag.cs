using System.Collections.Immutable;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Diagnostics;

/// <summary>Collects diagnostics for one source text in report order.</summary>
internal sealed class DiagnosticBag(SourceText text)
{
    private readonly List<Diagnostic> _diagnostics = [];

    public int Count => _diagnostics.Count;

    public void Report(DiagnosticDescriptor descriptor, TextSpan span, params object[] args) =>
        _diagnostics.Add(new Diagnostic(descriptor, new Location(text, span), args));

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _diagnostics.AddRange(diagnostics);

    public ImmutableArray<Diagnostic> ToImmutable() =>
        [.. _diagnostics.OrderBy(d => d.Location.Span.Start)];
}
