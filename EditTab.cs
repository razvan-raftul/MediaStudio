using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        readonly Dictionary<string, Control> edFields = new Dictionary<string, Control>();
        TextBox trIn, trStart, trEnd, trOut;
        ComboBox trMode;
        ListBox ccFiles;
        TextBox ccOut;
        ComboBox ccMode;
        TextBox spIn, spLen, spOut;

        void BuildEditTab(TabPage page)
        {
            var b = new Builder(page, edFields);

            var t = b.Section("Tăiere", false);
            trIn = t.Path("trIn", "&Fișier de tăiat", PathKind.OpenFile, Dialogs.MediaFilter);
            trStart = t.Text("trStart", "Î&nceput (hh:mm:ss sau secunde)", "00:00:00");
            trEnd = t.Text("trEnd", "S&fârșit (hh:mm:ss sau secunde)", "", false, "Gol înseamnă până la sfârșitul fișierului.");
            trMode = t.Combo("trMode", "Mod", new[] { "Rapid, fără reconversie (taie la cel mai apropiat cadru cheie)", "Exact, cu reconversie (mai lent)" });
            trOut = t.Path("trOut", "Fișier rezultat", PathKind.SaveFile, Dialogs.MediaFilter, "");
            t.Buttons(Builder.B("&Taie", (s, e) => Run(TrimJob())));

            var c = b.Section("Unire (concatenare)", false);
            ccFiles = c.List("ccFiles", "Fișiere de unit, în ordine");
            c.Buttons(
                Builder.B("Adaugă fișiere…", (s, e) => { foreach (var f in Dialogs.PickFiles("Alege fișierele de unit", Dialogs.MediaFilter, this)) ccFiles.Items.Add(f); Announce(ccFiles.Items.Count + " fișiere în listă."); }),
                Builder.B("Mută în sus", (s, e) => MoveItem(ccFiles, -1)),
                Builder.B("Mută în jos", (s, e) => MoveItem(ccFiles, 1)),
                Builder.B("Elimină", (s, e) => RemoveSelected(ccFiles)));
            ccMode = c.Combo("ccMode", "Mod", new[] { "Rapid, fără reconversie (fișierele trebuie să aibă același format)", "Cu reconversie (merge cu fișiere diferite)" });
            ccOut = c.Path("ccOut", "Fișier rezultat", PathKind.SaveFile, Dialogs.MediaFilter);
            c.Buttons(Builder.B("&Unește", (s, e) => Run(ConcatJob())));

            var sp = b.Section("Împărțire în bucăți egale", true);
            spIn = sp.Path("spIn", "Fișier de împărțit", PathKind.OpenFile, Dialogs.MediaFilter);
            spLen = sp.Text("spLen", "Lungimea unei bucăți (hh:mm:ss sau secunde)", "00:10:00");
            spOut = sp.Path("spOut", "Folder pentru bucăți", PathKind.Folder);
            sp.Buttons(Builder.B("Împarte", (s, e) => Run(SplitJob())));

            PresetBar(b, "editare", () => edFields);
        }

        void MoveItem(ListBox l, int dir)
        {
            int i = l.SelectedIndex, k = i + dir;
            if (i < 0 || k < 0 || k >= l.Items.Count) return;
            var it = l.Items[i]; l.Items.RemoveAt(i); l.Items.Insert(k, it); l.SelectedIndex = k;
            Announce("Poziția " + (k + 1) + " din " + l.Items.Count);
        }

        static double ParseTime(string s)
        {
            s = (s ?? "").Trim().Replace(',', '.');
            if (s.Length == 0) return -1;
            double total = 0;
            foreach (var p in s.Split(':')) { double v; if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return -1; total = total * 60 + v; }
            return total;
        }
        static string Sec(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        bool Need(string path, string what)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path.Trim())) { Announce("Alege " + what + "."); return false; }
            return true;
        }

        Job TrimJob()
        {
            if (!Need(trIn.Text, "fișierul de tăiat")) return null;
            var input = trIn.Text.Trim();
            double st = ParseTime(trStart.Text), en = ParseTime(trEnd.Text);
            if (st < 0) st = 0;
            if (en >= 0 && en <= st) { Announce("Sfârșitul trebuie să fie după început."); return null; }
            var outp = trOut.Text.Trim().Length > 0 ? trOut.Text.Trim() : OutPath(input, null, " (tăiat)", Path.GetExtension(input));
            var j = new Job(Tool.Ffmpeg, "Tăiere: " + Path.GetFileName(input));
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-ss", Sec(st), "-i", input);
            if (en >= 0) { j.A("-t", Sec(en - st)); j.DurationSeconds = en - st; } else j.ProbeForDuration = input;
            if (trMode.SelectedIndex == 0) j.A("-map", "0", "-c", "copy", "-avoid_negative_ts", "make_zero");
            j.A(outp);
            if (en < 0) j.After = null;
            return j;
        }

        Job ConcatJob()
        {
            var files = ccFiles.Items.Cast<string>().ToList();
            if (files.Count < 2) { Announce("Adaugă cel puțin două fișiere."); return null; }
            var outp = ccOut.Text.Trim().Length > 0 ? ccOut.Text.Trim() : OutPath(files[0], null, " (unit)", Path.GetExtension(files[0]));
            var j = new Job(Tool.Ffmpeg, "Unire: " + files.Count + " fișiere");
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats");
            if (ccMode.SelectedIndex == 0)
            {
                var list = Path.Combine(Path.GetTempPath(), "mediastudio_concat_" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(list, string.Join("\n", files.Select(f => "file '" + f.Replace("'", "'\\''") + "'")), new UTF8Encoding(false));
                j.TempFile = list;
                j.A("-f", "concat", "-safe", "0", "-i", list, "-c", "copy");
            }
            else
            {
                foreach (var f in files) j.A("-i", f);
                var sb = new StringBuilder();
                for (int i = 0; i < files.Count; i++) sb.Append("[" + i + ":v:0]scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1[v" + i + "];");
                for (int i = 0; i < files.Count; i++) sb.Append("[v" + i + "][" + i + ":a:0]");
                sb.Append("concat=n=" + files.Count + ":v=1:a=1[v][a]");
                j.A("-filter_complex", sb.ToString(), "-map", "[v]", "-map", "[a]");
            }
            j.DurationSeconds = 0;
            j.A(outp);
            return j;
        }

        Job SplitJob()
        {
            if (!Need(spIn.Text, "fișierul de împărțit")) return null;
            double len = ParseTime(spLen.Text);
            if (len <= 0) { Announce("Scrie lungimea unei bucăți."); return null; }
            var input = spIn.Text.Trim();
            var dir = spOut.Text.Trim().Length > 0 ? spOut.Text.Trim() : Path.GetDirectoryName(input);
            var ext = Path.GetExtension(input);
            var pattern = Path.Combine(dir, Path.GetFileNameWithoutExtension(input) + " - partea %03d" + ext);
            var j = new Job(Tool.Ffmpeg, "Împărțire: " + Path.GetFileName(input)) { ProbeForDuration = input };
            j.A("-hide_banner", "-y", "-progress", "pipe:1", "-nostats", "-i", input, "-map", "0", "-c", "copy", "-f", "segment", "-segment_time", Sec(len), "-reset_timestamps", "1", pattern);
            return j;
        }
    }
}
