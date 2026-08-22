using System.Runtime.InteropServices;

namespace ProLang.Compiler;

/// <summary>
/// Embeds a Windows icon into a native launcher, so a compiled program has its own face in
/// Explorer, the taskbar and the Start Menu.
/// </summary>
/// <remarks>
/// <para>
/// The apphost template <see cref="AppHost"/> copies has no resources at all, so a program built
/// from it shows the generic executable icon. Windows reads an application's icon out of the PE
/// resource directory, which is a section of the file rather than a byte to patch — so unlike the
/// assembly name, this cannot be done with a search and replace.
/// </para>
/// <para>
/// It is done with the resource update API instead (<c>BeginUpdateResource</c> and friends), which
/// is what every linker and the .NET SDK itself use, and which handles adding a resource directory
/// to a file that has none. That makes this Windows-only; on other platforms there is nothing to
/// embed an icon into, because an ELF binary has no such concept.
/// </para>
/// <para>
/// An <c>.ico</c> file is a small archive: a directory, then one image per resolution. It does not
/// go into the executable as a unit. Each image becomes its own <c>RT_ICON</c> resource, and a
/// <c>RT_GROUP_ICON</c> resource replaces the file's directory, with each entry's byte offset
/// swapped for the resource id of the image it now refers to. Windows loads the group and picks
/// the image whose size it wants.
/// </para>
/// </remarks>
internal static class IconEmbedder
{
    private const int GroupIconResourceType = 14; // RT_GROUP_ICON
    private const int IconResourceType = 3;       // RT_ICON

    /// <summary>The id Windows shows for a program: the lowest-numbered icon group in the file.</summary>
    private const int MainIconGroupId = 1;

    /// <summary>Language-neutral, which is what an icon should be.</summary>
    private const ushort NeutralLanguage = 0;

    private const int DirectoryHeaderSize = 6;

    /// <summary>A directory entry in the file: descriptor, then a 4-byte length and 4-byte offset.</summary>
    private const int DirectoryEntrySize = 16;

    /// <summary>The leading fields of an entry that mean the same thing in a file and in a group.</summary>
    private const int DescriptorSize = 8;

    /// <summary>
    /// A group entry: the descriptor, a 4-byte length, and a 2-byte resource id where the file
    /// had a 4-byte offset.
    /// </summary>
    private const int GroupEntrySize = DescriptorSize + 4 + 2;

    /// <summary>
    /// Copies every image in <paramref name="iconPath"/> into <paramref name="executablePath"/>.
    /// </summary>
    /// <returns>False if the icon could not be read or the executable could not be updated.</returns>
    public static bool TryEmbed(string executablePath, string iconPath, out string error)
    {
        error = "";

        if (!OperatingSystem.IsWindows())
        {
            error = "icons can only be embedded on Windows; other platforms have no equivalent.";
            return false;
        }

        if (!File.Exists(iconPath))
        {
            error = $"no icon file at '{iconPath}'.";
            return false;
        }

        byte[] icon;

        try
        {
            icon = File.ReadAllBytes(iconPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"could not read '{iconPath}': {e.Message}";
            return false;
        }

        if (!TryReadDirectory(icon, out var images, out var directoryError))
        {
            error = $"'{iconPath}' is not a usable icon file: {directoryError}";
            return false;
        }

        return TryWriteResources(executablePath, icon, images, out error);
    }

    /// <summary>One image's position and shape within the .ico file.</summary>
    private readonly record struct IconImage(byte[] Header, int Offset, int Length);

    /// <summary>
    /// Reads the icon file's directory.
    /// </summary>
    /// <remarks>
    /// Every offset and length is bounds-checked against the file. A malformed icon would
    /// otherwise be copied into the executable as whatever bytes happened to follow, producing a
    /// program that looks built and shows a corrupt icon — a failure that is hard to trace back
    /// to the file that caused it.
    /// </remarks>
    private static bool TryReadDirectory(byte[] icon, out List<IconImage> images, out string error)
    {
        images = [];
        error = "";

        if (icon.Length < DirectoryHeaderSize)
        {
            error = "the file is too short to hold a directory";
            return false;
        }

        if (BitConverter.ToUInt16(icon, 2) != 1)
        {
            error = "it is not an icon (the type field says otherwise; a cursor, perhaps)";
            return false;
        }

        int count = BitConverter.ToUInt16(icon, 4);

        if (count == 0)
        {
            error = "it contains no images";
            return false;
        }

        if (icon.Length < DirectoryHeaderSize + (count * DirectoryEntrySize))
        {
            error = $"it claims {count} images but is too short to hold their directory";
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var entry = DirectoryHeaderSize + (i * DirectoryEntrySize);

            var length = BitConverter.ToInt32(icon, entry + 8);
            var offset = BitConverter.ToInt32(icon, entry + 12);

            if (length <= 0 || offset < 0 || offset > icon.Length - length)
            {
                error = $"image {i + 1} of {count} lies outside the file";
                return false;
            }

            // A directory entry is width, height, colours and a reserved byte, then two words for
            // planes and bit depth: eight bytes that describe the image and carry over unchanged.
            // The four-byte length and four-byte offset that follow do not — the length is
            // rewritten and the offset becomes a two-byte resource id.
            images.Add(new IconImage(icon[entry..(entry + DescriptorSize)], offset, length));
        }

        return true;
    }

    /// <summary>Writes the images and their group into the executable's resource directory.</summary>
    private static bool TryWriteResources(string executablePath, byte[] icon, List<IconImage> images, out string error)
    {
        error = "";

        // deleteExistingResources: false — the apphost has none, and a future one may carry
        // version information that should survive.
        var handle = BeginUpdateResourceW(executablePath, bDeleteExistingResources: false);

        if (handle == IntPtr.Zero)
        {
            error = $"could not open '{executablePath}' for resource update "
                  + $"(error {Marshal.GetLastWin32Error()}).";
            return false;
        }

        var committed = false;

        try
        {
            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];
                var data = icon[image.Offset..(image.Offset + image.Length)];

                if (!Update(handle, IconResourceType, IconIdOf(i), data, out error))
                {
                    return false;
                }
            }

            if (!Update(handle, GroupIconResourceType, MainIconGroupId, BuildGroup(images), out error))
            {
                return false;
            }

            if (!EndUpdateResourceW(handle, fDiscard: false))
            {
                error = $"could not commit the icon to '{executablePath}' "
                      + $"(error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            committed = true;
            return true;
        }
        finally
        {
            // Ending with discard is how the update handle is released when something failed
            // partway. Skipping it leaves the executable locked for the life of the process.
            if (!committed)
            {
                EndUpdateResourceW(handle, fDiscard: true);
            }
        }
    }

    /// <summary>Resource ids for the images, numbered from one.</summary>
    private static int IconIdOf(int index) => index + 1;

    /// <summary>
    /// The group resource: the icon file's directory with each image's file offset replaced by its
    /// resource id.
    /// </summary>
    /// <remarks>
    /// The entry is fourteen bytes here rather than the file's sixteen, because a two-byte resource
    /// id takes the place of a four-byte offset. Getting that length wrong is the classic mistake
    /// and does not fail loudly: the resource is written, the executable is valid, and Windows
    /// quietly falls back to the generic icon because it cannot parse the group.
    /// </remarks>
    private static byte[] BuildGroup(List<IconImage> images)
    {
        using var buffer = new MemoryStream(DirectoryHeaderSize + (images.Count * GroupEntrySize));
        using var writer = new BinaryWriter(buffer);

        writer.Write((ushort)0);                 // reserved
        writer.Write((ushort)1);                 // 1 = icon
        writer.Write((ushort)images.Count);

        for (var i = 0; i < images.Count; i++)
        {
            writer.Write(images[i].Header);      // width, height, colours, reserved, planes, bits
            writer.Write(images[i].Length);
            writer.Write((ushort)IconIdOf(i));
        }

        writer.Flush();
        return buffer.ToArray();
    }

    private static bool Update(IntPtr handle, int type, int name, byte[] data, out string error)
    {
        error = "";

        if (UpdateResourceW(handle, type, name, NeutralLanguage, data, (uint)data.Length))
        {
            return true;
        }

        error = $"could not add resource {name} of type {type} (error {Marshal.GetLastWin32Error()}).";
        return false;
    }

    // Resource types and names are passed as integers cast to pointers — the MAKEINTRESOURCE
    // convention, which is how the API distinguishes a numeric id from a string name.

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.SysInt)]
    private static extern IntPtr BeginUpdateResourceW(
        string pFileName,
        [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResourceW(
        IntPtr hUpdate,
        IntPtr lpType,
        IntPtr lpName,
        ushort wLanguage,
        byte[] lpData,
        uint cb);

    private static bool UpdateResourceW(IntPtr handle, int type, int name, ushort language, byte[] data, uint size)
        => UpdateResourceW(handle, (IntPtr)type, (IntPtr)name, language, data, size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResourceW(
        IntPtr hUpdate,
        [MarshalAs(UnmanagedType.Bool)] bool fDiscard);
}
