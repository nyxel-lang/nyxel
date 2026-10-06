using System.Globalization;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Diagnostics;

public enum DiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>
/// One kind of problem: a stable id (ADR-0019, listed in docs/design/diagnostics.md), a severity and an English
/// message template. Ids never change meaning once published, and retired ids are not reused.
/// </summary>
public sealed record DiagnosticDescriptor(string Id, DiagnosticSeverity Severity, string MessageFormat);

/// <summary>Where a diagnostic points: a span in one source text.</summary>
public readonly record struct Location(SourceText Text, TextSpan Span)
{
    public LinePositionSpan LineSpan => Text.GetLinePositionSpan(Span);
}

public sealed class Diagnostic
{
    public Diagnostic(DiagnosticDescriptor descriptor, Location location, params object[] args)
    {
        Descriptor = descriptor;
        Location = location;
        Message = string.Format(CultureInfo.InvariantCulture, descriptor.MessageFormat, args);
    }

    public DiagnosticDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public DiagnosticSeverity Severity => Descriptor.Severity;

    public string Message { get; }

    public Location Location { get; }

    /// <summary>
    /// The MSBuild canonical form <c>path(line,col): error NYX1103: message</c>, one-based, which editors and CI
    /// problem matchers already understand.
    /// </summary>
    public override string ToString()
    {
        var start = Location.LineSpan.Start;
        var severity = Severity == DiagnosticSeverity.Error ? "error" : "warning";
        return $"{Location.Text.Path}({start.Line + 1},{start.Character + 1}): {severity} {Id}: {Message}";
    }
}
