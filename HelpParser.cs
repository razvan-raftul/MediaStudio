using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MediaStudio
{
    /// <summary>O opțiune citită direct din ajutorul programului.</summary>
    class OptionInfo
    {
        public Tool Tool;
        public string Section = "";
        public string Names = "";          // cum apare în ajutor, de ex. "-f, --format"
        public string Key = "";            // numele folosit în comandă
        public string Arg;                 // null dacă opțiunea nu primește valoare
        public string Type = "";           // la FFmpeg: <int>, <string>, <boolean>...
        public string Description = "";
        public List<string> Values = new List<string>();   // valori posibile (constante), „nume — descriere”
        public string FilterKind;          // "v" sau "a" pentru filtre FFmpeg
        public bool NeedsValue { get { return FilterKind != null || !string.IsNullOrEmpty(Arg) || (!string.IsNullOrEmpty(Type) && Type != "<flags>" ); } }
        public string Display
        {
            get
            {
                var d = Description.Length > 160 ? Description.Substring(0, 160) + "…" : Description;
                return (FilterKind != null ? "Filtru " + Key : Key) + (string.IsNullOrEmpty(Arg) ? "" : " " + Arg) + " — " + d;
            }
        }
        public override string ToString() { return Display; }
    }

    static class HelpParser
    {
        static readonly Regex YtOpt = new Regex(@"^\s{2,6}(-\S.*)$");

        /// <summary>Citește „yt-dlp --help”: secțiuni, opțiuni, argumente, descrieri.</summary>
        public static List<OptionInfo> ParseYtdlp(string text)
        {
            var list = new List<OptionInfo>();
            if (string.IsNullOrEmpty(text)) return list;
            string section = "General";
            OptionInfo last = null;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                int indent = raw.Length - raw.TrimStart().Length;
                var t = raw.Trim();
                if (indent <= 4 && t.EndsWith(":") && !t.StartsWith("-")) { section = t.TrimEnd(':'); last = null; continue; }
                if (t.StartsWith("-") && indent <= 8)
                {
                    var m = Regex.Match(t, @"^(.*?)(\s{2,}(.*))?$");
                    string sig = m.Groups[1].Value.Trim(), desc = m.Groups[3].Value.Trim();
                    var o = new OptionInfo { Tool = Tool.Ytdlp, Section = Translate(section), Names = sig, Description = desc };
                    // semnătura: "-f, --format FORMAT" sau "--no-playlist"
                    var parts = sig.Split(new[] { ", " }, StringSplitOptions.None);
                    string lastPart = parts[parts.Length - 1];
                    int sp = lastPart.IndexOf(' ');
                    string lastName = sp > 0 ? lastPart.Substring(0, sp) : lastPart;
                    o.Arg = sp > 0 ? lastPart.Substring(sp + 1).Trim() : null;
                    o.Key = parts.Select(p => p.Split(' ')[0]).FirstOrDefault(p => p.StartsWith("--")) ?? lastName;
                    list.Add(o); last = o;
                    continue;
                }
                if (last != null && indent >= 10) last.Description = (last.Description + " " + t).Trim();
            }
            return list.Where(o => o.Key != "--help" && o.Key != "--version" && o.Key != "--update" && !o.Key.StartsWith("--update")).ToList();
        }

        /// <summary>Citește „ffmpeg -h full”: opțiunile principale și opțiunile fiecărui codec, format și filtru.</summary>
        public static List<OptionInfo> ParseFfmpeg(string text)
        {
            var list = new List<OptionInfo>();
            if (string.IsNullOrEmpty(text)) return list;
            string section = "Opțiuni principale";
            OptionInfo last = null;
            var avopt = new Regex(@"^-(\S+)\s+(<\w+>)\s+([A-Z\.]{8,12})\s*(.*)$");
            var constLine = new Regex(@"^(\S+)\s+(?:(-?\d+)\s+)?([A-Z\.]{8,12})\s*(.*)$");
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                int indent = raw.Length - raw.TrimStart().Length;
                var t = raw.Trim();
                if (indent == 0 && t.EndsWith(":") && !t.StartsWith("-")) { section = Translate(t.TrimEnd(':')); last = null; continue; }
                if (t.StartsWith("-") && indent <= 2)
                {
                    var m = avopt.Match(t);
                    OptionInfo o;
                    if (m.Success)
                    {
                        o = new OptionInfo { Tool = Tool.Ffmpeg, Section = section, Key = "-" + m.Groups[1].Value, Names = "-" + m.Groups[1].Value, Type = m.Groups[2].Value, Description = m.Groups[4].Value.Trim() };
                        if (o.Type == "<boolean>") o.Arg = "0|1"; else if (o.Type != "<flags>") o.Arg = o.Type;
                        else o.Arg = "<flags>";
                    }
                    else
                    {
                        var g = Regex.Match(t, @"^(-\S+)(?:\s(\S+))?\s{2,}(.*)$");
                        if (!g.Success) g = Regex.Match(t, @"^(-\S+)()()$");
                        o = new OptionInfo { Tool = Tool.Ffmpeg, Section = section, Key = g.Groups[1].Value, Names = g.Groups[1].Value, Arg = g.Groups[2].Value.Length > 0 ? g.Groups[2].Value : null, Description = g.Groups[3].Value.Trim() };
                    }
                    list.Add(o); last = o;
                    continue;
                }
                if (last != null && indent >= 3)
                {
                    var c = constLine.Match(t);
                    if (c.Success && !string.IsNullOrEmpty(last.Type)) last.Values.Add(c.Groups[1].Value + (c.Groups[4].Value.Trim().Length > 0 ? " — " + c.Groups[4].Value.Trim() : ""));
                }
            }
            return list;
        }

        /// <summary>Citește „ffmpeg -filters”.</summary>
        public static List<OptionInfo> ParseFilters(string text)
        {
            var list = new List<OptionInfo>();
            if (string.IsNullOrEmpty(text)) return list;
            var re = new Regex(@"^\s*[T\.][S\.][C\.]?\s+(\S+)\s+(\S+->\S+)\s+(.*)$");
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var m = re.Match(raw);
                if (!m.Success) continue;
                var io = m.Groups[2].Value;
                string kind = io.EndsWith("V") || io.StartsWith("V") ? "v" : io.EndsWith("A") || io.StartsWith("A") ? "a" : null;
                if (kind == null || io.Contains("N")) continue;  // filtrele cu număr variabil de intrări nu se pot folosi într-un lanț simplu
                if (io != "V->V" && io != "A->A") continue;
                list.Add(new OptionInfo { Tool = Tool.Ffmpeg, Section = kind == "v" ? "Filtre video" : "Filtre audio", Key = m.Groups[1].Value, Names = m.Groups[1].Value, Description = m.Groups[3].Value.Trim(), FilterKind = kind, Arg = "parametri" });
            }
            return list;
        }

        /// <summary>Lista de codificatoare: tip V, A sau S și nume.</summary>
        public static List<Tuple<char, string, string>> ParseEncoders(string text)
        {
            var res = new List<Tuple<char, string, string>>();
            if (string.IsNullOrEmpty(text)) return res;
            var re = new Regex(@"^\s([VAS])[F\.][S\.][X\.][B\.][D\.]\s+(\S+)\s+(.*)$");
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var m = re.Match(raw);
                if (m.Success && m.Groups[2].Value != "=") res.Add(Tuple.Create(m.Groups[1].Value[0], m.Groups[2].Value, m.Groups[3].Value.Trim()));
            }
            return res;
        }

        static string Translate(string s)
        {
            switch (s)
            {
                case "General Options": return "Generale";
                case "Network Options": return "Rețea";
                case "Geo-restriction": return "Restricții geografice";
                case "Video Selection": return "Selecția videoclipurilor";
                case "Download Options": return "Descărcare";
                case "Filesystem Options": return "Fișiere și foldere";
                case "Thumbnail Options": return "Miniaturi";
                case "Internet Shortcut Options": return "Scurtături internet";
                case "Verbosity and Simulation Options": return "Mesaje și simulare";
                case "Workarounds": return "Soluții pentru probleme";
                case "Video Format Options": return "Formate video";
                case "Subtitle Options": return "Subtitrări";
                case "Authentication Options": return "Autentificare";
                case "Post-Processing Options": return "Procesare după descărcare";
                case "SponsorBlock Options": return "SponsorBlock";
                case "Extractor Options": return "Extractoare";
                case "Preset Aliases": return "Presetări yt-dlp";
                case "Getting help": return "Informații";
                case "Print help / information / capabilities": return "Informații";
                case "Global options (affect whole program instead of just one file)": return "Opțiuni globale";
                case "Advanced global options": return "Opțiuni globale avansate";
                case "Per-file main options": return "Opțiuni principale pentru fișier";
                case "Advanced per-file options": return "Opțiuni avansate pentru fișier";
                case "Video options": return "Video";
                case "Advanced Video options": return "Video avansat";
                case "Audio options": return "Audio";
                case "Advanced Audio options": return "Audio avansat";
                case "Subtitle options": return "Subtitrări";
                case "Main options": return "Opțiuni principale";
                case "Advanced options": return "Opțiuni avansate";
                default: return s;
            }
        }
    }
}
