using System.Runtime.InteropServices;

namespace TacoInjector.Services.Windows;

internal static class NativeDllFileDialog
{
    private const int MaxFilePathLength = 32768;

    private const int OfnHideReadOnly = 0x00000004;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnExplorer = 0x00080000;
    private const int OfnNoChangeDir = 0x00000008;
    private const int OfnDontAddToRecent = 0x02000000;

    public static string? PickDllPath(nint ownerWindow)
    {
        var fileBuffer = Marshal.AllocHGlobal(MaxFilePathLength * sizeof(char));
        var filter = Marshal.StringToHGlobalUni("Dynamic link library (*.dll)\0*.dll\0All files (*.*)\0*.*\0\0");
        var title = Marshal.StringToHGlobalUni("Select the .dll file");
        var defaultExtension = Marshal.StringToHGlobalUni("dll");

        try
        {
            ClearBuffer(fileBuffer, MaxFilePathLength * sizeof(char));

            var openFileName = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = ownerWindow,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = MaxFilePathLength,
                lpstrTitle = title,
                lpstrDefExt = defaultExtension,
                Flags = OfnExplorer |
                        OfnHideReadOnly |
                        OfnPathMustExist |
                        OfnFileMustExist |
                        OfnNoChangeDir |
                        OfnDontAddToRecent
            };

            if (GetOpenFileName(ref openFileName))
                return Marshal.PtrToStringUni(fileBuffer);

            var error = CommDlgExtendedError();

            // 0 means the user canceled the dialog.
            if (error == 0)
                return null;

            throw new InvalidOperationException($"GetOpenFileName failed with common-dialog error 0x{error:X}.");
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
            Marshal.FreeHGlobal(filter);
            Marshal.FreeHGlobal(title);
            Marshal.FreeHGlobal(defaultExtension);
        }
    }

    private static void ClearBuffer(nint buffer, int byteCount)
    {
        for (var index = 0; index < byteCount; index++)
            Marshal.WriteByte(buffer, index, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public nint lpstrFilter;
        public nint lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;
        public int nMaxFile;
        public nint lpstrFileTitle;
        public int nMaxFileTitle;
        public nint lpstrInitialDir;
        public nint lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public nint lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName openFileName);

    [DllImport("comdlg32.dll")]
    private static extern int CommDlgExtendedError();
}
