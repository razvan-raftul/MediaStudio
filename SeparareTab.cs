using System;
using System.Collections.Generic;
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
    /// Tabul „Voce și instrumente”: piesa poate fi un fișier sau un link (se descarcă întâi cu yt-dlp). Desparte o piesă în voce, tobe, bas, chitară, pian și restul, face negativul
    /// (totul fără voce) (tonalitatea se schimbă în tabul Transpoziție). Lucrul îl face separare.exe (Demucs și ffmpeg cu rubberband),
    /// care se descarcă singur prima dată, în subfolderul unelte\separare.
    /// </summary>
    partial class MainForm
    {
        const string SepUrl = "https://github.com/razvan-raftul/MediaStudio/releases/download/separare/separare.zip";
        // programul e în bucăți (separare.zip.001, .002...), pentru că un fișier de release are cel mult 2 GB; lista e aici
        const string SepPartiUrl = "https://github.com/razvan-raftul/MediaStudio/releases/download/separare/separare-parti.txt";
        const string SepVersiune = "bs-roformer 1297 + demucs 4.0.1, htdemucs + htdemucs_6s, ffmpeg rubberband, cuda 12.8, v4";   // aceeași ca în separare.txt

        // codul pentru separare.exe, textul bifei, numele fișierului
        static readonly string[][] SepElemente =
        {
            new[] { "negativ", "Negativ (totul fără voce)", "Negativ" },
            new[] { "voce", "Vocea", "Voce" },
            new[] { "tobe", "Tobele", "Tobe" },
            new[] { "bas", "Basul", "Bas" },
            new[] { "chitara", "Chitara", "Chitară" },
            new[] { "pian", "Pianul", "Pian" },
            new[] { "restul", "Restul instrumentelor", "Restul" },
        };

        TextBox sepFile;
        CheckedListBox sepList;
        CheckBox sepTogether;
        ComboBox sepFormat;
        Process sepProc;
        bool sepBusy, sepStopped;
        int sepLast = -1;
        string sepOutDir = "";
        readonly StringBuilder sepFiles = new StringBuilder();
        string sepError = "";
        string sepTemp = "";          // folderul temporar cu piesa descărcată de pe internet (se șterge la final)

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
            sepFile = b.Path(null, "P&iesa: fișier de pe calculator sau link (YouTube și altele)", PathKind.OpenFile, Dialogs.MediaFilter, "");
            b.Note("Poți scrie sau lipi direct linkul piesei: o descarc eu, fac ce ai bifat, iar piesa descărcată o șterg la final, rămân doar rezultatele. În lista Ce păstrez te miști cu săgețile și bifezi cu Spațiu. Fiecare bifă iese într-un fișier separat. Pentru schimbarea tonalității folosește tabul Transpoziție.");
            // o singură listă cu bife (săgeți sus și jos, Spațiu bifează); din start nu e nimic bifat
            // restul apare doar după ce e aleasă piesa (fișier sau link)
            Panel box;
            var g = b.Gate(out box);
            sepList = g.CheckList(null, "&Ce păstrez (săgețile sus și jos, Spațiu bifează)", SepElemente.Select(e => e[1]), 140);
            sepTogether = g.Check(null, "Pune-le pe toate cele bifate împreună, într-un singur &fișier");
            sepFormat = g.Combo(null, "F&ormatul", new[] { "mp3", "wav" });
            g.Buttons(
                Builder.B("Por&nește", (s, e) => SepStart()),
                Builder.B("Oprește separarea", (s, e) => SepStop()),
                Builder.B("Deschide folderul cu rezultatele", (s, e) => SepOpenFolder()));
            ShowWhenFile(sepFile, box, null, true);
        }

        static string SepClean(string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            return name;
        }

        static bool SepEsteLink(string t)
        {
            t = t.Trim().ToLowerInvariant();
            return t.StartsWith("http://") || t.StartsWith("https://") || t.StartsWith("www.") || t.StartsWith("youtu");
        }

        void SepStart()
        {
            if (sepBusy) { Announce("Lucrez deja la o piesă. Apasă Oprește separarea dacă vrei s-o lași."); return; }
            if (runner.Running) { Announce("O operațiune rulează deja. Așteaptă să se termine sau oprește-o."); return; }
            var src = sepFile.Text.Trim().Trim('"');
            bool link = SepEsteLink(src);
            if (!link && (src.Length == 0 || !File.Exists(src))) { Announce("Alege întâi piesa sau scrie linkul ei."); sepFile.Focus(); return; }
            if (sepList.CheckedIndices.Count == 0) { Announce("Bifează întâi ce vrei să păstrezi."); sepList.Focus(); return; }
            if (link && !File.Exists(Settings.YtdlpPath)) { ShowError(Tools.MissingText(Tool.Ytdlp)); return; }
            sepBusy = true; sepStopped = false; sepLast = -1; sepFiles.Clear(); sepError = "";
            cancelBtn.Enabled = true;
            if (link) SepFetchSong(src.StartsWith("http") ? src : "https://" + src);
            else SepPrepare(src);
        }

        /// <summary>Descarcă sunetul piesei de la link (yt-dlp) într-un folder temporar, apoi pornește separarea.</summary>
        void SepFetchSong(string url)
        {
            sepTemp = Path.Combine(Path.GetTempPath(), "MediaStudio-piesa-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(sepTemp);
            var a = new StringBuilder("--no-playlist --newline --no-part -f bestaudio/best -x --audio-format wav");
            if (File.Exists(Settings.FfmpegPath)) a.Append(" --ffmpeg-location ").Append(Runner.Quote(Path.GetDirectoryName(Settings.FfmpegPath)));
            if (File.Exists(Tools.DenoPath)) a.Append(" --js-runtimes ").Append(Runner.Quote("deno:" + Tools.DenoPath));
            a.Append(" -o ").Append(Runner.Quote(Path.Combine(sepTemp, "%(title)s.%(ext)s"))).Append(' ').Append(Runner.Quote(url));
            Announce("Descarc piesa de pe internet.");
            Log("");
            Log("=== Voce și instrumente: descarc piesa ===");
            Log("> " + Runner.Quote(Settings.YtdlpPath) + " " + a);
            var psi = new ProcessStartInfo(Settings.YtdlpPath, a.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            int last = -1;
            try
            {
                sepProc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                sepProc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    var line = e.Data;
                    BeginInvoke((Action)(() =>
                    {
                        Log(line);
                        // „[download]  45.3% of ...” -> anunț din 10 în 10
                        var t = line.Trim();
                        int i = t.IndexOf('%');
                        if (t.StartsWith("[download]") && i > 10)
                        {
                            double p;
                            if (double.TryParse(t.Substring(10, i - 10).Trim(), System.Globalization.NumberStyles.Float,
                                                System.Globalization.CultureInfo.InvariantCulture, out p))
                            {
                                int bucket = (int)p / 10 * 10;
                                if (bucket > last && bucket > 0 && bucket < 100) { last = bucket; Announce("Descarc piesa: " + bucket + "%", false); }
                            }
                        }
                    }));
                };
                sepProc.ErrorDataReceived += (s, e) => { if (e.Data != null) { var l = e.Data; BeginInvoke((Action)(() => Log(l))); } };
                sepProc.Exited += (s, e) => BeginInvoke((Action)(() => SepSongDone()));
                sepProc.Start();
                sepProc.BeginOutputReadLine();
                sepProc.BeginErrorReadLine();
            }
            catch (Exception ex) { SepDone(null); ShowError("Nu am putut porni descărcarea piesei: " + ex.Message); }
        }

        void SepSongDone()
        {
            int code = -1;
            try { sepProc.WaitForExit(); code = sepProc.ExitCode; } catch { }
            sepProc = null;
            FlushLog();
            if (sepStopped) { SepDone("Oprit."); return; }
            var gasite = Directory.Exists(sepTemp) ? Directory.GetFiles(sepTemp) : new string[0];
            var piesa = gasite.FirstOrDefault(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) ?? gasite.FirstOrDefault();
            if (code != 0 || piesa == null)
            {
                SepDone(null);
                ShowError("Nu am putut descărca piesa de la link. Verifică linkul și internetul. Detaliile sunt în jurnal.");
                return;
            }
            Announce("Am descărcat piesa: " + Path.GetFileNameWithoutExtension(piesa) + ".", false);
            SepPrepare(piesa);
        }

        /// <summary>Pregătește comanda pentru separare.exe (piesa e acum un fișier) și o pornește; prima dată descarcă programul.</summary>
        void SepPrepare(string input)
        {
            // tonalitatea se schimbă acum în tabul Transpoziție; aici rămâne mereu 0
            const int semi = 0;
            const string semn = "";
            var alese = SepElemente.Where((e, i) => sepList.GetItemChecked(i)).ToList();
            var args = new StringBuilder();
            foreach (var e in alese) args.Append(" --element ").Append(e[0]).Append(' ').Append(Runner.Quote(e[2] + semn));
            if (sepTogether.Checked && alese.Count > 1)
                args.Append(" --impreuna ").Append(Runner.Quote(string.Join(" + ", alese.Select(e => e[2])) + semn));
            var titlu = Path.GetFileNameWithoutExtension(input);
            sepOutDir = Path.Combine(Settings.DownloadFolder, "Separate", SepClean(titlu));
            string all = "--intrare " + Runner.Quote(input) + " --iesire " + Runner.Quote(sepOutDir) + " --semitonuri " + semi
                + " --format " + sepFormat.Text.Trim() + args;
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

        /// <summary>Bucățile programului de separare: (adresa, mărimea). Dacă lista lipsește, o singură arhivă.</summary>
        static List<KeyValuePair<string, long>> SepParti()
        {
            var parti = new List<KeyValuePair<string, long>>();
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "MediaStudio";
                    wc.Encoding = Encoding.UTF8;
                    var baza = SepPartiUrl.Substring(0, SepPartiUrl.LastIndexOf('/') + 1);
                    foreach (var rand in wc.DownloadString(SepPartiUrl).Split('\n'))
                    {
                        var bucati = rand.Trim().Split(' ');
                        long marime;
                        if (bucati.Length == 2 && long.TryParse(bucati[1], out marime))
                            parti.Add(new KeyValuePair<string, long>(baza + bucati[0], marime));
                    }
                }
            }
            catch (WebException) { parti.Clear(); }
            if (parti.Count == 0) parti.Add(new KeyValuePair<string, long>(SepUrl, 0));
            return parti;
        }

        void SepDownload()
        {
            Directory.CreateDirectory(Tools.ToolDir);
            var zip = Path.Combine(Path.GetTempPath(), "MediaStudio-separare-" + Process.GetCurrentProcess().Id + ".zip");
            try
            {
                var parti = SepParti();
                long total = parti.Sum(x => x.Value), done = 0;
                int last = -1;
                using (var dst = File.Create(zip))
                {
                    var buf = new byte[1 << 20];
                    foreach (var parte in parti)
                    {
                        var req = (HttpWebRequest)WebRequest.Create(parte.Key);
                        req.UserAgent = "MediaStudio";
                        req.Timeout = 60000;
                        req.ReadWriteTimeout = 120000;
                        long inParte = 0;
                        using (var resp = req.GetResponse())
                        using (var src = resp.GetResponseStream())
                        {
                            if (total == 0) total = resp.ContentLength;
                            int n;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                if (sepStopped) return;
                                dst.Write(buf, 0, n);
                                done += n;
                                inParte += n;
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
                            if (parte.Value > 0 && inParte != parte.Value) throw new IOException("descărcarea nu s-a terminat întreagă");
                            if (parte.Value == 0 && resp.ContentLength > 0 && inParte != resp.ContentLength) throw new IOException("descărcarea nu s-a terminat întreagă");
                        }
                    }
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
            Announce("Despart piesa. Poate dura câteva minute.");
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
                Sounds.Done(true);
            }
            else
            {
                SepDone(null);
                Sounds.Done(false);
                ShowError("Separarea nu a mers. " + (sepError.Length > 0 ? sepError : "Programul s-a oprit cu codul " + code + ". Detaliile sunt în jurnal."));
            }
        }

        void SepDone(string msg)
        {
            sepBusy = false;
            if (sepTemp.Length > 0)
            {
                try { if (Directory.Exists(sepTemp)) Directory.Delete(sepTemp, true); } catch { }
                sepTemp = "";
            }
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
