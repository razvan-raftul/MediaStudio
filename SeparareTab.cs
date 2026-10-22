using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaStudio
{
    /// <summary>
    /// Tabul „Voce și instrumente”: desparte o piesă în voce, tobe, bas, chitară, pian și restul, face negativul
    /// (totul fără voce) și schimbă tonalitatea. Lucrul îl face separare.exe (Demucs și ffmpeg cu rubberband),
    /// care se descarcă singur prima dată, în subfolderul unelte\separare.
    /// </summary>
    partial class MainForm
    {
        const string SepUrl = "https://github.com/razvan-raftul/MediaStudio/releases/download/separare/separare.zip";
        const string SepVersiune = "demucs 4.0.1, htdemucs + htdemucs_6s, ffmpeg rubberband, v1";   // aceeași ca în separare.txt

        // codul pentru separare.exe, textul bifei, numele fișierului
        static readonly string[][] SepElemente =
        {
            new[] { "negativ", "&Negativ (totul fără voce)", "Negativ" },
            new[] { "voce", "&Vocea", "Voce" },
            new[] { "tobe", "To&bele", "Tobe" },
            new[] { "bas", "Ba&sul", "Bas" },
            new[] { "chitara", "&Chitara", "Chitară" },
            new[] { "pian", "&Pianul", "Pian" },
            new[] { "restul", "&Restul instrumentelor", "Restul" },
        };

        TextBox sepFile;
        CheckBox[] sepChecks;
        CheckBox sepTogether;
        NumericUpDown sepSemi;
        ComboBox sepFormat;
        Process sepProc;
        bool sepBusy, sepStopped;
        int sepLast = -1;
        string sepOutDir = "";
        readonly StringBuilder sepFiles = new StringBuilder();
        string sepError = "";

        static string SepDir { get { return Path.Combine(Tools.ToolDir, "separare"); } }
        static string SepExe { get { return Path.Combine(SepDir, "separare.exe"); } }

        static bool SepInstalled()
        {
            try { return File.Exists(SepExe) && File.ReadAllText(Path.Combine(SepDir, "versiune.txt")).Trim() == SepVersiune; }
            catch { return false; }
        }

        void BuildSeparareTab(TabPage page)
        {
            var b = new Builder(page);
            sepFile = b.Path(null, "P&iesa", PathKind.OpenFile, Dialogs.MediaFilter, "");
            b.Note("Bifează ce păstrezi. Fiecare bifă iese într-un fișier separat. Dacă nu bifezi nimic și alegi doar tonalitatea, se mută toată piesa în tonalitatea nouă, cu același tempo.");
            sepChecks = SepElemente.Select(e => b.Check(null, e[1], e[0] == "negativ")).ToArray();
            sepTogether = b.Check(null, "Pune-le pe toate cele bifate împreună, într-un singur &fișier");
            sepSemi = b.Number(null, "&Tonalitatea, în semitonuri (de la minus 12 la 12; 0 o lasă la fel)", -12, 12, 0);
            sepFormat = b.Combo(null, "F&ormatul", new[] { "mp3", "wav" });
            b.Buttons(
                Builder.B("Por&nește", (s, e) => SepStart()),
                Builder.B("Oprește separarea", (s, e) => SepStop()),
                Builder.B("Deschide folderul cu rezultatele", (s, e) => SepOpenFolder()));
        }

        static string SepClean(string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            return name;
        }

        void SepStart()
        {
            if (sepBusy) { Announce("Lucrez deja la o piesă. Apasă Oprește separarea dacă vrei s-o lași."); return; }
            if (runner.Running) { Announce("O operațiune rulează deja. Așteaptă să se termine sau oprește-o."); return; }
            var input = sepFile.Text.Trim().Trim('"');
            if (input.Length == 0 || !File.Exists(input)) { Announce("Alege întâi piesa."); sepFile.Focus(); return; }
            int semi = (int)sepSemi.Value;
            string semn = semi != 0 ? " (tonalitate " + (semi > 0 ? "+" : "") + semi + ")" : "";
            var alese = SepElemente.Where((e, i) => sepChecks[i].Checked).ToList();
            var args = new StringBuilder();
            if (alese.Count == 0)
            {
                if (semi == 0) { Announce("Bifează ce vrei să păstrezi sau alege o tonalitate."); return; }
                args.Append(" --element original ").Append(Runner.Quote("Tonalitate " + (semi > 0 ? "+" : "") + semi));
            }
            else
                foreach (var e in alese) args.Append(" --element ").Append(e[0]).Append(' ').Append(Runner.Quote(e[2] + semn));
            if (sepTogether.Checked && alese.Count > 1)
                args.Append(" --impreuna ").Append(Runner.Quote(string.Join(" + ", alese.Select(e => e[2])) + semn));
            var titlu = Path.GetFileNameWithoutExtension(input);
            sepOutDir = Path.Combine(Settings.DownloadFolder, "Separate", SepClean(titlu));
            string all = "--intrare " + Runner.Quote(input) + " --iesire " + Runner.Quote(sepOutDir) + " --semitonuri " + semi
                + " --format " + sepFormat.Text.Trim() + args;
            sepBusy = true; sepStopped = false; sepLast = -1; sepFiles.Clear(); sepError = "";
            cancelBtn.Enabled = true;
            if (SepInstalled()) { SepRun(all, alese.Count > 0); return; }
            Announce("Prima dată se descarcă programul de separare, o singură dată. Se descarcă.");
            Task.Run(() => SepDownload()).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                if (sepStopped) { SepDone("Oprit."); return; }
                if (t.Exception != null || !SepInstalled())
                {
                    var msg = t.Exception != null ? t.Exception.GetBaseException().Message : "arhiva nu e întreagă";
                    SepDone(null); ShowError("Nu am putut descărca programul de separare. Verifică internetul. (" + msg + ")");
                    return;
                }
                SepRun(all, alese.Count > 0);
            })));
        }

        void SepDownload()
        {
            Directory.CreateDirectory(Tools.ToolDir);
            var zip = Path.Combine(Path.GetTempPath(), "MediaStudio-separare-" + Process.GetCurrentProcess().Id + ".zip");
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(SepUrl);
                req.UserAgent = "MediaStudio";
                using (var resp = req.GetResponse())
                using (var src = resp.GetResponseStream())
                using (var dst = File.Create(zip))
                {
                    long total = resp.ContentLength, done = 0;
                    var buf = new byte[1 << 20];
                    int n, last = -1;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (sepStopped) return;
                        dst.Write(buf, 0, n);
                        done += n;
                        if (total > 0)
                        {
                            int p = (int)(done * 100 / total) / 10 * 10;
                            if (p > last && p < 100)
                            {
                                last = p;
                                string mb = (total / 1048576).ToString();
                                BeginInvoke((Action)(() => Announce("Se descarcă programul de separare (" + mb + " MB): " + p + "%", false)));
                            }
                        }
                    }
                    if (total > 0 && done != total) throw new IOException("descărcarea nu s-a terminat întreagă");
                }
                BeginInvoke((Action)(() => Announce("Despachetez programul de separare.", false)));
                var nou = SepDir + ".nou";
                if (Directory.Exists(nou)) Directory.Delete(nou, true);
                ZipFile.ExtractToDirectory(zip, nou);
                if (Directory.Exists(SepDir)) Directory.Delete(SepDir, true);
                Directory.Move(nou, SepDir);
            }
            finally { try { File.Delete(zip); } catch { } }
        }

        void SepRun(string args, bool separa)
        {
            if (sepStopped) { SepDone("Oprit."); return; }
            Announce(separa ? "Despart piesa. Poate dura câteva minute." : "Schimb tonalitatea.");
            Log("");
            Log("=== Voce și instrumente ===");
            Log("> " + Runner.Quote(SepExe) + " " + args);
            var psi = new ProcessStartInfo(SepExe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = SepDir
            };
            try
            {
                sepProc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                sepProc.OutputDataReceived += (s, e) => { if (e.Data != null) BeginInvoke((Action)(() => SepLine(e.Data))); };
                sepProc.ErrorDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
                sepProc.Exited += (s, e) => BeginInvoke((Action)(() => SepExited()));
                sepProc.Start();
                sepProc.BeginOutputReadLine();
                sepProc.BeginErrorReadLine();
            }
            catch (Exception ex) { SepDone(null); ShowError("Nu am putut porni programul de separare: " + ex.Message); }
        }

        void SepLine(string line)
        {
            Log(line);
            line = line.Trim();
            if (line.StartsWith("PROGRES "))
            {
                int p;
                if (int.TryParse(line.Substring(8).Trim(), out p))
                {
                    progress.Value = Math.Max(0, Math.Min(1000, p * 10));
                    int bucket = p / 10 * 10;
                    if (bucket > sepLast && bucket > 0 && bucket < 100) { sepLast = bucket; Announce(bucket + "%", false); }
                }
            }
            else if (line.StartsWith("FISIER ")) sepFiles.AppendLine(Path.GetFileName(line.Substring(7).Trim()));
            else if (line.StartsWith("EROARE ")) sepError = line.Substring(7).Trim();
        }

        void SepExited()
        {
            int code = -1;
            try { sepProc.WaitForExit(); code = sepProc.ExitCode; } catch { }
            sepProc = null;
            FlushLog();
            if (sepStopped) { SepDone("Oprit."); return; }
            int n = sepFiles.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (code == 0 && n > 0)
            {
                progress.Value = 1000;
                SepDone("Gata. Am făcut " + n + (n == 1 ? " fișier" : " fișiere") + ", în folderul " + sepOutDir + ".");
                Log(sepFiles.ToString());
                if (Settings.DoneSound) System.Media.SystemSounds.Asterisk.Play();
            }
            else
            {
                SepDone(null);
                ShowError("Separarea nu a mers. " + (sepError.Length > 0 ? sepError : "Programul s-a oprit cu codul " + code + ". Detaliile sunt în jurnal."));
            }
        }

        void SepDone(string msg)
        {
            sepBusy = false;
            if (!runner.Running) cancelBtn.Enabled = false;
            if (msg != null) Announce(msg);
        }

        /// <summary>Oprește separarea (și descărcarea ei); folosit și de butonul Oprește operațiunea și la închidere.</summary>
        void SepStop(bool quiet = false)
        {
            if (!sepBusy) { if (!quiet) Announce("Nu lucrez acum la nicio piesă."); return; }
            sepStopped = true;
            try { if (sepProc != null && !sepProc.HasExited) sepProc.Kill(); } catch { }
            if (!quiet) Announce("Opresc separarea…");
        }

        void SepOpenFolder()
        {
            var f = sepOutDir.Length > 0 ? sepOutDir : Path.Combine(Settings.DownloadFolder, "Separate");
            if (!Directory.Exists(f)) { Announce("Încă nu există rezultate."); return; }
            try { Process.Start(new ProcessStartInfo(f) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex.Message); }
        }
    }
}
