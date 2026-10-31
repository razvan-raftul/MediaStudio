using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MediaStudio
{
    /// <summary>Setările aplicației, salvate ca text simplu cheie=valoare în %APPDATA%\MediaStudio\settings.ini.</summary>
    static class Settings
    {
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaStudio"); } }
        public static string PresetDir { get { return Path.Combine(Dir, "presetari"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }
        static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string FfmpegPath { get { return Get("ffmpeg"); } set { Set("ffmpeg", value); } }
        public static string FfprobePath { get { return Get("ffprobe"); } set { Set("ffprobe", value); } }
        public static string YtdlpPath { get { return Get("ytdlp"); } set { Set("ytdlp", value); } }
        public static string DownloadFolder
        {
            // folderul implicit pentru tot ce salvează aplicația: Documente\Media Studio
            get { var v = Get("mediaFolder"); return string.IsNullOrEmpty(v) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Media Studio") : v; }
            set { Set("mediaFolder", value); }
        }
        // progresul se anunță mereu, din 10 în 10; dacă rezultatul există deja, se întreabă înainte (MainForm.AskOverwrite)
        public static bool AnnounceProgress { get { return true; } }
        public static int AnnounceStep { get { return 10; } }
        public static bool ErrorDialogs { get { return Get("errorDialogs", "1") == "1"; } set { Set("errorDialogs", value ? "1" : "0"); } }
        public static bool DoneSound { get { return Get("doneSound", "1") == "1"; } set { Set("doneSound", value ? "1" : "0"); } }
        public static bool Overwrite { get { return false; } }

        /// <summary>Folderele pe care le face Media Studio în folderul principal, câte unul pentru fiecare fel de operațiune.</summary>
        public static readonly string[] SubFolders =
        {
            "Descărcări", "Convertite", "Volum", "Separate", "Tăiate", "Fade", "Lipite", "Împărțite",
            "Cu poze", "Piste și subtitrări", "Sunet peste videoclip", "Metadate"
        };

        /// <summary>Face de la început folderul principal și toate subfolderele (cele care există deja rămân cum sunt).</summary>
        public static void EnsureFolders()
        {
            try
            {
                Directory.CreateDirectory(DownloadFolder);
                foreach (var f in SubFolders) Directory.CreateDirectory(Path.Combine(DownloadFolder, f));
            }
            catch { }
        }
        public static int LastTab { get { int n; return int.TryParse(Get("lastTab", "0"), out n) ? n : 0; } set { Set("lastTab", value.ToString()); } }

        public static string Get(string key, string def = "")
        {
            string v; return values.TryGetValue(key, out v) ? v : def;
        }
        public static void Set(string key, string value) { values[key] = value ?? ""; }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var kv in Ini.Read(File.ReadAllText(FilePath, Encoding.UTF8))) values[kv.Key] = kv.Value;
            }
            catch { }
        }
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, Ini.Write(values), Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>Format simplu cheie=valoare; valorile pe mai multe rânduri sunt codate cu \n.</summary>
    static class Ini
    {
        public static Dictionary<string, string> Read(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                int i = line.IndexOf('=');
                if (i <= 0 || line.StartsWith("#")) continue;
                d[line.Substring(0, i)] = Unescape(line.Substring(i + 1));
            }
            return d;
        }
        public static string Write(IDictionary<string, string> d)
        {
            var sb = new StringBuilder();
            foreach (var kv in d) sb.Append(kv.Key).Append('=').Append(Escape(kv.Value)).Append("\r\n");
            return sb.ToString();
        }
        static string Escape(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }
        static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length) { i++; sb.Append(s[i] == 'n' ? '\n' : s[i]); }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
