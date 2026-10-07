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
        TextBox cvOutFolder, cvSuffix, cvWidth, cvHeight, cvVBitrate, cvStart, cvDuration;
        ComboBox cvFormat, cvVCodec, cvQualityMode, cvPreset, cvRes, cvFps, cvACodec, cvABitrate, cvRate, cvChannels, cvHw;
        NumericUpDown cvCrf, cvVolume, cvSpeed;
        CheckBox cvNoVideo, cvNoAudio, cvLoudnorm, cvDeinterlace, cvSameFolder;

        static readonly string[] VideoFormats = { "mp4", "mkv", "webm", "mov", "avi", "wmv", "flv", "ts", "m4v", "gif" };
        static readonly string[] AudioFormats = { "mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "wma", "ac3", "aiff", "mka" };

        void BuildConvertTab(TabPage page)
        {
            var b = new Builder(page, cvFields);
            cvFiles = b.List("files", "&Fișiere de convertit");
            b.Buttons(
                Builder.B("&Adaugă fișiere…", (s, e) => { foreach (var f in Dialogs.PickFiles("Alege fișierele de convertit", Dialogs.MediaFilter, this)) if (!cvFiles.Items.Contains(f)) cvFiles.Items.Add(f); Announce(cvFiles.Items.Count + " fișiere în listă."); }),
                Builder.B("Adaugă un folder…", (s, e) => AddFolder(cvFiles)),
                Builder.B("Elimină fișierul selectat", (s, e) => RemoveSelected(cvFiles)),
                Builder.B("Golește lista", (s, e) => { cvFiles.Items.Clear(); Announce("Lista e goală."); }));
            cvFormat = b.Combo("format", "Format de &ieșire", VideoFormats.Concat(AudioFormats).Select(f => f + (AudioFormats.Contains(f) ? " (audio)" : " (video)")));
            cvSameFolder = b.Check("sameFolder", "Salvează lângă fișierul original", true);
            cvOutFolder = b.Path("outFolder", "Folder de ieși&re", PathKind.Folder);
            cvSuffix = b.Text("suffix", "Adaugă la nume", "", false, "Text adăugat la sfârșitul numelui fișierului nou. Gol înseamnă același nume.");

            var v = b.Section("Video", true, true);
            cvVCodec = v.Combo("vcodec", "&Codec video", new[] { "Automat", "copy — fără reconversie", "libx264 — H.264", "libx265 — H.265 HEVC", "libvpx-vp9 — VP9", "libaom-av1 — AV1", "libsvtav1 — AV1 rapid", "mpeg4", "prores_ks — ProRes", "gif" }, 0, true,
                "Poți scrie orice codificator FFmpeg. Lista completă e în secțiunea Toate opțiunile.");
            cvQualityMode = v.Combo("qmode", "Mod calitate", new[] { "Calitate constantă (CRF)", "Bitrate fix" });
            cvCrf = v.Number("crf", "Valoare CRF", 0, 63, 23, 1, 0, "Mai mic înseamnă calitate mai bună și fișier mai mare. 18-28 e obișnuit.");
            cvVBitrate = v.Text("vbitrate", "Bitrate video (kbps)", "2500");
            cvPreset = v.Combo("preset", "Viteză codare", new[] { "Implicit", "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" });
            cvRes = v.Combo("res", "Re&zoluție", new[] { "Originală", "2160p (3840 lățime)", "1440p", "1080p", "720p", "576p", "480p", "360p", "240p", "Personalizată" });
            cvWidth = v.Text("width", "Lățime personalizată (pixeli)", "");
            cvHeight = v.Text("height", "Înălțime personalizată (pixeli)", "");
            cvFps = v.Combo("fps", "Cadre pe secundă", new[] { "Originale", "60", "50", "30", "29.97", "25", "24", "23.976", "15", "10" }, 0, true);
            cvDeinterlace = v.Check("deint", "Deîntrețesere (pentru înregistrări TV)");
            cvNoVideo = v.Check("novideo", "Elimină pista video");

            var a = b.Section("Audio", true, true);
            cvACodec = a.Combo("acodec", "Codec a&udio", new[] { "Automat", "copy — fără reconversie", "aac", "libmp3lame — MP3", "libopus — Opus", "libvorbis — Vorbis", "flac", "pcm_s16le — WAV 16 biți", "pcm_s24le — WAV 24 biți", "ac3", "alac" }, 0, true);
            cvABitrate = a.Combo("abitrate", "Bitrate audio", new[] { "Automat", "320k", "256k", "192k", "160k", "128k", "96k", "64k", "32k" }, 0, true);
            cvRate = a.Combo("rate", "Frecvență de eșantionare", new[] { "Originală", "8000", "11025", "16000", "22050", "32000", "44100", "48000", "88200", "96000" });
            cvChannels = a.Combo("channels", "Canale", new[] { "Originale", "Mono (1)", "Stereo (2)", "5.1 (6)" });
            cvVolume = a.Number("volume", "Volum (%)", 0, 1000, 100, 10, 0, "100 înseamnă neschimbat.");
            cvLoudnorm = a.Check("loudnorm", "Normalizează volumul (EBU R128)");
            cvNoAudio = a.Check("noaudio", "Elimină pista audio");

            var t = b.Section("Timp și viteză", true);
            cvStart = t.Text("start", "Începe de la (hh:mm:ss)", "");
            cvDuration = t.Text("duration", "Durată (hh:mm:ss)", "", false, "Gol înseamnă până la sfârșit.");
            cvSpeed = t.Number("speed", "Viteză de redare (%)", 25, 400, 100, 5, 0, "100 înseamnă neschimbat. Se aplică la video și audio.");
            cvHw = t.Combo("hw", "Accelerare hardware la decodare", new[] { "Nu", "auto", "cuda", "qsv", "d3d11va", "dxva2" });

            b.Buttons(
                Builder.B("Con&vertește", (s, e) => Run(cvFiles.Items.Cast<string>().Select(f => ConvertJob(f)).ToList())));

            Action sync = () =>
            {
                cvOutFolder.Parent.Enabled = !cvSameFolder.Checked;
                bool crf = cvQualityMode.SelectedIndex == 0;
                cvCrf.Enabled = crf; cvVBitrate.Enabled = !crf;
                bool custom = cvRes.Text == "Personalizată";
                cvWidth.Enabled = custom; cvHeight.Enabled = custom;
            };
            cvSameFolder.CheckedChanged += (s, e) => sync();
            cvQualityMode.SelectedIndexChanged += (s, e) => sync();
            cvRes.SelectedIndexChanged += (s, e) => sync();
            sync();
        }

        void AddFolder(ListBox list)
        {
            var dir = Dialogs.PickFolder("Alege folderul cu fișiere media", null, this);
            if (dir == null) return;
            var exts = new HashSet<string>(VideoFormats.Concat(AudioFormats).Concat(new[] { "mpg", "mpeg", "3gp", "m2ts", "vob" }).Select(x => "." + x), StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var f in Directory.GetFiles(dir).Where(f => exts.Contains(Path.GetExtension(f))).OrderBy(f => f)) if (!list.Items.Contains(f)) { list.Items.Add(f); n++; }
            Announce("Am adăugat " + n + " fișiere. În listă sunt " + list.Items.Count + ".");
        }

        void RemoveSelected(ListBox list)
        {
            int i = list.SelectedIndex;
            if (i < 0) { Announce("Niciun fișier selectat."); return; }
            list.Items.RemoveAt(i);
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(i, list.Items.Count - 1);
            Announce("Eliminat. Rămân " + list.Items.Count + ".");
        }

        static string Code(ComboBox c) { var w = FirstWord(c.Text); return w; }
        static bool IsAuto(ComboBox c) { var t = c.Text.Trim(); return t.Length == 0 || t.StartsWith("Automat") || t.StartsWith("Original") || t == "Implicit" || t == "Nu"; }

        Job ConvertJob(string input)
        {
            var fmt = FirstWord(cvFormat.Text);
            bool audioOnly = AudioFormats.Contains(fmt);
            var outp = OutPath(input, cvSameFolder.Checked ? null : cvOutFolder.Text.Trim(), cvSuffix.Text, fmt);
            var j = new Job(Tool.Ffmpeg, "Conversie: " + Path.GetFileName(input) + " în " + fmt) { ProbeForDuration = input };
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats");
            if (!IsAuto(cvHw)) j.A("-hwaccel", cvHw.Text);
            if (cvStart.Text.Trim().Length > 0) j.A("-ss", cvStart.Text.Trim());
            j.A("-i", input);
            if (cvDuration.Text.Trim().Length > 0) j.A("-t", cvDuration.Text.Trim());

            var vf = new List<string>();
            var af = new List<string>();
            double speed = (double)cvSpeed.Value / 100.0;

            if (audioOnly || cvNoVideo.Checked) j.A("-vn");
            else
            {
                if (!IsAuto(cvVCodec)) j.A("-c:v", Code(cvVCodec));
                bool copyV = Code(cvVCodec) == "copy";
                if (!copyV)
                {
                    var vc = Code(cvVCodec);
                    if (cvQualityMode.SelectedIndex == 0)
                    {
                        if (fmt != "gif") { j.A("-crf", ((int)cvCrf.Value).ToString()); if (vc == "libvpx-vp9" || vc == "libaom-av1") j.A("-b:v", "0"); }
                    }
                    else if (cvVBitrate.Text.Trim().Length > 0) j.A("-b:v", cvVBitrate.Text.Trim().TrimEnd('k', 'K') + "k");
                    if (!IsAuto(cvPreset)) j.A("-preset", cvPreset.Text);
                    if (cvDeinterlace.Checked) vf.Add("yadif");
                    var h = Regex.Match(cvRes.Text, @"^(\d+)p").Groups[1].Value;
                    if (h.Length > 0) vf.Add("scale=-2:" + h);
                    else if (cvRes.Text == "Personalizată")
                    {
                        var w = cvWidth.Text.Trim(); var hh = cvHeight.Text.Trim();
                        if (w.Length > 0 || hh.Length > 0) vf.Add("scale=" + (w.Length > 0 ? w : "-2") + ":" + (hh.Length > 0 ? hh : "-2"));
                    }
                    if (!IsAuto(cvFps)) j.A("-r", cvFps.Text.Trim());
                    if (Math.Abs(speed - 1) > 0.001) vf.Add("setpts=PTS/" + speed.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (fmt == "gif") { vf.Add("split[a][b];[a]palettegen[p];[b][p]paletteuse"); }
                    if (fmt == "mp4" || fmt == "m4v" || fmt == "mov") j.A("-pix_fmt", "yuv420p");
                }
            }

            if (cvNoAudio.Checked || fmt == "gif") j.A("-an");
            else
            {
                if (!IsAuto(cvACodec)) j.A("-c:a", Code(cvACodec));
                bool copyA = Code(cvACodec) == "copy";
                if (!copyA)
                {
                    if (!IsAuto(cvABitrate)) j.A("-b:a", cvABitrate.Text.Trim());
                    if (!IsAuto(cvRate)) j.A("-ar", cvRate.Text.Trim());
                    var ch = Regex.Match(cvChannels.Text, @"\((\d)\)").Groups[1].Value;
                    if (ch.Length > 0) j.A("-ac", ch);
                    if (cvVolume.Value != 100) af.Add("volume=" + ((double)cvVolume.Value / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (cvLoudnorm.Checked) af.Add("loudnorm");
                    if (Math.Abs(speed - 1) > 0.001) af.Add(Atempo(speed));
                }
                if (fmt == "mp3" && IsAuto(cvACodec)) j.A("-c:a", "libmp3lame");
            }
            AddExtraFilters(vf, af);
            if (vf.Count > 0) { if (vf.Any(x => x.Contains("["))) j.A("-filter_complex", string.Join(",", vf)); else j.A("-vf", string.Join(",", vf)); }
            if (af.Count > 0) j.A("-af", string.Join(",", af));
            if (fmt == "mp4" || fmt == "mov" || fmt == "m4a" || fmt == "m4v") j.A("-movflags", "+faststart");
            foreach (var x in ExtraArgs(Tool.Ffmpeg)) j.Args.Add(x);
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
