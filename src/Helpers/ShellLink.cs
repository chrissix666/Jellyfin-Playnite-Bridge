using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// Makes and reads Windows shortcuts (.lnk files) through the shell link COM interface. Windows only: callers check
/// <see cref="Curator.IsWindows"/> first. A shortcut is a plain file to a directory scan, so the Curator links the game folder
/// and the extras folder with it (a junction or symbolic link would be followed and could loop).
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShellLink
{
    // The vtable order of both interfaces matters: do not reorder or drop a method.
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkObject
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxChars, IntPtr findData, uint flags);

        void GetIDList(out IntPtr idList);

        void SetIDList(IntPtr idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxChars);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxChars);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxChars);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxChars, out int icon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

        void Resolve(IntPtr window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    /// <summary>The longest target path a shortcut stores (MAX_PATH).</summary>
    public const int MaxTargetChars = 259;

    /// <summary>Writes the shortcut file <paramref name="lnkPath"/> (any file name; the path is used as given) that opens the folder <paramref name="targetFolder"/>.</summary>
    public static void Create(string lnkPath, string targetFolder, string description)
    {
        var link = new ShellLinkObject();
        try
        {
            var shell = (IShellLinkW)link;
            shell.SetPath(targetFolder);
            shell.SetDescription(description);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>The target path stored in the shortcut file <paramref name="lnkPath"/>.</summary>
    public static string TargetOf(string lnkPath)
    {
        var link = new ShellLinkObject();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0);
            var path = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(path, path.Capacity, IntPtr.Zero, 0);
            return path.ToString();
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }
}
