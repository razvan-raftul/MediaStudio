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
        Action ccSync;
        TextBox spLen;

        void BuildCutTab(TabPage page) { BuildCutsSection(new Builder(page, edFields)); }
        void BuildOverlayTab(TabPage page) { BuildOverlaySection(new Builder(page, edFields)); }

        void BuildJoinTab(TabPage page)
        {
            var c = new Builder(page, edFields).Section("Lipire", false);
            c.Note("Adaugi fișierele în ordinea în care vrei să se audă sau să se vadă, apoi apeși Lipește. Iese un singur fișier. Merge cu orice fișiere, chiar dacă au formate diferite.");
            Button addBtn = null;
            addBtn = c.Buttons(Builder.B("&Adaugă fișiere…", (s, e) =>
            {
                int n = 0;
                foreach (var f in Dialogs.PickFiles("Alege fișierele de lipit", Dialogs.MediaFilter, this)) { ccFiles.Items.Add(new FileItem(f)); n++; }
                ccSync();
                if (n > 0) { ccFiles.SelectedIndex = ccFiles.Items.Count - 1; Announce("Am adăugat " + n + ". În listă sunt " + ccFiles.Items.Count + " fișiere."); }
            }))[0];
            // lista, mutarea, eliminarea și Lipește apar doar când există fișiere în listă
            Panel box;
            var g = c.Gate(out box);
            ccFiles = g.List(null, "Fișiere de lipit, în ordine");
            g.Buttons(
                Builder.B("Mută în sus", (s, e) => MoveItem(ccFiles, -1)),
                Builder.B("Mută în jos", (s, e) => MoveItem(ccFiles, 1)),
                Builder.B("Elimină", (s, e) => { RemoveSelected(ccFiles); ccSync(); }));
            g.Buttons(Builder.B("&Lipește", (s, e) => Run(ConcatJob())));
            ccSync = () => HideKeepFocus(box, ccFiles.Items.Count > 0, addBtn);
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
            var files = ccFiles.Items.Cast<FileItem>().Select(f => f.Path).ToList();
            if (files.Count < 2) { Announce("Adaugă cel puțin două fișiere."); return null; }
            // dacă toate sunt doar sunet (mp3, wav...), se lipește doar sunetul; altfel imagine și sunet
            bool allAudio = files.All(IsAudioOnly);
            var ext = Path.GetExtension(files[0]);
            if (allAudio && !IsAudioExt(files[0])) ext = ".m4a";
            var outp = AutoOut(files[0], "Lipite", "lipit", ext);
            var j = new Job(Tool.Ffmpeg, "Unire: " + files.Count + " fișiere");
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats");
            foreach (var f in files) j.A("-i", f);
            var sb = new StringBuilder();
            if (allAudio)
            {
                for (int i = 0; i < files.Count; i++) sb.Append("[" + i + ":a:0]");
                sb.Append("concat=n=" + files.Count + ":v=0:a=1[a]");
                j.A("-filter_complex", sb.ToString(), "-map", "[a]");
            }
            else
            {
                for (int i = 0; i < files.Count; i++) sb.Append("[" + i + ":v:0]scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1[v" + i + "];");
                for (int i = 0; i < files.Count; i++) sb.Append("[v" + i + "][" + i + ":a:0]");
                sb.Append("concat=n=" + files.Count + ":v=1:a=1[v][a]");
                j.A("-filter_complex", sb.ToString(), "-map", "[v]", "-map", "[a]");
            }
            j.DurationSeconds = files.Sum(f => Math.Max(0, Runner.ProbeDuration(f)));
            j.A(outp);
            return j;
        }

        Job SplitJob()
        {
            if (!Need(cuIn.Text, "fișierul de împărțit")) return null;
            double len = ParseTime(spLen.Text);
            if (len <= 0) { Announce("Scrie lungimea unei bucăți."); return null; }
            var input = cuIn.Text.Trim();
            var ext = Path.GetExtension(input);
            var first = AutoOut(input, "Împărțite", "partea_000", ext);
            var pattern = Path.Combine(Path.GetDirectoryName(first), Path.GetFileNameWithoutExtension(first).Replace("partea_000", "partea_%03d") + ext);
            var j = new Job(Tool.Ffmpeg, "Împărțire: " + Path.GetFileName(input)) { ProbeForDuration = input };
            j.A("-hide_banner", "-y", "-progress", "pipe:1", "-nostats", "-i", input, "-map", "0", "-c", "copy", "-f", "segment", "-segment_time", Sec(len), "-reset_timestamps", "1", pattern);
            return j;
        }
    }
}
