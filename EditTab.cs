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
        ListBox ccFiles;
        TextBox ccOut;
        ComboBox ccMode;
        TextBox spIn, spLen, spOut;

        void BuildCutTab(TabPage page) { BuildCutsSection(new Builder(page, edFields)); }
        void BuildOverlayTab(TabPage page) { BuildOverlaySection(new Builder(page, edFields)); }

        void BuildJoinTab(TabPage page)
        {
            var c = new Builder(page, edFields).Section("Lipire", false);
            c.Note("Adaugi fișierele în ordinea în care vrei să se audă sau să se vadă, apoi apeși Lipește. Iese un singur fișier.");
            ccFiles = c.List(null, "Fișiere de lipit, în ordine");
            c.Buttons(
                Builder.B("&Adaugă fișiere…", (s, e) => { foreach (var f in Dialogs.PickFiles("Alege fișierele de lipit", Dialogs.MediaFilter, this)) ccFiles.Items.Add(f); Announce(ccFiles.Items.Count + " fișiere în listă."); }),
                Builder.B("Mută în sus", (s, e) => MoveItem(ccFiles, -1)),
                Builder.B("Mută în jos", (s, e) => MoveItem(ccFiles, 1)),
                Builder.B("Elimină", (s, e) => RemoveSelected(ccFiles)));
            ccMode = c.Combo("ccMode", "Mod", new[] { "Rapid (fișierele trebuie să aibă același format)", "Sigur (merge cu orice fișiere, durează mai mult)" }, 1);
            ccOut = c.Path("ccOut", "Fișier rezultat (gol înseamnă în folderul Media Studio)", PathKind.SaveFile, Dialogs.MediaFilter);
            c.Buttons(Builder.B("&Lipește", (s, e) => Run(ConcatJob())));
        }

        void BuildSplitTab(TabPage page)
        {
            var sp = new Builder(page, edFields).Section("Împărțire în bucăți egale", false);
            spIn = sp.Path("spIn", "&Fișier de împărțit", PathKind.OpenFile, Dialogs.MediaFilter);
            spLen = sp.Text("spLen", "Lungimea unei bucăți (secunde, mm:ss sau hh:mm:ss)", "10:00");
            spOut = sp.Path("spOut", "Folder pentru bucăți (gol înseamnă folderul Media Studio)", PathKind.Folder);
            sp.Buttons(Builder.B("Îm&parte", (s, e) => Run(SplitJob())));
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
            var dir = spOut.Text.Trim().Length > 0 ? spOut.Text.Trim() : Settings.DownloadFolder;
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(input);
            var pattern = Path.Combine(dir, Path.GetFileNameWithoutExtension(input) + " - partea %03d" + ext);
            var j = new Job(Tool.Ffmpeg, "Împărțire: " + Path.GetFileName(input)) { ProbeForDuration = input };
            j.A("-hide_banner", "-y", "-progress", "pipe:1", "-nostats", "-i", input, "-map", "0", "-c", "copy", "-f", "segment", "-segment_time", Sec(len), "-reset_timestamps", "1", pattern);
            return j;
        }
    }
}
