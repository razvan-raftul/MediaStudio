using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        ListBox trFiles;
        VolumeSlider trSlider;
        Action trSync;

        /// <summary>
        /// Tabul Transpoziție: unul sau mai multe fișiere, un glisor de la minus 12 la plus 12 semitonuri, Exportă.
        /// Tempoul rămâne la fel; la videoclipuri imaginea se copiază neschimbată. Rezultatele merg în folderul Transpuse.
        /// </summary>
        void BuildTranspozitieTab(TabPage page)
        {
            var b = new Builder(page);
            Button[] addBtns = null;
            addBtns = b.Buttons(
                Builder.B("&Adaugă fișiere…", (s, e) =>
                {
                    int n = 0;
                    foreach (var f in Dialogs.PickFiles("Alege piesele pe care le transpui", Dialogs.MediaFilter, this)) n += AddTrFile(f);
                    TrAdded(n);
                }),
                Builder.B("Adaugă un folder…", (s, e) =>
                {
                    var dir = Dialogs.PickFolder("Alege folderul cu piese", null, this);
                    if (dir == null) return;
                    var exts = new HashSet<string>(VideoFormats.Concat(AudioFormats).Concat(new[] { "mpg", "mpeg", "3gp", "m2ts", "vob" }).Select(x => "." + x), StringComparer.OrdinalIgnoreCase);
                    int n = 0;
                    foreach (var f in Directory.GetFiles(dir).Where(f => exts.Contains(Path.GetExtension(f))).OrderBy(f => f)) n += AddTrFile(f);
                    TrAdded(n);
                }));
            // lista, glisorul și Exportă apar doar când există fișiere
            Panel box;
            var g = b.Gate(out box);
            trFiles = g.List(null, "&Fișiere");
            g.Buttons(
                Builder.B("Elimină fișierul selectat", (s, e) =>
                {
                    int i = trFiles.SelectedIndex;
                    if (i < 0) { Announce("Niciun fișier selectat."); return; }
                    trFiles.Items.RemoveAt(i);
                    if (trFiles.Items.Count > 0) trFiles.SelectedIndex = Math.Min(i, trFiles.Items.Count - 1);
                    trSync();
                    Announce("Eliminat. Rămân " + trFiles.Items.Count + ".");
                }),
                Builder.B("Golește lista", (s, e) => { trFiles.Items.Clear(); trSync(); Announce("Lista e goală."); }));

            // săgeată sus = un semiton mai sus, săgeată jos = un semiton mai jos, Page Up/Down câte 5, Home înapoi la 0
            trSlider = new VolumeSlider(-12, 12, 0, SpuneSemitonuri);
            trSlider.AccessibleDescription = "Săgeată sus urcă un semiton, săgeată jos coboară un semiton. Home te duce înapoi la 0, tonalitatea originală. Tempoul rămâne la fel.";
            AddSlider(g, "&Semitonuri", trSlider);

            g.Buttons(Builder.B("&Exportă", (s, e) =>
            {
                if (trFiles.Items.Count == 0) { Announce("Adaugă întâi fișierele."); return; }
                int semi = trSlider.Value;
                if (semi == 0) { Announce("Semitonurile sunt pe 0, adică tonalitatea originală. Urcă sau coboară întâi cu săgețile."); trSlider.Focus(); return; }
                var files = trFiles.Items.Cast<FileItem>().Select(f => f.Path).ToList();
                Run(files.Select(f => TranspozitieJob(f, semi)).ToList());
            }));
            int ti = 0;
            foreach (Control c in g.Table.Controls.Cast<Control>().OrderBy(c => g.Table.GetRow(c)).ThenBy(c => g.Table.GetColumn(c))) c.TabIndex = ti++;

            trSync = () => HideKeepFocus(box, trFiles.Items.Count > 0, addBtns[0]);
            trSync();
        }

        static string SpuneSemitonuri(int v)
        {
            if (v == 0) return "0, tonalitatea originală";
            int a = Math.Abs(v);
            string cat = a == 1 ? "un semiton" : a + " semitonuri";
            return (v > 0 ? "plus " : "minus ") + cat + (v > 0 ? ", mai sus" : ", mai jos");
        }

        int AddTrFile(string f)
        {
            var it = new FileItem(f);
            if (trFiles.Items.Contains(it)) return 0;
            trFiles.Items.Add(it);
            return 1;
        }

        void TrAdded(int n)
        {
            trSync();
            if (n == 0) return;
            trFiles.SelectedIndex = trFiles.Items.Count - 1;
            var names = trFiles.Items.Cast<object>().Select(o => o.ToString()).ToList();
            string list = names.Count <= 5 ? string.Join(", ", names) : string.Join(", ", names.Take(5)) + " și încă " + (names.Count - 5);
            Announce("Am adăugat " + n + ". În listă sunt " + names.Count + ": " + list + ".");
        }

        /// <summary>Același format ca originalul, cu sunetul mutat cu atâtea semitonuri, la același tempo.</summary>
        Job TranspozitieJob(string input, int semi)
        {
            string semn = (semi > 0 ? "+" : "") + semi;
            double f = Math.Pow(2, semi / 12.0);
            string fs = f.ToString("0.######", Inv);
            string filter;
            if (FfmpegAreRubberband())
                filter = "rubberband=pitch=" + fs + ":pitchq=quality";
            else
                // rezervă dacă FFmpeg-ul nu are rubberband: se schimbă viteza, apoi tempoul e adus înapoi
                filter = "aresample=48000,asetrate=" + (48000 * f).ToString("0.###", Inv) + ",aresample=48000,atempo=" + (1 / f).ToString("0.######", Inv);
            var j = new Job(Tool.Ffmpeg, "Transpoziție " + semn + " semitonuri: " + Path.GetFileName(input)) { ProbeForDuration = input };
            FillVolumeArgs(j, input, filter, " (" + semn + " semitonuri)", "Transpuse");
            return j;
        }

        static bool? areRubberband;

        /// <summary>Verifică o singură dată dacă FFmpeg-ul are filtrul rubberband (cel mai curat pentru schimbarea tonalității).</summary>
        static bool FfmpegAreRubberband()
        {
            if (areRubberband.HasValue) return areRubberband.Value;
            bool ok = false;
            try
            {
                var psi = new ProcessStartInfo(Settings.FfmpegPath, "-hide_banner -filters")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    var outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    ok = outp.Contains(" rubberband ");
                }
            }
            catch { }
            areRubberband = ok;
            return ok;
        }
    }
}
