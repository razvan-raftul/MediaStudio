using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaStudio
{
    /// <summary>
    /// Redarea din tabul Tăiere și împărțire, ca să găsești după ureche unde tai.
    /// Sunetul fișierului se scoate o dată într-un wav temporar (cu FFmpeg), apoi se redă prin Windows (MCI),
    /// care știe să sară exact la orice milisecundă. Scurtăturile merg oriunde în tab, fără să încurce câmpurile:
    /// F5 redă / pauză, F6 spune timpul, F7 și F8 derulează (Shift câte 1 s, simplu câte 5 s, Control câte 30 s),
    /// F9 pune timpul la începutul bucății, F11 la sfârșitul ei.
    /// </summary>
    partial class MainForm
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr callback);

        const string PlAlias = "mscutplayer";
        string plSource;      // fișierul original pentru care e pregătit wav-ul
        string plWav;         // wav-ul temporar
        bool plOpen, plPreparing;
        double plLength;      // secunde

        static string Mci(string cmd)
        {
            var sb = new StringBuilder(256);
            int err = mciSendString(cmd, sb, sb.Capacity, IntPtr.Zero);
            return err == 0 ? sb.ToString() : null;
        }

        bool PlOnCutTab { get { return tabs.SelectedTab != null && tabs.SelectedTab.Text == "Tăiere și împărțire"; } }

        /// <summary>Scurtăturile playerului; se cheamă din ProcessCmdKey.</summary>
        bool PlKey(Keys keyData)
        {
            if (!PlOnCutTab) return false;
            var k = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0, ctrl = (keyData & Keys.Control) != 0, alt = (keyData & Keys.Alt) != 0;
            if (alt) return false;
            double step = shift ? 1 : ctrl ? 30 : 5;
            switch (k)
            {
                case Keys.F5: if (shift || ctrl) return false; PlToggle(); return true;
                case Keys.F6: if (shift || ctrl) return false; PlSay(); return true;
                case Keys.F7: PlSeekBy(-step); return true;
                case Keys.F8: PlSeekBy(step); return true;
                case Keys.F9: if (shift || ctrl) return false; PlMark(cuStart, "Început"); return true;
                case Keys.F11: if (shift || ctrl) return false; PlMark(cuEnd, "Sfârșit"); return true;
            }
            return false;
        }

        /// <summary>F5: prima dată pregătește sunetul și pornește; apoi pune pauză sau reia.</summary>
        void PlToggle()
        {
            if (!PlReady(true)) return;
            if (Mci("status " + PlAlias + " mode") == "playing")
            {
                Mci("pause " + PlAlias);
                Announce("Pauză la " + FormatTime(PlPos()));
            }
            else
            {
                double p = PlPos();
                if (p >= plLength - 0.05) p = 0;   // de la capăt, dacă s-a terminat
                Mci("play " + PlAlias + " from " + (long)(p * 1000));
                Announce("Redare de la " + FormatTime(p), false);
            }
        }

        void PlSay()
        {
            if (!PlReady(false)) return;
            Announce(FormatTime(PlPos()) + " din " + FormatTime(plLength), false);
        }

        void PlSeekBy(double delta)
        {
            if (!PlReady(false)) return;
            bool playing = Mci("status " + PlAlias + " mode") == "playing";
            double p = Math.Max(0, Math.Min(plLength, PlPos() + delta));
            if (playing) Mci("play " + PlAlias + " from " + (long)(p * 1000));
            else Mci("seek " + PlAlias + " to " + (long)(p * 1000));
            Announce(FormatTime(p), false);
        }

        void PlMark(TextBox field, string what)
        {
            if (!PlReady(false)) return;
            var t = FormatTime(PlPos());
            field.Text = t;
            Announce(what + ": " + t);
        }

        double PlPos()
        {
            double ms;
            var s = Mci("status " + PlAlias + " position");
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out ms) ? ms / 1000.0 : 0;
        }

        /// <summary>1:20.5 sau 1:02:30 — exact cum se scrie în câmpurile de început și sfârșit.</summary>
        static string FormatTime(double sec)
        {
            sec = Math.Max(0, Math.Round(sec, 1));
            int h = (int)(sec / 3600), m = (int)(sec % 3600 / 60);
            double s = sec % 60;
            string ss = s.ToString("00.#", CultureInfo.InvariantCulture);
            return h > 0 ? h + ":" + m.ToString("00") + ":" + ss : m + ":" + ss;
        }

        /// <summary>E gata de redare? Dacă nu și start e true, începe pregătirea (și pornește redarea la final).</summary>
        bool PlReady(bool start)
        {
            var file = (cuIn.Text ?? "").Trim();
            if (file.Length == 0 || !File.Exists(file)) { Announce("Alege întâi fișierul de editat."); return false; }
            if (plOpen && string.Equals(plSource, file, StringComparison.OrdinalIgnoreCase)) return true;
            if (plPreparing) { Announce("Încă pregătesc redarea, așteaptă puțin."); return false; }
            if (!start) { Announce("Pornește întâi redarea cu F5."); return false; }
            if (!File.Exists(Settings.FfmpegPath)) { ShowError("Lipsește FFmpeg. Îl poți descărca din tabul Setări."); return false; }
            PlClose();
            plPreparing = true;
            Announce("Pregătesc redarea…");
            var wav = Path.Combine(Path.GetTempPath(), "MediaStudio-redare-" + Process.GetCurrentProcess().Id + ".wav");
            Task.Run(() =>
            {
                bool ok = false;
                try
                {
                    try { File.Delete(wav); } catch { }
                    var psi = new ProcessStartInfo(Settings.FfmpegPath,
                        "-hide_banner -nostats -loglevel error -y -i " + Runner.Quote(file) + " -vn -map 0:a:0 -ac 2 -ar 44100 -c:a pcm_s16le " + Runner.Quote(wav))
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                    using (var p = Process.Start(psi))
                    {
                        p.StandardError.ReadToEnd();
                        p.WaitForExit();
                        ok = p.ExitCode == 0 && File.Exists(wav) && new FileInfo(wav).Length > 44;
                    }
                }
                catch { }
                BeginInvoke((Action)(() =>
                {
                    plPreparing = false;
                    if (!ok) { ShowError("Nu pot reda fișierul. Poate nu are sunet."); return; }
                    // între timp s-a ales alt fișier: nu mai pornesc
                    if (!string.Equals((cuIn.Text ?? "").Trim(), file, StringComparison.OrdinalIgnoreCase)) return;
                    if (Mci("open \"" + wav + "\" type waveaudio alias " + PlAlias) == null) { ShowError("Windows nu poate deschide sunetul pentru redare."); return; }
                    Mci("set " + PlAlias + " time format milliseconds");
                    double len;
                    var l = Mci("status " + PlAlias + " length");
                    plLength = l != null && double.TryParse(l, NumberStyles.Float, CultureInfo.InvariantCulture, out len) ? len / 1000.0 : 0;
                    plOpen = true; plSource = file; plWav = wav;
                    Mci("play " + PlAlias + " from 0");
                    Announce("Redare. Durata " + FormatTime(plLength) + ".", false);
                }));
            });
            return false;
        }

        /// <summary>Oprește redarea și șterge wav-ul temporar (la alt fișier și la închiderea aplicației).</summary>
        void PlClose()
        {
            if (plOpen) { Mci("stop " + PlAlias); Mci("close " + PlAlias); }
            plOpen = false; plSource = null;
            if (plWav != null) { try { File.Delete(plWav); } catch { } plWav = null; }
        }
    }
}
