using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MediaStudio
{
    /// <summary>Dialogurile standard Windows (Deschide, Salvează, Alege folderul) — accesibile cu NVDA.</summary>
    static class Dialogs
    {
        public const string MediaFilter = "Fișiere media|*.mp4;*.mkv;*.webm;*.mov;*.avi;*.wmv;*.flv;*.m4v;*.ts;*.mpg;*.mpeg;*.3gp;*.mp3;*.m4a;*.aac;*.wav;*.flac;*.ogg;*.opus;*.wma;*.ac3;*.mka|Toate fișierele|*.*";
        public const string VideoFilter = "Fișiere video|*.mp4;*.mkv;*.webm;*.mov;*.avi;*.wmv;*.flv;*.m4v;*.ts;*.mpg;*.mpeg;*.3gp|Toate fișierele|*.*";
        public const string AudioFilter = "Fișiere audio|*.mp3;*.m4a;*.aac;*.wav;*.flac;*.ogg;*.opus;*.wma;*.ac3;*.mka|Toate fișierele|*.*";
        public const string SubFilter = "Subtitrări|*.srt;*.ass;*.ssa;*.vtt;*.sub|Toate fișierele|*.*";
        public const string ImageFilter = "Imagini|*.jpg;*.jpeg;*.png;*.webp|Toate fișierele|*.*";
        public const string ExeFilter = "Programe|*.exe|Toate fișierele|*.*";

        public static string Pick(PathKind kind, string title, string filter, string current, IWin32Window owner)
        {
            if (kind == PathKind.Folder) return PickFolder(title, current, owner);
            if (kind == PathKind.SaveFile)
            {
                using (var d = new SaveFileDialog { Title = title, Filter = filter ?? "Toate fișierele|*.*", OverwritePrompt = true })
                {
                    TrySetInitial(d, current);
                    return d.ShowDialog(owner) == DialogResult.OK ? d.FileName : null;
                }
            }
            using (var d = new OpenFileDialog { Title = title, Filter = filter ?? "Toate fișierele|*.*", Multiselect = kind == PathKind.OpenFiles })
            {
                TrySetInitial(d, current);
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                return kind == PathKind.OpenFiles ? string.Join("\n", d.FileNames) : d.FileName;
            }
        }

        public static string[] PickFiles(string title, string filter, IWin32Window owner)
        {
            var r = Pick(PathKind.OpenFiles, title, filter, null, owner);
            return r == null ? new string[0] : r.Split('\n');
        }

        static void TrySetInitial(FileDialog d, string current)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(current)) return;
                if (Directory.Exists(current)) d.InitialDirectory = current;
                else { var dir = Path.GetDirectoryName(current); if (Directory.Exists(dir)) { d.InitialDirectory = dir; d.FileName = Path.GetFileName(current); } }
            }
            catch { }
        }

        // ---- Alegerea folderului cu dialogul modern Windows (IFileOpenDialog cu FOS_PICKFOLDERS) ----
        public static string PickFolder(string title, string current, IWin32Window owner)
        {
            try
            {
                var dlg = (IFileDialog)new FileOpenDialogRCW();
                uint opts; dlg.GetOptions(out opts);
                dlg.SetOptions(opts | 0x20 | 0x40); // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM
                dlg.SetTitle(title);
                if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                {
                    IShellItem item; Guid iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(current, IntPtr.Zero, ref iid, out item) == 0) dlg.SetFolder(item);
                }
                int hr = dlg.Show(owner == null ? IntPtr.Zero : owner.Handle);
                if (hr != 0) return null;
                IShellItem res; dlg.GetResult(out res);
                IntPtr p; res.GetDisplayName(0x80058000, out p); // SIGDN_FILESYSPATH
                string path = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                return path;
            }
            catch
            {
                using (var f = new FolderBrowserDialog { Description = title, SelectedPath = current ?? "" })
                    return f.ShowDialog(owner) == DialogResult.OK ? f.SelectedPath : null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
