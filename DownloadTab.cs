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
        readonly Dictionary<string, Control> dlFields = new Dictionary<string, Control>();
        TextBox dlUrls, dlFolder;
        ComboBox dlType, dlQuality, dlContainer, dlAudioFmt, dlAudioQ;
        CheckBox dlSubs, dlAutoSubs, dlEmbedSubs;
        TextBox dlSubLangs;

        void BuildDownloadTab(TabPage page)
        {
            var b = new Builder(page, dlFields);
            dlUrls = b.Text("urls", "&Adrese", "", false, "Lipește linkul unui videoclip sau al unui playlist. Dacă pui mai multe, le desparți prin virgulă. La un playlist se face singur un folder cu numele lui.");
            dlType = b.Combo("type", "&Ce descarc", new[] { "Video cu sunet", "Doar audio", "Doar video, fără sunet" });
            dlQuality = b.Combo("quality", "Ca&litate video", new[] { "Cea mai bună", "2160p (4K)", "1440p", "1080p", "720p", "480p", "360p", "Cea mai mică" });
            dlContainer = b.Combo("container", "&Format video", new[] { "Automat", "mp4", "mkv", "webm", "mov" });
            dlAudioFmt = b.Combo("audioFmt", "Format a&udio", new[] { "mp3", "m4a", "opus", "flac", "wav", "aac", "vorbis", "Cel mai bun, fără conversie" });
            dlAudioQ = b.Combo("audioQ", "Calitate audi&o", new[] { "Cea mai bună", "320 kbps", "256 kbps", "192 kbps", "128 kbps", "96 kbps" });
            dlFolder = b.Path("folder", "Folder de &salvare", PathKind.Folder, null, Settings.DownloadFolder);

            var subs = b.Section("Subtitrări", true);
            dlSubs = subs.Check("subs", "Descarcă subtitrările");
            dlSubLangs = subs.Text("subLangs", "Limbi subtitrări", "ro,en", false, "Coduri de limbă separate prin virgulă. all înseamnă toate.");
            dlAutoSubs = subs.Check("autoSubs", "Include și subtitrările generate automat");
            dlEmbedSubs = subs.Check("embedSubs", "Pune subtitrările în fișierul video");

            b.Buttons(Builder.B("&Descarcă", (s, e) => StartDownload()));

            Action sync = () =>
            {
                bool audio = dlType.SelectedIndex == 1;
                dlQuality.Enabled = !audio; dlContainer.Enabled = !audio;
                dlAudioFmt.Enabled = audio; dlAudioQ.Enabled = audio;
            };
            dlType.SelectedIndexChanged += (s, e) => sync();
            sync();
        }

        // adresele se despart prin virgulă, spațiu sau rând nou
        IEnumerable<string> Urls() { return Regex.Split(dlUrls.Text, @"[\s,;]+").Select(u => u.Trim()).Where(u => u.Length > 0); }

        Job BuildDownloadJob()
        {
            var urls = Urls().ToList();
            if (urls.Count == 0) { Announce("Scrie cel puțin o adresă."); dlUrls.Focus(); return null; }
            var j = new Job(Tool.Ytdlp, urls.Count == 1 ? "Descărcare: " + urls[0] : "Descărcare: " + urls.Count + " adrese");
            j.A("--newline", "--no-colors");
            if (File.Exists(Settings.FfmpegPath)) j.A("--ffmpeg-location", Path.GetDirectoryName(Settings.FfmpegPath));

            var folder = string.IsNullOrWhiteSpace(dlFolder.Text) ? Settings.DownloadFolder : dlFolder.Text.Trim();
            j.A("-P", folder);
            // numele original; la playlist intră într-un folder cu numele playlistului
            j.A("-o", "%(playlist_title&{}/|)s%(title)s.%(ext)s");
            // un link de videoclip care conține și playlist descarcă doar videoclipul; un link de playlist descarcă tot playlistul
            j.A("--no-playlist");

            string h = Regex.Match(dlQuality.Text, @"^\d+").Value;
            if (dlType.SelectedIndex == 1)
            {
                j.A("-f", "ba/b");
                if (dlAudioFmt.SelectedIndex < dlAudioFmt.Items.Count - 1)
                {
                    j.A("-x", "--audio-format", dlAudioFmt.Text);
                    var q = Regex.Match(dlAudioQ.Text, @"^\d+").Value;
                    j.A("--audio-quality", q.Length > 0 ? q + "K" : "0");
                }
                else j.A("-x");
            }
            else
            {
                string f;
                string lim = h.Length > 0 ? "[height<=" + h + "]" : "";
                bool worst = dlQuality.SelectedIndex == dlQuality.Items.Count - 1;
                if (dlType.SelectedIndex == 2) f = worst ? "wv" : "bv" + lim;
                else if (worst) f = "wv*+wa/w";
                else if (dlContainer.Text == "mp4") f = "bv*" + lim + "[ext=mp4]+ba[ext=m4a]/b" + lim + "[ext=mp4]/bv*" + lim + "+ba/b" + lim;
                else f = "bv*" + lim + "+ba/b" + lim;
                j.A("-f", f);
                if (dlContainer.SelectedIndex > 0) j.A("--merge-output-format", dlContainer.Text, "--remux-video", dlContainer.Text);
            }

            if (dlSubs.Checked)
            {
                j.A("--write-subs", "--sub-langs", string.IsNullOrWhiteSpace(dlSubLangs.Text) ? "all" : dlSubLangs.Text.Replace(" ", ""));
                if (dlAutoSubs.Checked) j.A("--write-auto-subs");
                if (dlEmbedSubs.Checked && dlType.SelectedIndex != 1) j.A("--embed-subs"); else j.A("--convert-subs", "srt");
            }
            j.A("--embed-metadata");
            foreach (var extra in ExtraArgs(Tool.Ytdlp)) j.Args.Add(extra);
            j.A("--");
            j.Args.AddRange(urls);
            return j;
        }

        void StartDownload()
        {
            var j = BuildDownloadJob();
            if (j == null) return;
            var folder = string.IsNullOrWhiteSpace(dlFolder.Text) ? Settings.DownloadFolder : dlFolder.Text.Trim();
            try { Directory.CreateDirectory(folder); } catch (Exception ex) { ShowError("Nu pot folosi folderul de salvare: " + ex.Message); return; }
            Run(j);
        }
    }
}
