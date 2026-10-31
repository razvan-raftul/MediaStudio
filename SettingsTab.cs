using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        TextBox stYt, stFf, stProbe, stFolder;
        CheckBox stDialogs, stSound;
        ListBox stFolders;
        bool downloading;

        /// <summary>Un folder din lista din Setări: arată numele și câte fișiere are.</summary>
        class FolderItem
        {
            public readonly string Name;
            public FolderItem(string n) { Name = n; }
            public string FullPath { get { return Path.Combine(Settings.DownloadFolder, Name); } }
            public override string ToString()
            {
                int n = 0;
                try { if (Directory.Exists(FullPath)) n = Directory.GetFiles(FullPath, "*", SearchOption.AllDirectories).Length; } catch { }
                return Name + ", " + (n == 0 ? "gol" : n == 1 ? "un fișier" : n + " fișiere");
            }
        }

        void BuildSettingsTab(TabPage page)
        {
            var b = new Builder(page);
            var p = b.Section("Programe", false);
            stYt = p.Path(null, "Calea către &yt-dlp.exe", PathKind.OpenFile, Dialogs.ExeFilter, Settings.YtdlpPath);
            stFf = p.Path(null, "Calea către &ffmpeg.exe", PathKind.OpenFile, Dialogs.ExeFilter, Settings.FfmpegPath);
            stProbe = p.Path(null, "Calea către ff&probe.exe", PathKind.OpenFile, Dialogs.ExeFilter, Settings.FfprobePath);
            p.Buttons(
                Builder.B("&Descarcă automat ce lipsește", (s, e) => DownloadMissing()),
                Builder.B("Caută din nou pe calculator", (s, e) => { Tools.Detect(true); RefreshPaths(); ReportMissingTools(false); }),
                Builder.B("Actualizează yt-dlp", (s, e) => { if (File.Exists(Settings.YtdlpPath)) Run(new Job(Tool.Ytdlp, "Actualizare yt-dlp").A("-U")); else ShowError(Tools.MissingText(Tool.Ytdlp)); }),
                Builder.B("Descarcă din nou FFmpeg (ultima versiune)", (s, e) => DownloadTools(false, true)),
                Builder.B("Verifică versiunile", (s, e) => ShowVersions()));

            var g = b.Section("General", false);
            stDialogs = g.Check(null, "Arată erorile într-o fereastră separată", Settings.ErrorDialogs);
            stSound = g.Check(null, "Sunet la terminarea operațiunilor", Settings.DoneSound);
            stSound.CheckedChanged += (s, e) => { if (stSound.Checked) { Settings.DoneSound = true; Sounds.Done(true); } };

            // folderele: restrânse, se deschid cu caseta „Arată folderele”
            var f = b.Section("Folderele", true);
            f.Note("Tot ce face Media Studio se salvează în folderul principal, fiecare operațiune în folderul ei. Folderele se fac singure când pornește programul.");
            stFolder = f.Path(null, "Folderul &principal", PathKind.Folder, null, Settings.DownloadFolder);
            stFolders = f.List(null, "Folderele din Media Studio", 190);
            f.Buttons(
                Builder.B("Deschide folderul selectat", (s, e) =>
                {
                    var it = stFolders.SelectedItem as FolderItem;
                    if (it == null) { Announce("Alege un folder din listă."); return; }
                    Settings.EnsureFolders();
                    System.Diagnostics.Process.Start("explorer.exe", Runner.Quote(it.FullPath));
                }),
                Builder.B("Deschide folderul principal", (s, e) => { Settings.EnsureFolders(); System.Diagnostics.Process.Start("explorer.exe", Runner.Quote(Settings.DownloadFolder)); }));
            stFolders.Enter += (s, e) => RefreshFolderList();
            stFolders.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { var it = stFolders.SelectedItem as FolderItem; if (it != null) { Settings.EnsureFolders(); System.Diagnostics.Process.Start("explorer.exe", Runner.Quote(it.FullPath)); } e.Handled = true; } };
            RefreshFolderList();

            b.Buttons(Builder.B("&Salvează setările", (s, e) => { SaveSettingsFromUi(); Announce("Setările au fost salvate."); }),
                      Builder.B("Caută actualizări acum", (s, e) => { Announce("Caut o versiune nouă…"); Updater.CheckAsync(this, true); }));

            EventHandler save = (s, e) => SaveSettingsFromUi();
            foreach (var c in new Control[] { stYt, stFf, stProbe, stFolder }) c.Leave += save;
            foreach (var c in new CheckBox[] { stDialogs, stSound }) c.CheckedChanged += save;
        }

        void RefreshFolderList()
        {
            if (stFolders == null) return;
            int sel = Math.Max(0, stFolders.SelectedIndex);
            stFolders.Items.Clear();
            foreach (var n in Settings.SubFolders) stFolders.Items.Add(new FolderItem(n));
            if (stFolders.Items.Count > 0) stFolders.SelectedIndex = Math.Min(sel, stFolders.Items.Count - 1);
        }

        void SaveSettingsFromUi()
        {
            Settings.YtdlpPath = stYt.Text.Trim();
            Settings.FfmpegPath = stFf.Text.Trim();
            Settings.FfprobePath = stProbe.Text.Trim();
            if (stFolder.Text.Trim().Length > 0 && stFolder.Text.Trim() != Settings.DownloadFolder)
            {
                Settings.DownloadFolder = stFolder.Text.Trim();
                Settings.EnsureFolders();
                RefreshFolderList();
            }
            Settings.ErrorDialogs = stDialogs.Checked;
            Settings.DoneSound = stSound.Checked;
            Settings.Save();
        }

        void RefreshPaths()
        {
            if (stYt == null) return;
            stYt.Text = Settings.YtdlpPath; stFf.Text = Settings.FfmpegPath; stProbe.Text = Settings.FfprobePath;
        }

        void ShowVersions()
        {
            var y = Tools.Version(Tool.Ytdlp);
            var f = Tools.Version(Tool.Ffmpeg);
            var p = Tools.Version(Tool.Ffprobe);
            var msg = "yt-dlp: " + (y ?? "lipsește") + "\r\nFFmpeg: " + (f ?? "lipsește") + "\r\nFFprobe: " + (p ?? "lipsește");
            Log(msg);
            Announce(msg.Replace("\r\n", ". "));
        }

        void DownloadMissing()
        {
            DownloadTools(!File.Exists(Settings.YtdlpPath), !File.Exists(Settings.FfmpegPath) || !File.Exists(Settings.FfprobePath));
        }

        async void DownloadTools(bool yt, bool ff)
        {
            if (!yt && !ff) { Announce("Toate programele sunt deja configurate."); return; }
            if (downloading) { Announce("Descărcarea e deja în curs."); return; }
            downloading = true;
            cancelBtn.Enabled = false;
            int lastPct = -100;
            Action<string, int> report = (m, pct) => BeginInvoke((Action)(() =>
            {
                statusBox.Text = m;
                if (pct >= 0) progress.Value = pct * 10;
                if (pct < 0 || pct >= lastPct + Settings.AnnounceStep) { lastPct = pct; Announce(m, false); }
            }));
            try
            {
                if (yt) { Log("Descarc yt-dlp de la " + Tools.YtdlpUrl); await Tools.DownloadYtdlp(report); lastPct = -100; }
                if (ff) { Log("Descarc FFmpeg de la " + Tools.FfmpegUrl); await Tools.DownloadFfmpeg(report); }
                RefreshPaths();
                Announce("Gata. Programele au fost descărcate în folderul „unelte” de lângă aplicație.");
                Log("Programele au fost descărcate în " + Tools.ToolDir);
                Sounds.Done(true);
            }
            catch (Exception ex)
            {
                ShowError("Descărcarea nu a reușit: " + ex.Message + "\r\nVerifică legătura la internet sau dacă folderul aplicației permite scrierea.");
            }
            finally { downloading = false; }
        }
    }
}
