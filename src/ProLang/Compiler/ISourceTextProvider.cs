using ProLang.Text;

namespace ProLang.Compiler;

/// <summary>
/// Supplies the contents of a file from somewhere other than the disk.
/// </summary>
/// <remarks>
/// <para>
/// Import resolution reads imported files with <see cref="File.ReadAllText(string)"/>, which is
/// right for a compiler and wrong for an editor: a program imports its own modules, so while
/// someone is editing one of them every other file in the graph would still be analysed against
/// the last saved version. Renaming a function and seeing errors from the version on disk is the
/// symptom.
/// </para>
/// <para>
/// A null provider means the disk, exactly as before, so nothing that does not ask for this pays
/// for it.
/// </para>
/// </remarks>
public interface ISourceTextProvider
{
    /// <summary>Whether this provider has a version of <paramref name="fullPath"/>.</summary>
    /// <remarks>
    /// Consulted alongside <see cref="File.Exists(string)"/> during resolution, so that an import
    /// of a file that has been created in the editor but never saved still resolves.
    /// </remarks>
    bool Exists(string fullPath);

    /// <summary>The provider's version of a file, if it has one.</summary>
    bool TryGetSourceText(string fullPath, out SourceText text);
}
