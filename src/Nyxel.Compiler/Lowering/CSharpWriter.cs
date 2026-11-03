using System.Globalization;
using System.Text;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Lowering;

/// <summary>
/// Writes the generated C# line by line and knows each line's column, which the <c>#line</c> span directives need
/// (docs/design/debug-mapping.md, rule 1 and 4). A mapped line gets a directive for its Nyxel span with the column
/// offset of its first character; other lines are under <c>#line hidden</c>.
/// </summary>
internal sealed class CSharpWriter
{
    private const int IndentSize = 4;
    private readonly StringBuilder _text = new();
    private int _indent;
    private bool _hidden;

    public void Indent() => _indent++;

    public void Unindent() => _indent--;

    /// <summary>A line without a directive: before any mapped code, such as the file header.</summary>
    public void WriteRaw(string line) => _text.Append(line).Append('\n');

    /// <summary>A line with no Nyxel counterpart: the debugger steps over it.</summary>
    public void WriteHidden(string line)
    {
        if (!_hidden)
        {
            _text.Append("#line hidden\n");
            _hidden = true;
        }
        AppendLine(line);
    }

    /// <summary>A line for a Nyxel span: its sequence points and diagnostics map to that span.</summary>
    public void WriteMapped(SourceText source, TextSpan span, string line)
    {
        var lines = source.GetLinePositionSpan(span);
        var offset = (_indent * IndentSize) + 1;
        // One-based, end column included (Roslyn adds one): see debug-mapping.md. An empty span still covers a column.
        var endColumn = Math.Max(lines.End.Character, lines.End.Line == lines.Start.Line ? lines.Start.Character + 1 : 1);
        _text.Append(CultureInfo.InvariantCulture,
            $"#line ({lines.Start.Line + 1}, {lines.Start.Character + 1}) - ({lines.End.Line + 1}, {endColumn}) {offset} \"{source.Path}\"\n");
        _hidden = false;
        AppendLine(line);
    }

    public override string ToString() => _text.ToString();

    private void AppendLine(string line) => _text.Append(' ', _indent * IndentSize).Append(line).Append('\n');
}
