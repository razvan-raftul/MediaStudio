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
        public int Fade = 0; // 0 implicit, 1 brusc, 2 început lin, 3 final lin, 4 amândouă, 5 apare lin peste poza dinainte
    }

    partial class MainForm
    {
        static readonly string[] OvPositions = { "Centru", "Stânga sus", "Dreapta sus", "Stânga jos", "Dreapta jos", "Sus, la mijloc", "Jos, la mijloc" };
        static readonly string[] OvFades = { "Implicit, programul alege trecerea cea mai frumoasă", "Fără trecere lină, apare și dispare brusc", "Doar începutul lin, apare lin", "Doar finalul lin, dispare lin", "Începutul și finalul lin", "Apare lin peste poza dinainte, fără negru între ele" };
        static readonly string[] OvSizes = { "Tot ecranul, se vede videoclipul pe margini", "Tot ecranul, cu fundal negru (acoperă videoclipul)", "Jumătate din ecran", "O treime din ecran", "Un sfert din ecran", "Mărimea originală a pozei" };

        readonly List<Overlay> overlays = new List<Overlay>();
        TextBox ovVideo, ovImage, ovStart, ovDur;
        ComboBox ovPos, ovSize;
        ComboBox ovFade;
        ListBox ovList;

        void BuildOverlaySection(Builder b)
        {
            var o = b.Section("Poze peste videoclip sau peste un sunet", false);
            o.Note("Alegi un videoclip sau un sunet, de exemplu un mp3. Apoi pentru fiecare poză: fișierul, la ce secundă apare, câte secunde stă, unde stă și cât de mare e. Apeși Adaugă poza în listă. Poți pune oricâte poze. La sfârșit apeși Pune pozele. Dacă ai ales un sunet, iese un videoclip mp4 cu mărimea primei poze, iar unde nu e nicio poză ecranul e negru.");
            ovVideo = o.Path("ovVideo", "&Videoclip sau sunet", PathKind.OpenFile, Dialogs.MediaFilter);
            ovVideo.TextChanged += (s, e) => { if (ovSize != null && IsAudioExt(ovVideo.Text)) ovSize.SelectedIndex = 1; };
            ovImage = o.Path(null, "&Poză", PathKind.OpenFile, Dialogs.ImageFilter);
            ovStart = o.Text(null, "Apare la (secunde sau mm:ss)", "0", false, "De exemplu 5 înseamnă secunda 5, iar 1:30 înseamnă minutul 1 și 30 de secunde.");
            ovDur = o.Text(null, "Stă pe ecran (secunde)", "5", false, "Câte secunde rămâne poza pe ecran.");
            ovPos = o.Combo(null, "Unde stă poza", OvPositions);
            ovSize = o.Combo(null, "Cât de mare e poza", OvSizes, 2);
            ovFade = o.Combo(null, "Cum apare și dispare poza (trecerea ține jumătate de secundă)", OvFades, 0);
            o.Buttons(
                Builder.B("A&daugă poza în listă", (s, e) => AddOverlay(false)),
                Builder.B("Înlocuiește poza selectată cu valorile de mai sus", (s, e) => AddOverlay(true)));
            ovList = o.List(null, "Pozele puse", 120);
            ovList.SelectedIndexChanged += (s, e) => LoadOverlay();
            o.Buttons(
                Builder.B("Elimină poza selectată", (s, e) =>
                {
                    int i = ovList.SelectedIndex; if (i < 0) { Announce("Nu e selectată nicio poză."); return; }
                    overlays.RemoveAt(i); RefreshOverlays(Math.Min(i, overlays.Count - 1));
                    Announce("Poza a fost eliminată. Au rămas " + overlays.Count + ".");
                }),
                Builder.B("Golește lista de poze", (s, e) => { overlays.Clear(); RefreshOverlays(-1); Announce("Lista de poze e goală."); }));
            o.Buttons(Builder.B("Pune po&zele", (s, e) => Run(OverlayJob())));
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
                OvPositions[o.Position].ToLowerInvariant() + ", " + OvSizes[o.Size].ToLowerInvariant() + ", " + OvFades[o.Fade].ToLowerInvariant();
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
            var ov = new Overlay { Image = ovImage.Text.Trim(), Start = st, Duration = du, Position = ovPos.SelectedIndex, Size = ovSize.SelectedIndex, Fade = Math.Max(0, ovFade.SelectedIndex) };
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
            ovPos.SelectedIndex = o.Position; ovSize.SelectedIndex = o.Size; ovFade.SelectedIndex = o.Fade;
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

        static readonly string[] AudioExts = { ".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg", ".opus", ".wma", ".ac3", ".mka" };

        static bool IsAudioExt(string file)
        {
            try { return AudioExts.Contains(Path.GetExtension(file.Trim()).ToLowerInvariant()); } catch { return false; }
        }

        /// <summary>Adevărat dacă fișierul are sunet, dar nicio imagine în mișcare (coperta unui mp3 nu se pune).</summary>
        static bool IsAudioOnly(string file)
        {
            if (IsAudioExt(file)) return true;
            var v = Runner.Capture(Tool.Ffprobe, "-v error -select_streams v -show_entries stream_disposition=attached_pic -of csv=p=0 " + Runner.Quote(file), 15000);
            if (v == null) return false;
            foreach (var l in v.Replace("\r", "").Split('\n')) if (l.Trim() == "0") return false;
            var a = Runner.Capture(Tool.Ffprobe, "-v error -select_streams a -show_entries stream=index -of csv=p=0 " + Runner.Quote(file), 15000);
            return a != null && a.Trim().Length > 0;
        }

        Job OverlayJob()
        {
            if (!Need(ovVideo.Text, "videoclipul sau sunetul")) return null;
            if (overlays.Count == 0) { Announce("Adaugă cel puțin o poză în listă."); return null; }
            foreach (var o in overlays) if (!File.Exists(o.Image)) { Announce("Nu găsesc poza " + o.Image + "."); return null; }
            var input = ovVideo.Text.Trim();
            if (IsAudioOnly(input)) return OverlayAudioJob(input);
            int W, H;
            if (!ProbeSize(input, out W, out H)) { ShowError("Nu pot afla mărimea videoclipului. Verifică dacă FFprobe e configurat în Setări și dacă fișierul e un videoclip."); return null; }

            var ext = Path.GetExtension(input).ToLowerInvariant();
            if (ext != ".mp4" && ext != ".mov" && ext != ".mkv" && ext != ".m4v") ext = ".mp4";
            var outp = AutoOut(input, "Cu poze", "cu_poze", ext);

            var j = new Job(Tool.Ffmpeg, "Poze peste videoclip: " + Path.GetFileName(input)) { ProbeForDuration = input };
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input);
            double[] odur; bool[] fin, fout;
            OverlayPlan(out odur, out fin, out fout);
            for (int i = 0; i < overlays.Count; i++) j.A("-loop", "1", "-framerate", "30", "-t", Sec(odur[i]), "-i", overlays[i].Image);

            var f = OverlayFilter(W, H, 1, "0:v", odur, fin, fout);
            j.A("-filter_complex", f.ToString(), "-map", "[vout]", "-map", "0:a?",
                "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "copy");
            if (ext == ".mp4" || ext == ".mov" || ext == ".m4v") j.A("-movflags", "+faststart");
            j.A(outp);
            return j;
        }

        /// <summary>Cât stă fiecare poză de fapt și unde are trecere lină. La „apare lin peste poza dinainte”, poza dinainte mai stă jumătate de secundă, fără să se stingă, ca noua poză să apară peste ea.</summary>
        void OverlayPlan(out double[] dur, out bool[] fin, out bool[] fout)
        {
            int n = overlays.Count;
            dur = new double[n]; fin = new bool[n]; fout = new bool[n];
            var mode = new int[n];
            for (int i = 0; i < n; i++)
            {
                var o = overlays[i];
                dur[i] = o.Duration;
                int m = o.Fade;
                if (m == 0)
                {
                    // Implicit: lipită de poza dinainte, se topește peste ea; prima poză de la secunda 0 pornește direct, fără negru;
                    // altfel apare lin. Se stinge lin doar dacă după ea nu urmează imediat altă poză.
                    bool lipitaInainte = i > 0 && o.Start <= overlays[i - 1].Start + overlays[i - 1].Duration + 0.05;
                    bool lipitaDupa = i < n - 1 && overlays[i + 1].Start <= o.Start + o.Duration + 0.05;
                    if (lipitaInainte) mode[i] = 5;
                    else fin[i] = o.Start > 0.01;
                    fout[i] = !lipitaDupa;
                }
                else
                {
                    mode[i] = m;
                    fin[i] = m == 2 || m == 4;
                    fout[i] = m == 3 || m == 4;
                }
                if (mode[i] == 5) fin[i] = true;
            }
            for (int i = 1; i < n; i++)
            {
                if (mode[i] != 5) continue;
                double fd = Math.Min(0.5, overlays[i].Duration / 3);
                double need = overlays[i].Start + fd - overlays[i - 1].Start;
                if (need > dur[i - 1]) dur[i - 1] = need;
                fout[i - 1] = false;
            }
        }

        /// <summary>Lanțul de filtre care pune pozele, pe rând, peste imaginea de bază. Pozele încep de la intrarea firstInput.</summary>
        StringBuilder OverlayFilter(int W, int H, int firstInput, string baseLabel, double[] dur, bool[] fin, bool[] fout)
        {
            var f = new StringBuilder();
            string prev = baseLabel;
            for (int i = 0; i < overlays.Count; i++)
            {
                var o = overlays[i];
                int n = i + 1;
                f.Append("[" + (firstInput + i) + ":v]");
                switch (o.Size)
                {
                    case 0: f.Append("scale=" + W + ":" + H + ":force_original_aspect_ratio=decrease,"); break;
                    case 1: f.Append("scale=" + W + ":" + H + ":force_original_aspect_ratio=decrease,pad=" + W + ":" + H + ":(ow-iw)/2:(oh-ih)/2:black,"); break;
                    case 2: f.Append("scale=" + (W / 2) + ":" + (H / 2) + ":force_original_aspect_ratio=decrease,"); break;
                    case 3: f.Append("scale=" + (W / 3) + ":" + (H / 3) + ":force_original_aspect_ratio=decrease,"); break;
                    case 4: f.Append("scale=" + (W / 4) + ":" + (H / 4) + ":force_original_aspect_ratio=decrease,"); break;
                }
                f.Append("format=rgba");
                double fdi = Math.Min(0.5, o.Duration / 3), fdo = Math.Min(0.5, dur[i] / 3);
                if (fin[i]) f.Append(",fade=t=in:st=0:d=" + Sec(fdi) + ":alpha=1");
                if (fout[i]) f.Append(",fade=t=out:st=" + Sec(dur[i] - fdo) + ":d=" + Sec(fdo) + ":alpha=1");
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
                f.Append("[" + prev + "][p" + n + "]overlay=x=" + x + ":y=" + y + ":eof_action=pass:enable='between(t," + Sec(o.Start) + "," + Sec(o.Start + dur[i]) + ")'[" + label + "]");
                if (i < overlays.Count - 1) f.Append(";");
                prev = label;
            }

            return f;
        }

        /// <summary>Pozele puse pe un sunet: fundal negru cu mărimea primei poze, cât ține sunetul, și pozele peste el.</summary>
        Job OverlayAudioJob(string input)
        {
            double dur = Runner.ProbeDuration(input);
            if (dur <= 0) { ShowError("Nu pot afla cât durează sunetul. Verifică dacă FFprobe e configurat în Setări și dacă fișierul e un sunet."); return null; }
            int W, H;
            if (!ProbeSize(overlays[0].Image, out W, out H)) { ShowError("Nu pot afla mărimea primei poze: " + Path.GetFileName(overlays[0].Image) + "."); return null; }
            double k = Math.Min(1.0, 1920.0 / Math.Max(W, H));
            W = Math.Max(2, (int)(W * k) / 2 * 2); H = Math.Max(2, (int)(H * k) / 2 * 2);

            var outp = AutoOut(input, "Cu poze", "cu_poze", ".mp4");
            var j = new Job(Tool.Ffmpeg, "Poze pe sunet: " + Path.GetFileName(input)) { DurationSeconds = dur };
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input,
                "-f", "lavfi", "-i", "color=c=black:s=" + W + "x" + H + ":r=30:d=" + Sec(dur));
            double[] odur; bool[] fin, fout;
            OverlayPlan(out odur, out fin, out fout);
            for (int i = 0; i < overlays.Count; i++) j.A("-loop", "1", "-framerate", "30", "-t", Sec(odur[i]), "-i", overlays[i].Image);
            var f = OverlayFilter(W, H, 2, "1:v", odur, fin, fout);
            j.A("-filter_complex", f.ToString(), "-map", "[vout]", "-map", "0:a:0",
                "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-r", "30",
                "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", outp);
            return j;
        }
    }
}
