namespace ProLang.Lsp.Workspace;

/// <summary>
/// Converting between the URIs an editor speaks and the paths the compiler speaks.
/// </summary>
/// <remarks>
/// Small and fiddly enough to be worth having in one place with its own tests. Windows is where it
/// goes wrong: <c>file:///d:/a/b.prl</c> has a leading slash before the drive letter that is not
/// part of the path, and the drive letter's case varies between editors, which matters because
/// every lookup here is keyed by the result.
/// </remarks>
internal static class DocumentUri
{
    public static string ToPath(string uri)
    {
        if (!uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var path = Uri.UnescapeDataString(uri["file://".Length..]);

        // file:///d:/x  →  the slash before the drive letter is URI syntax, not path.
        if (path.Length > 2 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':')
        {
            path = path[1..];
        }

        if (Path.DirectorySeparatorChar != '/')
        {
            path = path.Replace('/', Path.DirectorySeparatorChar);
        }

        return path;
    }

    public static string FromPath(string path)
    {
        try
        {
            // Built through Uri rather than by escaping the string, which escapes too much: a
            // drive letter's colon is a legal path character, and `file:///d%3A/…` — while
            // technically valid — is not the spelling editors produce or compare against.
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }
        catch (UriFormatException)
        {
            return "file:///" + Path.GetFullPath(path).Replace('\\', '/');
        }
    }

    /// <summary>
    /// The form used as a dictionary key, so that two spellings of one file are one entry.
    /// </summary>
    public static string NormalisePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
