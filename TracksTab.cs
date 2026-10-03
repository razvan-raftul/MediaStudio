using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MediaStudio
{
    class StreamInfo
    {
        public int Index;            // indexul global în fișier
        public int TypeIndex;        // al câtelea de acel tip (0, 1…)
        public string Type = "";     // video, audio, subtitle, data, attachment
        public string Codec = "";
        public string Text = "";
        public override string ToString() { return Text; }
    }

    partial class MainForm
    {
        TextBox tkIn, tkOther, tkLang, tkSubFile, tkOut;
        ListBox tkStreams;
        ComboBox tkAction, tkSubAction, tkSubStyle;
        NumericUpDown tkSubSize;

        void BuildTracksTab(TabPage page)
        {
            var b = new Builder(page);
            tkIn = b.Path(null, "&Fișier", PathKind.OpenFile, Dialogs.MediaFilter);
            b.Buttons(Builder.B("&Analizează pistele", (s, e) => AnalyzeStreams()));
            tkStreams = b.List(null, "&Piste găsite", 120);

            var p = b.Section("Operațiuni cu piste", false);
            tkAction = p.Combo(null, "&Operațiune", new[] {
                "Extrage pista selectată într-un fișier separat",
                "Elimină pista selectată",
                "Păstrează doar pista video și pista audio selectată",
                "Înlocuiește tot sunetul cu un alt fișier audio",
                "Adaugă un fișier audio ca pistă în plus",
                "Fă pista selectată implicită",
                "Setează limba pistei selectate" });
            tkOther = p.Path(null, "Fișier audio (pentru înlocuire sau adăugare)", PathKind.OpenFile, Dialogs.AudioFilter);
            tkLang = p.Text(null, "Cod limbă (de ex. ron, eng)", "ron", false, "Cod de limbă din trei litere.");
            p.Buttons(Builder.B("&Execută operațiunea", (s, e) => Run(TrackJob())));

            var su = b.Section("Subtitrări", false);
            tkSubAction = su.Combo(null, "Operațiune s&ubtitrare", new[] {
                "Adaugă subtitrarea ca pistă (se poate activa sau dezactiva)",
                "Arde subtitrarea în imagine (apare mereu)",
                "Extrage pista de subtitrare selectată",
                "Convertește un fișier de subtitrare (de ex. ass în srt)" });
            tkSubFile = su.Path(null, "Fișier de subtitrare", PathKind.OpenFile, Dialogs.SubFilter);
            tkSubSize = su.Number(null, "Mărimea literelor la ardere", 8, 96, 24);
            tkSubStyle = su.Combo(null, "Stil la ardere", new[] { "Alb cu contur negru", "Galben cu contur negru", "Alb pe fundal negru" });
            su.Buttons(Builder.B("Execută operațiunea cu subtitrarea", (s, e) => Run(SubtitleJob())));

            tkOut = b.Path(null, "Fișier rezultat (gol = lângă original)", PathKind.SaveFile, Dialogs.MediaFilter);
        }

        List<StreamInfo> Probe(string file)
        {
            var res = new List<StreamInfo>();
            var o = Runner.Capture(Tool.Ffprobe, "-v error -show_entries stream=index,codec_type,codec_name,channels,channel_layout,sample_rate,width,height,r_frame_rate,bit_rate:stream_tags=language,title:stream_disposition=default -of compact=p=0:nk=0 " + Runner.Quote(file));
            if (o == null) return res;
            var counters = new Dictionary<string, int>();
            foreach (var line in o.Replace("\r", "").Split('\n'))
            {
                if (!line.Contains("index=")) continue;
                var d = line.Split('|').Select(kv => kv.Split(new[] { '=' }, 2)).Where(a => a.Length == 2).GroupBy(a => a[0]).ToDictionary(g => g.Key, g => g.First()[1]);
                Func<string, string> g2 = k => { string v; return d.TryGetValue(k, out v) ? v : ""; };
                var si = new StreamInfo { Index = int.Parse(g2("index")), Type = g2("codec_type"), Codec = g2("codec_name") };
                int n; counters.TryGetValue(si.Type, out n); si.TypeIndex = n; counters[si.Type] = n + 1;
                string typeRo = si.Type == "video" ? "video" : si.Type == "audio" ? "audio" : si.Type == "subtitle" ? "subtitrare" : si.Type;
                var parts = new List<string> { "Pista " + si.Index + ": " + typeRo + " " + (si.TypeIndex + 1), si.Codec };
                if (si.Type == "video" && g2("width").Length > 0) parts.Add(g2("width") + "x" + g2("height"));
                if (si.Type == "video" && g2("r_frame_rate").Contains("/")) { var f = g2("r_frame_rate").Split('/'); double a, bb; if (double.TryParse(f[0], out a) && double.TryParse(f[1], out bb) && bb > 0) parts.Add(Math.Round(a / bb, 2) + " cadre/s"); }
                if (si.Type == "audio") { parts.Add(g2("channel_layout").Length > 0 ? g2("channel_layout") : g2("channels") + " canale"); if (g2("sample_rate").Length > 0) parts.Add(g2("sample_rate") + " Hz"); }
                if (g2("tag:language").Length > 0) parts.Add("limba " + g2("tag:language"));
                if (g2("tag:title").Length > 0) parts.Add("„" + g2("tag:title") + "”");
                if (g2("disposition:default") == "1") parts.Add("implicită");
                si.Text = string.Join(", ", parts.Where(x => x.Length > 0));
                res.Add(si);
            }
            return res;
        }

        void AnalyzeStreams()
        {
            if (!Need(tkIn.Text, "un fișier")) return;
            if (!File.Exists(Settings.FfprobePath)) { ShowError(Tools.MissingText(Tool.Ffprobe)); return; }
            Announce("Analizez…");
            var list = Probe(tkIn.Text.Trim());
            tkStreams.Items.Clear();
            foreach (var s in list) tkStreams.Items.Add(s);
            var dur = Runner.ProbeDuration(tkIn.Text.Trim());
            if (list.Count > 0) { tkStreams.SelectedIndex = 0; Announce(list.Count + " piste. Durată " + TimeSpan.FromSeconds(Math.Round(dur)).ToString() + ". Alege o pistă din lista Piste găsite."); }
            else Announce("Nu am găsit piste. Poate fișierul nu este un fișier media.");
        }

        StreamInfo SelStream() { return tkStreams.SelectedItem as StreamInfo; }

        static string ExtFor(StreamInfo s)
        {
            switch (s.Codec)
            {
                case "aac": return "m4a";
                case "mp3": return "mp3";
                case "opus": return "opus";
                case "vorbis": return "ogg";
                case "flac": return "flac";
                case "ac3": return "ac3";
                case "eac3": return "eac3";
                case "dts": return "dts";
                case "subrip": return "srt";
                case "ass": case "ssa": return "ass";
                case "webvtt": return "vtt";
                case "mov_text": return "srt";
                case "h264": case "hevc": return "mkv";
                default: return s.Type == "audio" ? "mka" : s.Type == "subtitle" ? "mks" : "mkv";
            }
        }

        string TkOut(string input, string suffix, string ext)
        {
            return tkOut.Text.Trim().Length > 0 ? tkOut.Text.Trim() : OutPath(input, null, suffix, ext ?? Path.GetExtension(input));
        }

        Job TrackJob()
        {
            if (!Need(tkIn.Text, "un fișier")) return null;
            var input = tkIn.Text.Trim();
            var s = SelStream();
            int act = tkAction.SelectedIndex;
            if ((act <= 2 || act >= 5) && s == null) { Announce("Apasă Analizează pistele și alege o pistă din listă."); return null; }
            var j = new Job(Tool.Ffmpeg, tkAction.Text + ": " + Path.GetFileName(input)) { ProbeForDuration = input };
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input);
            switch (act)
            {
                case 0:
                    j.A("-map", "0:" + s.Index, "-c", "copy");
                    if (s.Codec == "mov_text") { j.Args.RemoveAt(j.Args.Count - 1); j.A("srt"); j.Args[j.Args.Count - 2] = "-c"; }
                    j.A(TkOut(input, " (pista " + s.Index + ")", ExtFor(s)));
                    break;
                case 1:
                    j.A("-map", "0", "-map", "-0:" + s.Index, "-c", "copy", TkOut(input, " (fără pista " + s.Index + ")", null));
                    break;
                case 2:
                    if (s.Type != "audio") { Announce("Alege o pistă audio."); return null; }
                    j.A("-map", "0:v?", "-map", "0:" + s.Index, "-c", "copy", TkOut(input, " (o singură pistă audio)", null));
                    break;
                case 3:
                case 4:
                    if (!Need(tkOther.Text, "fișierul audio")) return null;
                    j.A("-i", tkOther.Text.Trim());
                    if (act == 3) j.A("-map", "0:v?", "-map", "0:s?", "-map", "1:a", "-c", "copy", "-shortest");
                    else j.A("-map", "0", "-map", "1:a", "-c", "copy");
                    if (Path.GetExtension(input).Equals(".mp4", StringComparison.OrdinalIgnoreCase)) { j.Args[j.Args.IndexOf("copy")] = "copy"; j.A("-c:a", "aac"); }
                    j.A(TkOut(input, act == 3 ? " (sunet înlocuit)" : " (pistă adăugată)", null));
                    break;
                case 5:
                    j.A("-map", "0", "-c", "copy");
                    string tp = s.Type == "audio" ? "a" : s.Type == "subtitle" ? "s" : "v";
                    j.A("-disposition:" + tp, "0", "-disposition:" + tp + ":" + s.TypeIndex, "default");
                    j.A(TkOut(input, " (pistă implicită)", null));
                    break;
                case 6:
                    string tp2 = s.Type == "audio" ? "a" : s.Type == "subtitle" ? "s" : "v";
                    j.A("-map", "0", "-c", "copy", "-metadata:s:" + tp2 + ":" + s.TypeIndex, "language=" + tkLang.Text.Trim());
                    j.A(TkOut(input, " (limbă setată)", null));
                    break;
            }
            return j;
        }

        static string FilterPath(string p)
        {
            // calea în sintaxa filtrelor FFmpeg: \ devine /, : și ' se escapează
            return p.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
        }

        Job SubtitleJob()
        {
            int act = tkSubAction.SelectedIndex;
            var input = tkIn.Text.Trim();
            if (act == 3)
            {
                if (!Need(tkSubFile.Text, "fișierul de subtitrare")) return null;
                var sf = tkSubFile.Text.Trim();
                var outSub = tkOut.Text.Trim().Length > 0 ? tkOut.Text.Trim() : OutPath(sf, null, "", "srt");
                return new Job(Tool.Ffmpeg, "Conversie subtitrare: " + Path.GetFileName(sf)).A("-hide_banner", "-y", "-i", sf, outSub);
            }
            if (!Need(input, "fișierul video din câmpul Fișier")) return null;
            var j = new Job(Tool.Ffmpeg, tkSubAction.Text + ": " + Path.GetFileName(input)) { ProbeForDuration = input };
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input);
            var ext = Path.GetExtension(input).ToLowerInvariant();
            switch (act)
            {
                case 0:
                    if (!Need(tkSubFile.Text, "fișierul de subtitrare")) return null;
                    j.A("-i", tkSubFile.Text.Trim(), "-map", "0", "-map", "1", "-c", "copy", "-c:s", ext == ".mp4" || ext == ".mov" || ext == ".m4v" ? "mov_text" : ext == ".webm" ? "webvtt" : "srt");
                    if (tkLang.Text.Trim().Length > 0) j.A("-metadata:s:s:" + CountSubs(input), "language=" + tkLang.Text.Trim());
                    j.A(TkOut(input, " (cu subtitrare)", null));
                    break;
                case 1:
                    string style;
                    switch (tkSubStyle.SelectedIndex)
                    {
                        case 1: style = "PrimaryColour=&H0000FFFF,OutlineColour=&H00000000,BorderStyle=1,Outline=2"; break;
                        case 2: style = "PrimaryColour=&H00FFFFFF,BackColour=&H80000000,BorderStyle=3,Outline=1"; break;
                        default: style = "PrimaryColour=&H00FFFFFF,OutlineColour=&H00000000,BorderStyle=1,Outline=2"; break;
                    }
                    style += ",Fontsize=" + (int)tkSubSize.Value;
                    string src;
                    if (tkSubFile.Text.Trim().Length > 0 && File.Exists(tkSubFile.Text.Trim())) src = "subtitles='" + FilterPath(tkSubFile.Text.Trim()) + "'";
                    else
                    {
                        var s = SelStream();
                        if (s == null || s.Type != "subtitle") { Announce("Alege un fișier de subtitrare sau o pistă de subtitrare din listă."); return null; }
                        src = "subtitles='" + FilterPath(input) + "':si=" + s.TypeIndex;
                    }
                    j.A("-vf", src + ":force_style='" + style + "'", "-c:a", "copy", TkOut(input, " (subtitrare arsă)", null));
                    break;
                case 2:
                    var st = SelStream();
                    if (st == null || st.Type != "subtitle") { Announce("Analizează pistele și alege o pistă de subtitrare."); return null; }
                    j.A("-map", "0:" + st.Index, TkOut(input, " (subtitrare " + (st.TypeIndex + 1) + ")", st.Codec == "ass" || st.Codec == "ssa" ? "ass" : "srt"));
                    break;
            }
            return j;
        }

        int CountSubs(string file)
        {
            try { return Probe(file).Count(s => s.Type == "subtitle"); } catch { return 0; }
        }
    }
}
