namespace Nyxel.Compiler;

/// <summary>Names and file conventions fixed by ADR-0001. Tools read them from here instead of repeating literals.</summary>
public static class LanguageInfo
{
    public const string Name = "Nyxel";

    /// <summary>Language id used by editors and the language server.</summary>
    public const string LanguageId = "nyxel";

    public const string SourceFileExtension = ".nyxel";

    public const string ProjectFileExtension = ".nyxelproj";

    public const string MimeType = "text/x-nyxel";
}
