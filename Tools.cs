using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MediaStudio
{
    /// <summary>Găsește FFmpeg, FFprobe și yt-dlp pe calculator și le poate descărca automat.</summary>
    static class Tools
    {
        public static string AppDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static string ToolDir { get { return Path.Combine(AppDir, "unelte"); } }

        public static string MissingText(Tool t)
        {
            string n = t == Tool.Ytdlp ? "yt-dlp" : t == Tool.Ffmpeg ? "FFmpeg" : "FFprobe";
            return n + " nu este configurat. Mergi în tabul Setări și apasă „Descarcă automat ce lipsește”, sau alege fișierul " +
                   (t == Tool.Ytdlp ? "yt-dlp.exe" : t == Tool.Ffmpeg ? "ffmpeg.exe" : "ffprobe.exe") + " cu butonul Răsfoiește.";
        }

        /// <summary>Completează căile lipsă căutând în folderul aplicației, în PATH și în locurile obișnuite.</summary>
        public static void Detect(bool force = false)
        {
            if (force || !File.Exists(Settings.YtdlpPath)) Settings.YtdlpPath = Find("yt-dlp.exe") ?? Find("yt-dlp_x86.exe") ?? Settings.YtdlpPath;
            if (force || !File.Exists(Settings.FfmpegPath)) Settings.FfmpegPath = Find("ffmpeg.exe") ?? Settings.FfmpegPath;
            if (force || !File.Exists(Settings.FfprobePath))
            {
                string probe = null;
                if (File.Exists(Settings.FfmpegPath)) { var p = Path.Combine(Path.GetDirectoryName(Settings.FfmpegPath), "ffprobe.exe"); if (File.Exists(p)) probe = p; }
                Settings.FfprobePath = probe ?? Find("ffprobe.exe") ?? Settings.FfprobePath;
            }
            Settings.Save();
        }

        static string Find(string exe)
        {
            var dirs = new[] { ToolDir, AppDir, Path.Combine(AppDir, "bin") }.ToList();
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            dirs.AddRange(path.Split(';').Where(s => s.Trim().Length > 0).Select(s => s.Trim().Trim('"')));
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dirs.Add(Path.Combine(local, "Microsoft", "WinGet", "Links"));
            dirs.Add(@"C:\ffmpeg\bin");
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims"));
            foreach (var d in dirs)
            {
                try { var f = Path.Combine(d, exe); if (File.Exists(f)) return f; } catch { }
            }
            // pachetele winget pentru FFmpeg stau într-un subfolder cu versiunea
            try
            {
                var pk = Path.Combine(local, "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(pk))
                    foreach (var d in Directory.GetDirectories(pk))
                    {
                        var hit = Directory.GetFiles(d, exe, SearchOption.AllDirectories).FirstOrDefault();
                        if (hit != null) return hit;
                    }
            }
            catch { }
            return null;
        }

        public static string Version(Tool t)
        {
            var o = Runner.Capture(t, t == Tool.Ytdlp ? "--version" : "-version", 15000);
            if (o == null) return null;
            var first = o.Split('\n')[0].Trim();
            return first;
        }

        // ---------------- descărcare automată ----------------
        public const string YtdlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        public const string FfmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        public static async Task DownloadYtdlp(Action<string, int> report)
        {
            Directory.CreateDirectory(ToolDir);
            var dest = Path.Combine(ToolDir, "yt-dlp.exe");
            await Download(YtdlpUrl, dest + ".part", "yt-dlp", report);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(dest + ".part", dest);
            Settings.YtdlpPath = dest;
            Settings.Save();
        }

        public static async Task DownloadFfmpeg(Action<string, int> report)
        {
            Directory.CreateDirectory(ToolDir);
            var zip = Path.Combine(ToolDir, "ffmpeg.zip");
            await Download(FfmpegUrl, zip, "FFmpeg", report);
            report("Dezarhivez FFmpeg…", -1);
            await Task.Run(() =>
            {
                using (var a = ZipFile.OpenRead(zip))
                {
                    foreach (var e in a.Entries)
                    {
                        var name = e.Name.ToLowerInvariant();
                        if (name == "ffmpeg.exe" || name == "ffprobe.exe" || name == "ffplay.exe")
                        {
                            var dest = Path.Combine(ToolDir, name);
                            e.ExtractToFile(dest, true);
                        }
                    }
                }
                File.Delete(zip);
            });
            Settings.FfmpegPath = Path.Combine(ToolDir, "ffmpeg.exe");
            Settings.FfprobePath = Path.Combine(ToolDir, "ffprobe.exe");
            Settings.Save();
        }

        static async Task Download(string url, string dest, string name, Action<string, int> report)
        {
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "MediaStudio");
                int last = -1;
                wc.DownloadProgressChanged += (s, e) =>
                {
                    int p = e.ProgressPercentage;
                    if (p != last) { last = p; report("Descarc " + name + ": " + p + "%", p); }
                };
                await wc.DownloadFileTaskAsync(new Uri(url), dest);
            }
        }
    }
}
