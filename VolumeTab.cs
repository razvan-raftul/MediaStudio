using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        ListBox volFiles;
        Label volFilesLabel;
        VolumeSlider volSlider, volLevel;
        CheckBox volEqual;
        Action volSync;

        /// <summary>Tabul Volum: unul sau mai multe fișiere, un glisor de volum, Exportă.</summary>
        void BuildVolumeTab(TabPage page)
        {
            var b = new Builder(page);
            b.Buttons(
                Builder.B("&Adaugă fișiere…", (s, e) =>
                {
                    int n = 0;
                    foreach (var f in Dialogs.PickFiles("Alege fișierele la care schimbi volumul", Dialogs.MediaFilter, this)) n += AddVolFile(f);
                    VolAdded(n);
                }),
                Builder.B("Adaugă un folder…", (s, e) =>
                {
                    var dir = Dialogs.PickFolder("Alege folderul cu fișiere", null, this);
                    if (dir == null) return;
                    var exts = new HashSet<string>(VideoFormats.Concat(AudioFormats).Concat(new[] { "mpg", "mpeg", "3gp", "m2ts", "vob" }).Select(x => "." + x), StringComparer.OrdinalIgnoreCase);
                    int n = 0;
                    foreach (var f in Directory.GetFiles(dir).Where(f => exts.Contains(Path.GetExtension(f))).OrderBy(f => f)) n += AddVolFile(f);
                    VolAdded(n);
                }));
            volFiles = b.List(null, "&Fișiere");
            volFilesLabel = b.Table.Controls.OfType<Label>().FirstOrDefault(l => l.Text == "&Fișiere");
            var fileButtons = b.Buttons(
                Builder.B("Elimină fișierul selectat", (s, e) =>
                {
                    int i = volFiles.SelectedIndex;
                    if (i < 0) { Announce("Niciun fișier selectat."); return; }
                    volFiles.Items.RemoveAt(i);
                    if (volFiles.Items.Count > 0) volFiles.SelectedIndex = Math.Min(i, volFiles.Items.Count - 1);
                    volSync();
                    Announce("Eliminat. Rămân " + volFiles.Items.Count + ".");
                }),
                Builder.B("Golește lista", (s, e) => { volFiles.Items.Clear(); volSync(); Announce("Lista e goală."); }));

            volEqual = b.Check("volEqual", "Egalea&ză volumul tuturor fișierelor", false,
                "Fiecare fișier e ascultat întâi, apoi e adus la același nivel: cele încete urcă, cele tari coboară, ca toate să se audă la fel.");
            // glisoarele: săgeată sus/jos câte 1, Page Up/Page Down câte 5
            volSlider = new VolumeSlider(5, 400, 100, v => v + " la sută" + (v == 100 ? ", volumul original" : ""));
            volSlider.AccessibleDescription = "100 la sută înseamnă volumul original. Săgeată sus sau jos schimbă cu 1, Page Up sau Page Down cu 5.";
            var lblVol = AddSlider(b, "&Volum", volSlider);
            volLevel = new VolumeSlider(1, 100, 50, v => "nivel " + v + (v == 50 ? ", nivel obișnuit" : ""));
            volLevel.AccessibleDescription = "Nivelul la care se aduc toate fișierele. 50 e nivelul obișnuit al muzicii de pe internet; mai sus e mai tare, mai jos mai încet.";
            var lblLevel = AddSlider(b, "&Nivelul comun", volLevel);
            Action volMode = () =>
            {
                bool eq = volEqual.Checked;
                volSlider.Visible = lblVol.Visible = !eq;
                volLevel.Visible = lblLevel.Visible = eq;
            };
            volEqual.CheckedChanged += (s, e) => { volMode(); Announce(volEqual.Checked ? "Egalizare pornită. Alege nivelul comun." : "Egalizare oprită. Alege volumul."); };
            volMode();

            var exp = b.Buttons(Builder.B("&Exportă", (s, e) =>
            {
                if (volFiles.Items.Count == 0) { Announce("Adaugă întâi fișierele."); return; }
                var files = volFiles.Items.Cast<FileItem>().Select(f => f.Path).ToList();
                if (volEqual.Checked) { int lvl = volLevel.Value; Run(files.SelectMany(f => EqualizeJobs(f, lvl)).ToList()); }
                else { int pct = volSlider.Value; Run(files.Select(f => VolumeJob(f, pct)).ToList()); }
            }));
            // ordinea Tab: fișierele, glisorul, folderul, Exportă
            int ti = 0;
            foreach (Control c in b.Table.Controls.Cast<Control>().OrderBy(c => b.Table.GetRow(c)).ThenBy(c => b.Table.GetColumn(c))) c.TabIndex = ti++;

            volSync = () =>
            {
                bool any = volFiles.Items.Count > 0;
                volFiles.Visible = any; if (volFilesLabel != null) volFilesLabel.Visible = any; fileButtons[0].Parent.Visible = any;
            };
            volSync();
        }

        static Label AddSlider(Builder b, string label, VolumeSlider sl)
        {
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) };
            sl.AccessibleName = Builder.Plain(label);
            sl.Width = 300; sl.Anchor = AnchorStyles.Left | AnchorStyles.Right; sl.Margin = new Padding(3);
            int row = b.Table.RowCount;
            b.Table.Controls.Add(lbl, 0, row);
            b.Table.Controls.Add(sl, 1, row);
            b.Table.RowCount++;
            return lbl;
        }

        int AddVolFile(string f)
        {
            var it = new FileItem(f);
            if (volFiles.Items.Contains(it)) return 0;
            volFiles.Items.Add(it);
            return 1;
        }

        void VolAdded(int n)
        {
            volSync();
            if (n == 0) return;
            volFiles.SelectedIndex = volFiles.Items.Count - 1;
            var names = volFiles.Items.Cast<object>().Select(o => o.ToString()).ToList();
            string list = names.Count <= 5 ? string.Join(", ", names) : string.Join(", ", names.Take(5)) + " și încă " + (names.Count - 5);
            Announce("Am adăugat " + n + ". În listă sunt " + names.Count + ": " + list + ".");
        }

        /// <summary>Fișierul se exportă în același format, cu sunetul la volumul ales; imaginea video se copiază neschimbată.</summary>
        Job VolumeJob(string input, int pct)
        {
            string factor = (pct / 100.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            var j = new Job(Tool.Ffmpeg, "Volum " + pct + "%: " + Path.GetFileName(input)) { ProbeForDuration = input };
            FillVolumeArgs(j, input, "volume=" + factor, " (volum " + pct + "%)");
            return j;
        }

        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>Nivelul comun 1–100 în LUFS: 50 = -16 (muzica de pe internet), 100 = -6, 1 = -25,8.</summary>
        static double LevelToLufs(int lvl) { return -16.0 + (lvl - 50) * 0.2; }

        /// <summary>Egalizare în doi pași (loudnorm): întâi se măsoară fișierul, apoi se aplică exact corecția lui.</summary>
        IEnumerable<Job> EqualizeJobs(string input, int lvl)
        {
            string target = "I=" + LevelToLufs(lvl).ToString("0.0", Inv) + ":TP=-1.5:LRA=11";
            var measure = new Job(Tool.Ffmpeg, "Ascult nivelul: " + Path.GetFileName(input)) { ProbeForDuration = input };
            measure.A("-hide_banner", "-progress", "pipe:1", "-nostats", "-i", input, "-vn", "-af", "loudnorm=" + target + ":print_format=json", "-f", "null", "-");
            var apply = new Job(Tool.Ffmpeg, "Egalizez la nivelul " + lvl + ": " + Path.GetFileName(input)) { ProbeForDuration = input };
            // implicit (dacă măsurarea nu reușește): egalizare într-un singur pas
            FillVolumeArgs(apply, input, "loudnorm=" + target + ",aresample=48000", " (nivel " + lvl + ")");
            measure.After = (j, code) =>
            {
                if (code != 0) return;
                List<string> lines; lock (runner.TailLines) lines = runner.TailLines.ToList();
                var json = string.Join(" ", lines);
                Func<string, string> val = k =>
                {
                    var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + k + "\"\\s*:\\s*\"(-?[0-9.]+|-?inf)\"");
                    return m.Success ? m.Groups[1].Value : null;
                };
                string mi = val("input_i"), mtp = val("input_tp"), mlra = val("input_lra"), mth = val("input_thresh"), off = val("target_offset");
                if (mi == null || mtp == null || mlra == null || mth == null || off == null || mi.Contains("inf")) { Log("Măsurarea nu a dat valori; folosesc egalizarea într-un singur pas."); return; }
                Log("Nivel măsurat: " + mi + " LUFS.");
                int k2 = apply.Args.IndexOf("-af");
                if (k2 >= 0) apply.Args[k2 + 1] = "loudnorm=" + target + ":measured_I=" + mi + ":measured_TP=" + mtp + ":measured_LRA=" + mlra
                                                  + ":measured_thresh=" + mth + ":offset=" + off + ":linear=true,aresample=48000";
            };
            return new[] { measure, apply };
        }

        /// <summary>Același format ca originalul; imaginea video și subtitrările se copiază neschimbate, doar sunetul trece prin filtru.</summary>
        void FillVolumeArgs(Job j, string input, string filter, string suffix)
        {
            var ext = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
            if (ext.Length == 0) ext = "mp3";
            var outp = AutoOut(input, "Volum", Tag(suffix), ext);
            bool audioOnly = AudioFormats.Contains(ext);
            // sunetul trece prin filtru; imaginea (și coperta la mp3) și subtitrările se copiază neschimbate
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input, "-map", "0:v?", "-map", "0:a");
            if (!audioOnly) j.A("-map", "0:s?");
            j.A("-af", filter, "-c:v", "copy");
            if (!audioOnly) j.A("-c:s", "copy");
            switch (ext)
            {
                case "mp3": j.A("-c:a", "libmp3lame", "-q:a", "0"); break;
                case "m4a": case "aac": case "mp4": case "m4v": case "mov": j.A("-c:a", "aac", "-b:a", "256k"); break;
                case "ogg": j.A("-c:a", "libvorbis", "-q:a", "6"); break;
                case "opus": case "webm": j.A("-c:a", "libopus", "-b:a", "160k"); break;
                case "wav": j.A("-c:a", "pcm_s16le"); break;
                case "flac": j.A("-c:a", "flac"); break;
                case "mkv": case "mka": j.A("-c:a", "aac", "-b:a", "256k"); break;
                case "avi": j.A("-c:a", "libmp3lame", "-q:a", "0"); break;
            }
            if (ext == "mp4" || ext == "mov" || ext == "m4a" || ext == "m4v") j.A("-movflags", "+faststart");
            j.A(outp);
        }
    }

    /// <summary>
    /// Glisor accesibil: săgeată sus/dreapta = +1, jos/stânga = −1, Page Up = +5, Page Down = −5,
    /// Home = valoarea implicită, End = maximul. NVDA aude valoarea la fiecare schimbare.
    /// </summary>
    class VolumeSlider : TrackBar
    {
        readonly int def;
        readonly Func<int, string> say;
        public VolumeSlider(int min, int max, int value, Func<int, string> say)
        {
            Minimum = min; Maximum = max; Value = value; def = value; this.say = say;
            TickStyle = TickStyle.None; SmallChange = 1; LargeChange = 5;
        }

        public string Spoken { get { return say(Value); } }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int v = Value, nv = v;
            switch (e.KeyCode)
            {
                case Keys.Up: case Keys.Right: nv = v + 1; break;
                case Keys.Down: case Keys.Left: nv = v - 1; break;
                case Keys.PageUp: nv = v + 5; break;
                case Keys.PageDown: nv = v - 5; break;
                case Keys.Home: nv = def; break;
                case Keys.End: nv = Maximum; break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = true; e.SuppressKeyPress = true;
            nv = Math.Max(Minimum, Math.Min(Maximum, nv));
            if (nv != v) Value = nv; else Speak();
        }

        protected override void OnValueChanged(EventArgs e)
        {
            base.OnValueChanged(e);
            Speak();
        }

        void Speak()
        {
            if (!Focused || !IsHandleCreated) return;
            try { AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1); } catch { }
            try
            {
                AccessibilityObject.RaiseAutomationNotification(
                    System.Windows.Forms.Automation.AutomationNotificationKind.Other,
                    System.Windows.Forms.Automation.AutomationNotificationProcessing.MostRecent, Spoken);
            }
            catch { }
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acc(this); }

        class Acc : ControlAccessibleObject
        {
            readonly VolumeSlider s;
            public Acc(VolumeSlider s) : base(s) { this.s = s; }
            public override string Value { get { return s.Spoken; } set { } }
            public override AccessibleRole Role { get { return AccessibleRole.Slider; } }
        }
    }
}
