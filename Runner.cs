using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaStudio
{
    enum Tool { Ytdlp, Ffmpeg, Ffprobe }

    /// <summary>O operațiune de rulat: programul, argumentele și o descriere pentru utilizator.</summary>
    class Job
    {
        public Tool Tool;
        public List<string> Args = new List<string>();
        public string Title;
        public double DurationSeconds;      // pentru progresul FFmpeg
        public string ProbeForDuration;     // fișier din care se află durata înainte de pornire
        public Action<Job, int> After;      // rulează după terminare (cu codul de ieșire)
        public string TempFile;             // se șterge la final
        public Job(Tool t, string title) { Tool = t; Title = title; }
        public Job A(params string[] a) { Args.AddRange(a); return this; }
    }

    /// <summary>Rulează un program extern fără fereastră de consolă și transmite rândurile și progresul.</summary>
    class Runner
    {
        public event Action<string, bool> Line;   // text, este eroare
        public event Action<double> Progress;     // 0..100
        public event Action<int> Exited;
        Process proc;
        public Job Current;
        public readonly List<string> ErrorLines = new List<string>();
        public readonly List<string> TailLines = new List<string>();
        static readonly Regex YtPct = new Regex(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);
        static readonly Regex FfTime = new Regex(@"^out_time_(?:us|ms)=(\d+)", RegexOptions.Compiled);

        public bool Running { get { try { return proc != null && !proc.HasExited; } catch { return false; } } }

        public static string ExePath(Tool t)
        {
            return t == Tool.Ytdlp ? Settings.YtdlpPath : t == Tool.Ffmpeg ? Settings.FfmpegPath : Settings.FfprobePath;
        }

        public static string Quote(string a)
        {
            if (a == null) return "\"\"";
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"', '&', '|', '<', '>', '^', '(', ')' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; continue; }
                sb.Append('\\', bs); bs = 0; sb.Append(c);
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string CommandLine(Job j)
        {
            var exe = ExePath(j.Tool);
            var name = string.IsNullOrEmpty(exe) ? (j.Tool == Tool.Ytdlp ? "yt-dlp" : j.Tool == Tool.Ffmpeg ? "ffmpeg" : "ffprobe") : exe;
            return Quote(name) + " " + string.Join(" ", j.Args.Select(Quote));
        }

        public void Start(Job j)
        {
            Current = j;
            ErrorLines.Clear(); TailLines.Clear();
            var exe = ExePath(j.Tool);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) throw new FileNotFoundException(Tools.MissingText(j.Tool));
            if (j.Tool == Tool.Ffmpeg && j.DurationSeconds <= 0 && !string.IsNullOrEmpty(j.ProbeForDuration)) j.DurationSeconds = ProbeDuration(j.ProbeForDuration);

            var psi = new ProcessStartInfo(exe, string.Join(" ", j.Args.Select(Quote)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Settings.DownloadFolder
            };
            if (!Directory.Exists(psi.WorkingDirectory)) psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) Handle(e.Data, false); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Handle(e.Data, true); };
            proc.Exited += (s, e) =>
            {
                int code = -1;
                try { proc.WaitForExit(); code = proc.ExitCode; } catch { }
                if (Exited != null) Exited(code);
            };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }

        void Handle(string line, bool stderr)
        {
            var j = Current;
            if (j != null && j.Tool == Tool.Ffmpeg)
            {
                var m = FfTime.Match(line);
                if (m.Success)
                {
                    if (j.DurationSeconds > 0 && Progress != null)
                    {
                        double sec = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 1000000.0;
                        Progress(Math.Max(0, Math.Min(100, sec / j.DurationSeconds * 100)));
                    }
                    return;
                }
                if (Regex.IsMatch(line, @"^(frame|fps|stream_\d+_\d+_q|bitrate|total_size|out_time|dup_frames|drop_frames|speed|progress)=")) return;
            }
            if (j != null && j.Tool == Tool.Ytdlp)
            {
                var m = YtPct.Match(line);
                if (m.Success && Progress != null) Progress(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            bool isErr = line.IndexOf("ERROR", StringComparison.Ordinal) >= 0 || line.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
                         || line.IndexOf("Invalid", StringComparison.Ordinal) >= 0 || line.IndexOf("No such file", StringComparison.Ordinal) >= 0
                         || line.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 && stderr;
            if (isErr) lock (ErrorLines) ErrorLines.Add(line);
            lock (TailLines) { TailLines.Add(line); if (TailLines.Count > 12) TailLines.RemoveAt(0); }
            if (Line != null) Line(line, isErr);
        }

        public void Cancel()
        {
            try
            {
                if (!Running) return;
                try { proc.StandardInput.Write("q"); proc.StandardInput.Flush(); } catch { }
                if (proc.WaitForExit(1500)) return;
                var k = new ProcessStartInfo("taskkill", "/PID " + proc.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(k).WaitForExit(5000);
            }
            catch { }
        }

        // ---------- rulări scurte, sincrone (pentru citirea informațiilor) ----------
        public static string Capture(Tool t, string args, int timeoutMs = 20000)
        {
            var exe = ExePath(t);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var outp = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                    var o = outp.Result;
                    return string.IsNullOrWhiteSpace(o) ? err.Result : o + (err.Result.Length > 0 ? "\n" + err.Result : "");
                }
            }
            catch { return null; }
        }

        public static double ProbeDuration(string file)
        {
            var o = Capture(Tool.Ffprobe, "-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 " + Quote(file), 15000);
            double d;
            if (o != null && double.TryParse(o.Trim().Split('\n')[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }
    }
}
