using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MediaStudio
{
    /// <summary>O poză pusă peste videoclip: când apare, cât stă, unde și cât de mare.</summary>
    class Overlay
    {
        public string Image;
        public double Start, Duration;
        public int Position, Size;
        public bool Fade;
    }

    partial class MainForm
    {
        static readonly string[] OvPositions = { "Centru", "Stânga sus", "Dreapta sus", "Stânga jos", "Dreapta jos", "Sus, la mijloc", "Jos, la mijloc" };
        static readonly string[] OvSizes = { "Tot ecranul, se vede videoclipul pe margini", "Tot ecranul, cu fundal negru (acoperă videoclipul)", "Jumătate din ecran", "O treime din ecran", "Un sfert din ecran", "Mărimea originală a pozei" };

        readonly List<Overlay> overlays = new List<Overlay>();
        TextBox ovVideo, ovImage, ovStart, ovDur, ovOut;
        ComboBox ovPos, ovSize;
        CheckBox ovFade;
        ListBox ovList;

        void BuildOverlaySection(Builder b)
        {
            var o = b.Section("Poze peste videoclip", false);
            o.Note("Alegi videoclipul, apoi pentru fiecare poză: fișierul, la ce secundă apare, câte secunde stă, unde stă și cât de mare e. Apeși Adaugă poza în listă. Poți pune oricâte poze. La sfârșit apeși Pune pozele pe videoclip.");
            ovVideo = o.Path("ovVideo", "&Videoclip", PathKind.OpenFile, Dialogs.VideoFilter);
            ovImage = o.Path(null, "&Poză", PathKind.OpenFile, Dialogs.ImageFilter);
            ovStart = o.Text(null, "Apare la (secunde sau mm:ss)", "0", false, "De exemplu 5 înseamnă secunda 5, iar 1:30 înseamnă minutul 1 și 30 de secunde.");
            ovDur = o.Text(null, "Stă pe ecran (secunde)", "5", false, "Câte secunde rămâne poza pe ecran.");
            ovPos = o.Combo(null, "Unde stă poza", OvPositions);
            ovSize = o.Combo(null, "Cât de mare e poza", OvSizes, 2);
            ovFade = o.Check(null, "Apare și dispare lin (în jumătate de secundă)", true);
            o.Buttons(
                Builder.B("A&daugă poza în listă", (s, e) => AddOverlay(false)),
                Builder.B("Înlocuiește poza selectată cu valorile de mai sus", (s, e) => AddOverlay(true)));
            ovList = o.List(null, "Pozele puse pe videoclip", 120);
            ovList.SelectedIndexChanged += (s, e) => LoadOverlay();
            o.Buttons(
                Builder.B("Elimină poza selectată", (s, e) =>
                {
                    int i = ovList.SelectedIndex; if (i < 0) { Announce("Nu e selectată nicio poză."); return; }
                    overlays.RemoveAt(i); RefreshOverlays(Math.Min(i, overlays.Count - 1));
                    Announce("Poza a fost eliminată. Au rămas " + overlays.Count + ".");
                }),
                Builder.B("Golește lista de poze", (s, e) => { overlays.Clear(); RefreshOverlays(-1); Announce("Lista de poze e goală."); }));
            ovOut = o.Path("ovOut", "Fișier rezultat", PathKind.SaveFile, Dialogs.VideoFilter, "");
            o.Buttons(Builder.B("Pune po&zele pe videoclip", (s, e) => Run(OverlayJob())));
        }

        static string Clock(double s)
        {
            int m = (int)(s / 60); double r = s - m * 60;
            string sec = r.ToString(r == Math.Floor(r) ? "00" : "00.##", CultureInfo.InvariantCulture);
            return m + ":" + sec;
        }

        static string Describe(Overlay o, int index)
        {
            string secs = o.Duration == 1 ? "o secundă" : Sec(o.Duration) + " secunde";
            return (index + 1) + ". " + Path.GetFileName(o.Image) + ", apare la " + Clock(o.Start) + ", stă " + secs + ", " +
                OvPositions[o.Position].ToLowerInvariant() + ", " + OvSizes[o.Size].ToLowerInvariant() + (o.Fade ? ", lin" : "");
        }

        void RefreshOverlays(int select)
        {
            overlays.Sort((a, c) => a.Start.CompareTo(c.Start));
            ovList.Items.Clear();
            for (int i = 0; i < overlays.Count; i++) ovList.Items.Add(Describe(overlays[i], i));
            if (select >= 0 && select < ovList.Items.Count) ovList.SelectedIndex = select;
        }

        void AddOverlay(bool replace)
        {
            int sel = ovList.SelectedIndex;
            if (replace && sel < 0) { Announce("Alege întâi din listă poza pe care o înlocuiești."); return; }
            if (!Need(ovImage.Text, "poza")) { ovImage.Focus(); return; }
            double st = ParseTime(ovStart.Text), du = ParseTime(ovDur.Text);
            if (st < 0) { Announce("Scrie la ce secundă apare poza."); ovStart.Focus(); return; }
            if (du <= 0) { Announce("Scrie câte secunde stă poza pe ecran."); ovDur.Focus(); return; }
            var ov = new Overlay { Image = ovImage.Text.Trim(), Start = st, Duration = du, Position = ovPos.SelectedIndex, Size = ovSize.SelectedIndex, Fade = ovFade.Checked };
            if (replace) overlays[sel] = ov; else overlays.Add(ov);
            RefreshOverlays(-1);
            int idx = overlays.IndexOf(ov);
            Announce((replace ? "Am înlocuit: " : "Am adăugat: ") + Describe(ov, idx) + ". În listă sunt " + overlays.Count + " poze.");
        }

        void LoadOverlay()
        {
            int i = ovList.SelectedIndex;
            if (i < 0 || i >= overlays.Count) return;
            var o = overlays[i];
            ovImage.Text = o.Image; ovStart.Text = Sec(o.Start); ovDur.Text = Sec(o.Duration);
            ovPos.SelectedIndex = o.Position; ovSize.SelectedIndex = o.Size; ovFade.Checked = o.Fade;
        }

        /// <summary>Lățimea și înălțimea videoclipului așa cum se vede (ține cont de rotirea filmărilor de pe telefon).</summary>
        static bool ProbeSize(string file, out int w, out int h)
        {
            w = h = 0;
            var o = Runner.Capture(Tool.Ffprobe, "-v error -select_streams v:0 -show_entries stream=width,height:stream_tags=rotate:stream_side_data=rotation -of default=nw=1 " + Runner.Quote(file), 15000);
            if (o == null) return false;
            int rot = 0;
            foreach (var raw in o.Replace("\r", "").Split('\n'))
            {
                var l = raw.Trim(); int v;
                if (l.StartsWith("width=") && int.TryParse(l.Substring(6), out v)) w = v;
                else if (l.StartsWith("height=") && int.TryParse(l.Substring(7), out v)) h = v;
                else if (l.StartsWith("rotation=") && int.TryParse(l.Substring(9), out v)) rot = v;
                else if (l.StartsWith("TAG:rotate=") && int.TryParse(l.Substring(11), out v) && rot == 0) rot = v;
            }
            if (Math.Abs(rot) % 180 == 90) { int t = w; w = h; h = t; }
            return w > 0 && h > 0;
        }

        Job OverlayJob()
        {
            if (!Need(ovVideo.Text, "videoclipul")) return null;
            if (overlays.Count == 0) { Announce("Adaugă cel puțin o poză în listă."); return null; }
            foreach (var o in overlays) if (!File.Exists(o.Image)) { Announce("Nu găsesc poza " + o.Image + "."); return null; }
            var input = ovVideo.Text.Trim();
            int W, H;
            if (!ProbeSize(input, out W, out H)) { ShowError("Nu pot afla mărimea videoclipului. Verifică dacă FFprobe e configurat în Setări și dacă fișierul e un videoclip."); return null; }

            var ext = Path.GetExtension(input).ToLowerInvariant();
            if (ext != ".mp4" && ext != ".mov" && ext != ".mkv" && ext != ".m4v") ext = ".mp4";
            var outp = ovOut.Text.Trim().Length > 0 ? ovOut.Text.Trim() : OutPath(input, null, " (cu poze)", ext);

            var j = new Job(Tool.Ffmpeg, "Poze peste videoclip: " + Path.GetFileName(input)) { ProbeForDuration = input };
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input);
            foreach (var o in overlays) j.A("-loop", "1", "-framerate", "30", "-t", Sec(o.Duration), "-i", o.Image);

            var f = new StringBuilder();
            string prev = "0:v";
            for (int i = 0; i < overlays.Count; i++)
            {
                var o = overlays[i];
                int n = i + 1;
                f.Append("[" + n + ":v]");
                switch (o.Size)
                {
                    case 0: f.Append("scale=" + W + ":" + H + ":force_original_aspect_ratio=decrease,"); break;
                    case 1: f.Append("scale=" + W + ":" + H + ":force_original_aspect_ratio=decrease,pad=" + W + ":" + H + ":(ow-iw)/2:(oh-ih)/2:black,"); break;
                    case 2: f.Append("scale=" + (W / 2) + ":" + (H / 2) + ":force_original_aspect_ratio=decrease,"); break;
                    case 3: f.Append("scale=" + (W / 3) + ":" + (H / 3) + ":force_original_aspect_ratio=decrease,"); break;
                    case 4: f.Append("scale=" + (W / 4) + ":" + (H / 4) + ":force_original_aspect_ratio=decrease,"); break;
                }
                f.Append("format=rgba");
                if (o.Fade)
                {
                    double fd = Math.Min(0.5, o.Duration / 3);
                    f.Append(",fade=t=in:st=0:d=" + Sec(fd) + ":alpha=1,fade=t=out:st=" + Sec(o.Duration - fd) + ":d=" + Sec(fd) + ":alpha=1");
                }
                f.Append(",setpts=PTS-STARTPTS+" + Sec(o.Start) + "/TB[p" + n + "];");

                bool full = o.Size <= 1;
                string m = Math.Max(8, Math.Min(W, H) / 30).ToString(CultureInfo.InvariantCulture);
                string x, y;
                switch (full ? 0 : o.Position)
                {
                    case 1: x = m; y = m; break;
                    case 2: x = "W-w-" + m; y = m; break;
                    case 3: x = m; y = "H-h-" + m; break;
                    case 4: x = "W-w-" + m; y = "H-h-" + m; break;
                    case 5: x = "(W-w)/2"; y = m; break;
                    case 6: x = "(W-w)/2"; y = "H-h-" + m; break;
                    default: x = "(W-w)/2"; y = "(H-h)/2"; break;
                }
                string label = i == overlays.Count - 1 ? "vout" : "v" + n;
                f.Append("[" + prev + "][p" + n + "]overlay=x=" + x + ":y=" + y + ":eof_action=pass:enable='between(t," + Sec(o.Start) + "," + Sec(o.Start + o.Duration) + ")'[" + label + "]");
                if (i < overlays.Count - 1) f.Append(";");
                prev = label;
            }

            j.A("-filter_complex", f.ToString(), "-map", "[vout]", "-map", "0:a?",
                "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "copy");
            if (ext == ".mp4" || ext == ".mov" || ext == ".m4v") j.A("-movflags", "+faststart");
            j.A(outp);
            return j;
        }
    }
}
