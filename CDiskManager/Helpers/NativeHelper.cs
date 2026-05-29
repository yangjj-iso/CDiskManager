using System.Runtime.InteropServices;

namespace CDiskManager.Helpers;

public static partial class NativeHelper
{
    [LibraryImport("shell32.dll", SetLastError = true)]
    public static partial int SHEmptyRecycleBinW(nint hwnd, nint pszRootPath, uint dwFlags);

    public const uint SHERB_NOCONFIRMATION = 0x00000001;
    public const uint SHERB_NOPROGRESSUI = 0x00000002;
    public const uint SHERB_NOSOUND = 0x00000004;

    public static void EmptyRecycleBin()
    {
        SHEmptyRecycleBinW(nint.Zero, nint.Zero,
            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
    }

    // --- Recycle-bin aware delete (SHFileOperation) ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>
    /// Sends a file or folder to the Recycle Bin. Returns true on success.
    /// </summary>
    public static bool MoveToRecycleBin(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            // SHFileOperation requires the path list to be double-null terminated.
            pFrom = path + '\0' + '\0',
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI)
        };

        int result = SHFileOperation(ref op);
        return result == 0 && !op.fAnyOperationsAborted;
    }
}
