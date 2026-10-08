using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        // bucăți marcate: Item1 = început, Item2 = sfârșit (-1 = până la final)
        readonly List<Tuple<double, double>> cuts = new List<Tuple<double, double>>();
        TextBox cuIn, cuStart, cuEnd, cuOut;
        ComboBox cuMode;
        ListBox cuList;
        CheckBox cuJointFade;
        NumericUpDown cuFadeIn, cuFadeOut;

        void BuildCutsSection(Builder b)
        {
            var c = b.Section("Tăiere și fade", false);
            c.Note("Marchezi una sau mai multe bucăți: scrii începutul și sfârșitul și apeși Adaugă bucata. Alegi dacă bucățile marcate se scot din clip sau se păstrează doar ele. Poți pune și fade la început și la sfârșit. La final apeși Aplică tăieturile. Fișierul original nu se modifică.");
            cuIn = c.Path("cuIn", "Fișier de &editat", PathKind.OpenFile, Dialogs.MediaFilter);
            cuStart = c.Text(null, "Începutul bucății (secunde, mm:ss sau hh:mm:ss)", "", false, "De exemplu 1:20 înseamnă minutul 1 și 20 de secunde.");
            cuEnd = c.Text(null, "Sfârșitul bucății", "", false, "Gol înseamnă până la sfârșitul clipului.");
            c.Buttons(
                Builder.B("Adaugă &bucata în listă", (s, e) => AddCut(false)),
                Builder.B("Înlocuiește bucata selectată", (s, e) => AddCut(true)));
            cuList = c.List(null, "Bucăți marcate", 110);
            cuList.SelectedIndexChanged += (s, e) =>
            {
                int i = cuList.SelectedIndex; if (i < 0 || i >= cuts.Count) return;
                cuStart.Text = Sec(cuts[i].Item1); cuEnd.Text = cuts[i].Item2 < 0 ? "" : Sec(cuts[i].Item2);
            };
            c.Buttons(
                Builder.B("Elimină bucata selectată", (s, e) =>
                {
                    int i = cuList.SelectedIndex; if (i < 0) { Announce("Nu e selectată nicio bucată."); return; }
                    cuts.RemoveAt(i); RefreshCuts(Math.Min(i, cuts.Count - 1));
                    Announce("Bucata a fost eliminată. Au rămas " + cuts.Count + ".");
                }),
                Builder.B("Golește lista de bucăți", (s, e) => { cuts.Clear(); RefreshCuts(-1); Announce("Lista de bucăți e goală."); }));
            cuMode = c.Combo("cuMode", "Ce fac cu bucățile marcate", new[] { "Le scot din clip, restul se lipește", "Le păstrez doar pe ele, restul se scoate" });
            cuJointFade = c.Check("cuJointFade", "Trecere lină la fiecare tăietură (imaginea și sunetul se sting și revin în 0,3 secunde)");
            cuFadeIn = c.Number("cuFadeIn", "Fade in la început (secunde, 0 înseamnă fără)", 0, 60, 0, 0.5m, 1);
            cuFadeOut = c.Number("cuFadeOut", "Fade out la sfârșit (secunde, 0 înseamnă fără)", 0, 60, 0, 0.5m, 1);
            cuOut = c.Path("cuOut", "Fișier rezultat", PathKind.SaveFile, Dialogs.MediaFilter, "");
            c.Buttons(Builder.B("Apl&ică tăieturile", (s, e) => Run(CutsJob())));
        }

        string DescribeCut(Tuple<double, double> c, int i)
        {
            if (c.Item2 < 0) return (i + 1) + ". de la " + Clock(c.Item1) + " până la sfârșit";
            double len = c.Item2 - c.Item1;
            return (i + 1) + ". de la " + Clock(c.Item1) + " la " + Clock(c.Item2) + " (" + (len == 1 ? "o secundă" : Sec(len) + " secunde") + ")";
        }

        void RefreshCuts(int select)
        {
            cuts.Sort((a, c) => a.Item1.CompareTo(c.Item1));
            cuList.Items.Clear();
            for (int i = 0; i < cuts.Count; i++) cuList.Items.Add(DescribeCut(cuts[i], i));
            if (select >= 0 && select < cuList.Items.Count) cuList.SelectedIndex = select;
        }

        void AddCut(bool replace)
        {
            int sel = cuList.SelectedIndex;
            if (replace && sel < 0) { Announce("Alege întâi din listă bucata pe care o înlocuiești."); return; }
            double st = ParseTime(cuStart.Text), en = ParseTime(cuEnd.Text);
            if (st < 0) { Announce("Scrie începutul bucății."); cuStart.Focus(); return; }
            if (cuEnd.Text.Trim().Length > 0 && en < 0) { Announce("Sfârșitul nu e scris corect."); cuEnd.Focus(); return; }
            if (en >= 0 && en <= st) { Announce("Sfârșitul trebuie să fie după început."); cuEnd.Focus(); return; }
            var c = Tuple.Create(st, en);
            if (replace) cuts[sel] = c; else cuts.Add(c);
            RefreshCuts(-1);
            Announce((replace ? "Am înlocuit: " : "Am adăugat: ") + DescribeCut(c, cuts.IndexOf(c)) + ". În listă sunt " + cuts.Count + " bucăți.");
            cuStart.Text = ""; cuEnd.Text = "";
            cuStart.Focus();
        }

        /// <summary>Ce piste are fișierul: video (fără coperți) și audio.</summary>
        static void ProbeStreams(string file, out bool video, out bool audio)
        {
            video = audio = false;
            var o = Runner.Capture(Tool.Ffprobe, "-v error -show_entries stream=codec_type:stream_disposition=attached_pic -of csv=p=0 " + Runner.Quote(file), 15000);
            if (o == null) return;
            foreach (var raw in o.Replace("\r", "").Split('\n'))
            {
                var p = raw.Trim().Split(',');
                if (p[0] == "audio") audio = true;
                else if (p[0] == "video" && !(p.Length > 1 && p[1] == "1")) video = true;
            }
        }

        Job CutsJob()
        {
            if (!Need(cuIn.Text, "fișierul de editat")) return null;
            var input = cuIn.Text.Trim();
            double dur = Runner.ProbeDuration(input);
            if (dur <= 0) { ShowError("Nu pot afla durata fișierului. Verifică dacă FFprobe e configurat în Setări."); return null; }
            bool hasV, hasA;
            ProbeStreams(input, out hasV, out hasA);
            if (!hasV && !hasA) { ShowError("Fișierul nu are nici imagine, nici sunet."); return null; }

            // bucățile marcate, unite dacă se suprapun
            var marked = new List<double[]>();
            foreach (var c in cuts.OrderBy(x => x.Item1))
            {
                double a = Math.Min(c.Item1, dur), z = c.Item2 < 0 ? dur : Math.Min(c.Item2, dur);
                if (z - a < 0.01) continue;
                if (marked.Count > 0 && a <= marked[marked.Count - 1][1]) marked[marked.Count - 1][1] = Math.Max(marked[marked.Count - 1][1], z);
                else marked.Add(new[] { a, z });
            }

            var keep = new List<double[]>();
            if (cuMode.SelectedIndex == 1)
            {
                if (marked.Count == 0) { Announce("Adaugă cel puțin o bucată de păstrat."); return null; }
                keep = marked;
            }
            else
            {
                double pos = 0;
                foreach (var m in marked) { if (m[0] - pos > 0.01) keep.Add(new[] { pos, m[0] }); pos = m[1]; }
                if (dur - pos > 0.01) keep.Add(new[] { pos, dur });
            }
            if (keep.Count == 0) { Announce("După tăieturi nu mai rămâne nimic din clip."); return null; }
            double fi = (double)cuFadeIn.Value, fo = (double)cuFadeOut.Value;
            if (marked.Count == 0 && fi == 0 && fo == 0) { Announce("Nu ai marcat nicio bucată și nici fade. Nu e nimic de făcut."); return null; }

            double total = keep.Sum(k => k[1] - k[0]);
            var ext = Path.GetExtension(input).ToLowerInvariant();
            if (hasV && ext != ".mp4" && ext != ".mov" && ext != ".mkv" && ext != ".m4v") ext = ".mp4";
            var outp = cuOut.Text.Trim().Length > 0 ? cuOut.Text.Trim() : OutPath(input, null, " (editat)", ext);

            var f = new StringBuilder();
            double jf = cuJointFade.Checked ? 0.3 : 0;
            for (int i = 0; i < keep.Count; i++)
            {
                double a = keep[i][0], z = keep[i][1], len = z - a;
                double fin = i > 0 ? Math.Min(jf, len / 3) : 0, fout = i < keep.Count - 1 ? Math.Min(jf, len / 3) : 0;
                if (hasV)
                {
                    f.Append("[0:v]trim=start=" + Sec(a) + ":end=" + Sec(z) + ",setpts=PTS-STARTPTS");
                    if (fin > 0) f.Append(",fade=t=in:st=0:d=" + Sec(fin));
                    if (fout > 0) f.Append(",fade=t=out:st=" + Sec(len - fout) + ":d=" + Sec(fout));
                    f.Append("[v" + i + "];");
                }
                if (hasA)
                {
                    // un fade foarte scurt la fiecare lipitură, ca să nu se audă pocnituri
                    double ain = i > 0 ? Math.Max(fin, Math.Min(0.015, len / 3)) : 0, aout = i < keep.Count - 1 ? Math.Max(fout, Math.Min(0.015, len / 3)) : 0;
                    f.Append("[0:a]atrim=start=" + Sec(a) + ":end=" + Sec(z) + ",asetpts=PTS-STARTPTS");
                    if (ain > 0) f.Append(",afade=t=in:st=0:d=" + Sec(ain));
                    if (aout > 0) f.Append(",afade=t=out:st=" + Sec(len - aout) + ":d=" + Sec(aout));
                    f.Append("[a" + i + "];");
                }
            }
            for (int i = 0; i < keep.Count; i++) f.Append((hasV ? "[v" + i + "]" : "") + (hasA ? "[a" + i + "]" : ""));
            f.Append("concat=n=" + keep.Count + ":v=" + (hasV ? 1 : 0) + ":a=" + (hasA ? 1 : 0) + (hasV ? "[vc]" : "") + (hasA ? "[ac]" : ""));

            fi = Math.Min(fi, total / 2); fo = Math.Min(fo, total / 2);
            if (hasV)
            {
                f.Append(";[vc]");
                var parts = new List<string>();
                if (fi > 0) parts.Add("fade=t=in:st=0:d=" + Sec(fi));
                if (fo > 0) parts.Add("fade=t=out:st=" + Sec(total - fo) + ":d=" + Sec(fo));
                if (parts.Count == 0) parts.Add("null");
                f.Append(string.Join(",", parts) + "[vout]");
            }
            if (hasA)
            {
                f.Append(";[ac]");
                var parts = new List<string>();
                if (fi > 0) parts.Add("afade=t=in:st=0:d=" + Sec(fi));
                if (fo > 0) parts.Add("afade=t=out:st=" + Sec(total - fo) + ":d=" + Sec(fo));
                if (parts.Count == 0) parts.Add("anull");
                f.Append(string.Join(",", parts) + "[aout]");
            }

            var j = new Job(Tool.Ffmpeg, "Tăieturi: " + Path.GetFileName(input)) { DurationSeconds = total };
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input, "-filter_complex", f.ToString());
            if (hasV) j.A("-map", "[vout]", "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p");
            if (hasA)
            {
                j.A("-map", "[aout]");
                if (hasV) j.A("-c:a", "aac", "-b:a", "192k");
                else if (ext == ".mp3") j.A("-b:a", "320k");
            }
            if (ext == ".mp4" || ext == ".mov" || ext == ".m4v") j.A("-movflags", "+faststart");
            j.A(outp);
            return j;
        }
    }
}
