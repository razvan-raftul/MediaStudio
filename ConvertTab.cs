using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        readonly Dictionary<string, Control> cvFields = new Dictionary<string, Control>();
        ListBox cvFiles;
        Label cvFilesLabel;
        TextBox cvOutFolder;
        ComboBox cvFormat;
        Action cvSync;

        static readonly string[] VideoFormats = { "mp4", "mkv", "webm", "mov", "avi", "wmv", "flv", "ts", "m4v", "gif" };
        static readonly string[] AudioFormats = { "mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "wma", "ac3", "aiff", "mka" };

        /// <summary>Element din listă: arată doar numele fișierului, dar păstrează calea completă.</summary>
        class FileItem
        {
            public readonly string Path;
            public FileItem(string p) { Path = p; }
            public override string ToString() { return System.IO.Path.GetFileName(Path); }
            public override bool Equals(object o) { var f = o as FileItem; return f != null && string.Equals(f.Path, Path, StringComparison.OrdinalIgnoreCase); }
            public override int GetHashCode() { return Path.ToLowerInvariant().GetHashCode(); }
        }

        void BuildConvertTab(TabPage page)
        {
            var b = new Builder(page, cvFields);
            b.Buttons(
                Builder.B("&Adaugă fișiere…", (s, e) =>
                {
                    int n = 0;
                    foreach (var f in Dialogs.PickFiles("Alege fișierele de convertit", Dialogs.MediaFilter, this)) { var it = new FileItem(f); if (!cvFiles.Items.Contains(it)) { cvFiles.Items.Add(it); n++; } }
                    if (cvSync != null) cvSync();
                    if (n > 0) { AnnounceFiles(n); cvFiles.SelectedIndex = cvFiles.Items.Count - 1; }
                }),
                Builder.B("Adaugă un folder…", (s, e) => AddFolder(cvFiles)));
            cvFiles = b.List(null, "&Fișiere de convertit");
            cvFilesLabel = b.Table.Controls.OfType<Label>().FirstOrDefault(l => l.Text == "&Fișiere de convertit");
            var fileButtons = b.Buttons(
                Builder.B("Elimină fișierul selectat", (s, e) => RemoveSelected(cvFiles)),
                Builder.B("Golește lista", (s, e) => { cvFiles.Items.Clear(); if (cvSync != null) cvSync(); Announce("Lista e goală."); }));
            cvFormat = b.Combo("format", "Î&n", VideoFormats.Concat(AudioFormats), 0, false,
                "Formatul în care se transformă fișierele. mp3, wav, flac și celelalte formate audio păstrează doar sunetul.");
            cvFormat.SelectedItem = "mp3";
            cvOutFolder = b.Path("outFolder", "Folder de &salvare", PathKind.Folder, null, Settings.DownloadFolder);
            b.Buttons(Builder.B("Con&vertește", (s, e) =>
            {
                if (cvFiles.Items.Count == 0) { Announce("Adaugă întâi fișierele de convertit."); return; }
                Run(cvFiles.Items.Cast<FileItem>().Select(f => ConvertJob(f.Path)).ToList());
            }));

            // lista și butoanele ei apar doar când există fișiere
            cvSync = () =>
            {
                bool any = cvFiles.Items.Count > 0;
                cvFiles.Visible = any; if (cvFilesLabel != null) cvFilesLabel.Visible = any; fileButtons[0].Parent.Visible = any;
            };
            cvSync();
        }

        void AnnounceFiles(int added)
        {
            var names = cvFiles.Items.Cast<object>().Select(o => o.ToString()).ToList();
            string list = names.Count <= 5 ? string.Join(", ", names) : string.Join(", ", names.Take(5)) + " și încă " + (names.Count - 5);
            Announce("Am adăugat " + added + ". În listă sunt " + names.Count + ": " + list + ".");
        }

        void AddFolder(ListBox list)
        {
            var dir = Dialogs.PickFolder("Alege folderul cu fișiere media", null, this);
            if (dir == null) return;
            var exts = new HashSet<string>(VideoFormats.Concat(AudioFormats).Concat(new[] { "mpg", "mpeg", "3gp", "m2ts", "vob" }).Select(x => "." + x), StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var f in Directory.GetFiles(dir).Where(f => exts.Contains(Path.GetExtension(f))).OrderBy(f => f))
            {
                object it = list == cvFiles ? (object)new FileItem(f) : f;
                if (!list.Items.Contains(it)) { list.Items.Add(it); n++; }
            }
            if (list == cvFiles && cvSync != null) cvSync();
            if (list == cvFiles && n > 0) AnnounceFiles(n);
            else Announce("Am adăugat " + n + " fișiere. În listă sunt " + list.Items.Count + ".");
        }

        void RemoveSelected(ListBox list)
        {
            int i = list.SelectedIndex;
            if (i < 0) { Announce("Niciun fișier selectat."); return; }
            list.Items.RemoveAt(i);
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(i, list.Items.Count - 1);
            if (list == cvFiles && cvSync != null) cvSync();
            Announce("Eliminat. Rămân " + list.Items.Count + ".");
        }

        static string Code(ComboBox c) { var w = FirstWord(c.Text); return w; }
        static bool IsAuto(ComboBox c) { var t = c.Text.Trim(); return t.Length == 0 || t.StartsWith("Automat") || t.StartsWith("Original") || t == "Implicit" || t == "Nu"; }

        Job ConvertJob(string input)
        {
            var fmt = cvFormat.Text.Trim();
            bool audioOnly = AudioFormats.Contains(fmt);
            var outp = OutPath(input, cvOutFolder.Text.Trim(), "", fmt);
            var j = new Job(Tool.Ffmpeg, "Conversie: " + Path.GetFileName(input) + " în " + fmt) { ProbeForDuration = input };
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input);
            if (audioOnly) j.A("-vn");
            else if (fmt == "gif") j.A("-an", "-vf", "fps=12,scale=480:-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse");
            else if (fmt == "mp4" || fmt == "m4v" || fmt == "mov") j.A("-c:v", "libx264", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k");
            switch (fmt)
            {
                case "mp3": j.A("-c:a", "libmp3lame", "-q:a", "0"); break;
                case "m4a": case "aac": j.A("-c:a", "aac", "-b:a", "256k"); break;
                case "ogg": j.A("-c:a", "libvorbis", "-q:a", "6"); break;
                case "opus": j.A("-c:a", "libopus", "-b:a", "160k"); break;
            }
            if (fmt == "mp4" || fmt == "mov" || fmt == "m4a" || fmt == "m4v") j.A("-movflags", "+faststart");
            j.A(outp);
            return j;
        }

        static string Atempo(double speed)
        {
            // atempo acceptă 0.5–2.0; pentru alte valori se înlănțuie
            var parts = new List<string>();
            double s = speed;
            while (s > 2.0) { parts.Add("atempo=2.0"); s /= 2.0; }
            while (s < 0.5) { parts.Add("atempo=0.5"); s /= 0.5; }
            parts.Add("atempo=" + s.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(",", parts);
        }
    }
}
