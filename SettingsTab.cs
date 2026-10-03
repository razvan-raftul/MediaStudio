using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        TextBox stYt, stFf, stProbe, stFolder;
        CheckBox stAnnounce, stDialogs, stSound, stOverwrite;
        ComboBox stStep;
        bool downloading;

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
            stFolder = g.Path(null, "Folder implicit pentru descărcări", PathKind.Folder, null, Settings.DownloadFolder);
            stOverwrite = g.Check(null, "Suprascrie fișierele existente fără să întrebe", Settings.Overwrite);
            stAnnounce = g.Check(null, "Anunță progresul cu NVDA", Settings.AnnounceProgress);
            stStep = g.Combo(null, "Anunță progresul la fiecare", new[] { "5%", "10%", "20%", "25%", "50%" }, 1);
            stStep.SelectedItem = Settings.AnnounceStep + "%";
            stDialogs = g.Check(null, "Arată erorile într-o fereastră separată", Settings.ErrorDialogs);
            stSound = g.Check(null, "Sunet la terminarea operațiunilor", Settings.DoneSound);
            b.Buttons(Builder.B("&Salvează setările", (s, e) => { SaveSettingsFromUi(); Announce("Setările au fost salvate."); }),
                      Builder.B("Deschide folderul cu presetări", (s, e) => { Directory.CreateDirectory(Settings.PresetDir); System.Diagnostics.Process.Start("explorer.exe", Runner.Quote(Settings.PresetDir)); }));

            EventHandler save = (s, e) => SaveSettingsFromUi();
            foreach (var c in new Control[] { stYt, stFf, stProbe, stFolder }) c.Leave += save;
            foreach (var c in new CheckBox[] { stOverwrite, stAnnounce, stDialogs, stSound }) c.CheckedChanged += save;
            stStep.SelectedIndexChanged += save;
        }

        void SaveSettingsFromUi()
        {
            Settings.YtdlpPath = stYt.Text.Trim();
            Settings.FfmpegPath = stFf.Text.Trim();
            Settings.FfprobePath = stProbe.Text.Trim();
            if (stFolder.Text.Trim().Length > 0) Settings.DownloadFolder = stFolder.Text.Trim();
            Settings.Overwrite = stOverwrite.Checked;
            Settings.AnnounceProgress = stAnnounce.Checked;
            int n; if (int.TryParse(stStep.Text.TrimEnd('%'), out n)) Settings.AnnounceStep = n;
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
                ytOptions = null; ffOptions = null;
                Announce("Gata. Programele au fost descărcate în folderul „unelte” de lângă aplicație.");
                Log("Programele au fost descărcate în " + Tools.ToolDir);
                if (Settings.DoneSound) System.Media.SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                ShowError("Descărcarea nu a reușit: " + ex.Message + "\r\nVerifică legătura la internet sau dacă folderul aplicației permite scrierea.");
            }
            finally { downloading = false; }
        }
    }
}
