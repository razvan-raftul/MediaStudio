using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        ListBox faFiles;
        VolumeSlider faIn, faOut;
        Action faSync;

        /// <summary>
        /// Tabul Fade: unul sau mai multe fișiere (sunet sau videoclip), cât ține fade in la început și fade out la sfârșit, Exportă.
        /// La videoclipuri se stinge și imaginea, odată cu sunetul. Rezultatele merg în folderul Fade, cu numele nume_fade.ext.
        /// </summary>
        void BuildFadeTab(TabPage page)
        {
            var b = new Builder(page);
            Button[] addBtns = null;
            addBtns = b.Buttons(
                Builder.B("&Adaugă fișiere…", (s, e) =>
                {
                    int n = 0;
                    foreach (var f in Dialogs.PickFiles("Alege fișierele la care pui fade", Dialogs.MediaFilter, this)) n += AddFaFile(f);
                    FaAdded(n);
                }),
                Builder.B("Adaugă un folder…", (s, e) =>
                {
                    var dir = Dialogs.PickFolder("Alege folderul cu fișiere", null, this);
                    if (dir == null) return;
                    var exts = new HashSet<string>(VideoFormats.Concat(AudioFormats).Concat(new[] { "mpg", "mpeg", "3gp", "m2ts", "vob" }).Select(x => "." + x), StringComparer.OrdinalIgnoreCase);
                    int n = 0;
                    foreach (var f in Directory.GetFiles(dir).Where(f => exts.Contains(Path.GetExtension(f))).OrderBy(f => f)) n += AddFaFile(f);
                    FaAdded(n);
                }));
            // lista, glisoarele și Exportă apar doar când există fișiere
            Panel box;
            var g = b.Gate(out box);
            faFiles = g.List(null, "&Fișiere");
            g.Buttons(
                Builder.B("Elimină fișierul selectat", (s, e) =>
                {
                    int i = faFiles.SelectedIndex;
                    if (i < 0) { Announce("Niciun fișier selectat."); return; }
                    faFiles.Items.RemoveAt(i);
                    if (faFiles.Items.Count > 0) faFiles.SelectedIndex = Math.Min(i, faFiles.Items.Count - 1);
                    faSync();
                    Announce("Eliminat. Rămân " + faFiles.Items.Count + ".");
                }),
                Builder.B("Golește lista", (s, e) => { faFiles.Items.Clear(); faSync(); Announce("Lista e goală."); }));

            // săgeată sus/jos câte o secundă, Page Up/Down câte 5, Home înapoi la valoarea de început
            faIn = new VolumeSlider(0, 30, 3, v => v == 0 ? "0, fără fade in" : SpuneSecunde(v));
            faIn.AccessibleDescription = "Cât timp crește sunetul de la liniște la volumul întreg, la început. 0 înseamnă fără fade in.";
            AddSlider(g, "Fade &in la început", faIn);
            faOut = new VolumeSlider(0, 30, 3, v => v == 0 ? "0, fără fade out" : SpuneSecunde(v));
            faOut.AccessibleDescription = "Cât timp scade sunetul până la liniște, la sfârșit. 0 înseamnă fără fade out.";
            AddSlider(g, "Fade &out la sfârșit", faOut);

            g.Buttons(Builder.B("&Exportă", (s, e) =>
            {
                if (faFiles.Items.Count == 0) { Announce("Adaugă întâi fișierele."); return; }
                int fi = faIn.Value, fo = faOut.Value;
                if (fi == 0 && fo == 0) { Announce("Fade in și fade out sunt amândouă pe 0. Pune cel puțin unul mai sus."); faIn.Focus(); return; }
                var jobs = new List<Job>();
                foreach (var f in faFiles.Items.Cast<FileItem>().Select(x => x.Path))
                {
                    var j = FadeJob(f, fi, fo);
                    if (j != null) jobs.Add(j);
                }
                if (jobs.Count > 0) Run(jobs);
            }));
            int ti = 0;
            foreach (Control c in g.Table.Controls.Cast<Control>().OrderBy(c => g.Table.GetRow(c)).ThenBy(c => g.Table.GetColumn(c))) c.TabIndex = ti++;

            faSync = () => HideKeepFocus(box, faFiles.Items.Count > 0, addBtns[0]);
            faSync();
        }

        static string SpuneSecunde(int v) { return v == 1 ? "o secundă" : v + (v >= 20 ? " de secunde" : " secunde"); }

        int AddFaFile(string f)
        {
            var it = new FileItem(f);
            if (faFiles.Items.Contains(it)) return 0;
            faFiles.Items.Add(it);
            return 1;
        }

        void FaAdded(int n)
        {
            faSync();
            if (n == 0) return;
            faFiles.SelectedIndex = faFiles.Items.Count - 1;
            var names = faFiles.Items.Cast<object>().Select(o => o.ToString()).ToList();
            string list = names.Count <= 5 ? string.Join(", ", names) : string.Join(", ", names.Take(5)) + " și încă " + (names.Count - 5);
            Announce("Am adăugat " + n + ". În listă sunt " + names.Count + ": " + list + ".");
        }

        /// <summary>Fade in de la secunda 0 și fade out care se termină exact la sfârșitul fișierului.</summary>
        Job FadeJob(string input, int fadeIn, int fadeOut)
        {
            double dur = Runner.ProbeDuration(input);
            if (dur <= 0) { ShowError("Nu pot afla durata fișierului " + Path.GetFileName(input) + ". Verifică dacă FFprobe e configurat în Setări."); return null; }
            bool hasV, hasA;
            ProbeStreams(input, out hasV, out hasA);
            if (!hasV && !hasA) { ShowError(Path.GetFileName(input) + " nu are nici imagine, nici sunet."); return null; }
            // fade-urile nu pot fi mai lungi decât jumătate din fișier
            double fi = Math.Min(fadeIn, dur / 2), fo = Math.Min(fadeOut, dur / 2);
            var af = new List<string>(); var vf = new List<string>();
            if (fi > 0) { af.Add("afade=t=in:st=0:d=" + Sec(fi)); vf.Add("fade=t=in:st=0:d=" + Sec(fi)); }
            if (fo > 0) { af.Add("afade=t=out:st=" + Sec(dur - fo) + ":d=" + Sec(fo)); vf.Add("fade=t=out:st=" + Sec(dur - fo) + ":d=" + Sec(fo)); }

            var j = new Job(Tool.Ffmpeg, "Fade: " + Path.GetFileName(input)) { DurationSeconds = dur };
            if (!hasV)
            {
                // doar sunet: același format ca originalul (coperta mp3-ului rămâne)
                FillVolumeArgs(j, input, string.Join(",", af), " fade", "Fade");
                return j;
            }
            // videoclip: se stinge și imaginea, așa că imaginea se codează din nou
            var ext = Path.GetExtension(input).ToLowerInvariant();
            if (ext != ".mp4" && ext != ".mov" && ext != ".mkv" && ext != ".m4v") ext = ".mp4";
            var outp = AutoOut(input, "Fade", "fade", ext);
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input, "-map", "0:v:0");
            if (hasA) j.A("-map", "0:a:0");
            j.A("-vf", string.Join(",", vf), "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p");
            if (hasA) j.A("-af", string.Join(",", af), "-c:a", "aac", "-b:a", "192k");
            if (ext != ".mkv") j.A("-movflags", "+faststart");
            j.A(outp);
            return j;
        }
    }
}
