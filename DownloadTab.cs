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
        ComboBox dlType, dlQuality, dlContainer, dlAudioFmt, dlAudioQ, dlTemplate, dlPlaylist, dlCookies, dlExactFormat, dlSponsor;
        CheckBox dlSubs, dlAutoSubs, dlEmbedSubs, dlMeta, dlThumb, dlDesc, dlJson, dlChapters, dlArchive, dlNumber;
        TextBox dlSubLangs, dlItems, dlRate, dlMatch;

        void BuildDownloadTab(TabPage page)
        {
            var b = new Builder(page, dlFields);
            dlUrls = b.Text("urls", "&Adrese (una pe rând)", "", true, "Adresa unui videoclip, playlist sau canal. Poți pune mai multe, câte una pe rând.");
            dlType = b.Combo("type", "&Ce descarc", new[] { "Video cu sunet", "Doar audio", "Doar video, fără sunet" });
            dlQuality = b.Combo("quality", "Ca&litate video", new[] { "Cea mai bună", "2160p (4K)", "1440p", "1080p", "720p", "480p", "360p", "Cea mai mică" });
            dlContainer = b.Combo("container", "&Format video", new[] { "Automat", "mp4", "mkv", "webm", "mov" });
            dlAudioFmt = b.Combo("audioFmt", "Format a&udio", new[] { "mp3", "m4a", "opus", "flac", "wav", "aac", "vorbis", "Cel mai bun, fără conversie" });
            dlAudioQ = b.Combo("audioQ", "Calitate audi&o", new[] { "Cea mai bună", "320 kbps", "256 kbps", "192 kbps", "128 kbps", "96 kbps" });
            dlFolder = b.Path("folder", "Folder de &salvare", PathKind.Folder, null, Settings.DownloadFolder);
            dlTemplate = b.Combo("template", "&Nume fișier", new[] { "Titlu", "Titlu - Canal", "Număr în playlist - Titlu", "Folder cu numele canalului, apoi titlu", "Folder cu numele playlistului, apoi număr - titlu", "Data încărcării - Titlu" }, 0, true,
                "Poți alege din listă sau scrie un șablon yt-dlp propriu.");
            dlPlaylist = b.Combo("playlist", "Pla&ylist", new[] { "Doar videoclipul, dacă adresa conține și un playlist", "Tot playlistul sau canalul" });
            dlItems = b.Text("items", "Ele&mente din playlist", "", false, "Lasă gol pentru toate. Poți scrie poziții și intervale separate prin virgulă, de la-până la cu liniuță.");
            dlNumber = b.Check("number", "Numerotează fișierele după poziția din playlist");
            dlArchive = b.Check("archive", "Sari peste ce am descărcat deja (fișier de evidență în folderul de salvare)");

            var subs = b.Section("Subtitrări", true);
            dlSubs = subs.Check("subs", "Descarcă subtitrările");
            dlSubLangs = subs.Text("subLangs", "Limbi subtitrări", "ro,en", false, "Coduri de limbă separate prin virgulă. all înseamnă toate.");
            dlAutoSubs = subs.Check("autoSubs", "Include și subtitrările generate automat");
            dlEmbedSubs = subs.Check("embedSubs", "Pune subtitrările în fișierul video");

            var meta = b.Section("Metadate și extra", true);
            dlMeta = meta.Check("meta", "Adaugă metadatele (titlu, artist, dată) în fișier", true);
            dlThumb = meta.Check("thumb", "Pune miniatura ca copertă");
            dlChapters = meta.Check("chapters", "Păstrează capitolele");
            dlDesc = meta.Check("desc", "Salvează descrierea într-un fișier text");
            dlJson = meta.Check("json", "Salvează toate informațiile într-un fișier JSON");
            dlSponsor = meta.Combo("sponsor", "Elimină segmentele SponsorBlock", new[] { "Nu", "Reclamele sponsorizate", "Sponsorizări, introduceri, finaluri și autopromovare", "Toate categoriile" });

            var adv = b.Section("Opțiuni avansate de descărcare", true);
            dlExactFormat = adv.Combo("exactFormat", "Format exact (cod yt-dlp)", new[] { "" }, 0, true, "Apasă „Listează formatele disponibile” ca să completezi lista, apoi alege un format. Dacă e completat, înlocuiește alegerile de calitate.");
            dlCookies = adv.Combo("cookies", "Folosește conectarea din browser", new[] { "Nu", "chrome", "edge", "firefox", "brave", "opera", "vivaldi" }, 0, false, "Pentru videoclipuri care cer cont. Browserul trebuie să fie închis.");
            dlRate = adv.Text("rate", "Limită de viteză", "", false, "De exemplu 2M pentru 2 megaocteți pe secundă. Gol înseamnă fără limită.");
            dlMatch = adv.Text("match", "Doar titlurile care conțin", "", false, "Filtrează videoclipurile din playlist după un cuvânt din titlu.");

            b.Buttons(
                Builder.B("&Descarcă", (s, e) => StartDownload()),
                Builder.B("Listează formatele disponibile", (s, e) => ListFormats()),
                Builder.B("Arată comanda", (s, e) => { var j = BuildDownloadJob(); if (j != null) { lastCommand = Runner.CommandLine(j); Log("> " + lastCommand); Announce("Comanda e în jurnal și poate fi copiată cu butonul Copiază ultima comandă."); } }));
            PresetBar(b, "descarcare", () => dlFields);

            Action sync = () =>
            {
                bool audio = dlType.SelectedIndex == 1;
                dlQuality.Enabled = !audio; dlContainer.Enabled = !audio;
                dlAudioFmt.Enabled = audio; dlAudioQ.Enabled = audio;
            };
            dlType.SelectedIndexChanged += (s, e) => sync();
            sync();
        }

        IEnumerable<string> Urls() { return dlUrls.Text.Split('\n').Select(u => u.Trim()).Where(u => u.Length > 0); }

        Job BuildDownloadJob(bool forFormats = false)
        {
            var urls = Urls().ToList();
            if (urls.Count == 0) { Announce("Scrie cel puțin o adresă."); dlUrls.Focus(); return null; }
            var j = new Job(Tool.Ytdlp, urls.Count == 1 ? "Descărcare: " + urls[0] : "Descărcare: " + urls.Count + " adrese");
            j.A("--newline", "--no-colors");
            if (File.Exists(Settings.FfmpegPath)) j.A("--ffmpeg-location", Path.GetDirectoryName(Settings.FfmpegPath));
            if (forFormats) { j.Title = "Formate disponibile"; j.A("-F"); j.Args.AddRange(urls.Take(1)); return j; }

            var folder = string.IsNullOrWhiteSpace(dlFolder.Text) ? Settings.DownloadFolder : dlFolder.Text.Trim();
            j.A("-P", folder);
            j.A("-o", Template());

            string h = Regex.Match(dlQuality.Text, @"^\d+").Value;
            string exact = FirstWord(dlExactFormat.Text);
            if (dlType.SelectedIndex == 1)
            {
                if (exact.Length > 0) j.A("-f", exact); else j.A("-f", "ba/b");
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
                if (exact.Length > 0) f = exact;
                else if (dlType.SelectedIndex == 2) f = worst ? "wv" : "bv" + lim;
                else if (worst) f = "wv*+wa/w";
                else if (dlContainer.Text == "mp4") f = "bv*" + lim + "[ext=mp4]+ba[ext=m4a]/b" + lim + "[ext=mp4]/bv*" + lim + "+ba/b" + lim;
                else f = "bv*" + lim + "+ba/b" + lim;
                j.A("-f", f);
                if (dlContainer.SelectedIndex > 0) j.A("--merge-output-format", dlContainer.Text, "--remux-video", dlContainer.Text);
            }

            if (dlPlaylist.SelectedIndex == 0) j.A("--no-playlist"); else j.A("--yes-playlist");
            if (dlItems.Text.Trim().Length > 0) j.A("-I", dlItems.Text.Trim().Replace(" ", ""));
            if (dlArchive.Checked) j.A("--download-archive", Path.Combine(folder, "descarcate.txt"));

            if (dlSubs.Checked)
            {
                j.A("--write-subs", "--sub-langs", string.IsNullOrWhiteSpace(dlSubLangs.Text) ? "all" : dlSubLangs.Text.Replace(" ", ""));
                if (dlAutoSubs.Checked) j.A("--write-auto-subs");
                if (dlEmbedSubs.Checked && dlType.SelectedIndex != 1) j.A("--embed-subs"); else j.A("--convert-subs", "srt");
            }
            if (dlMeta.Checked) j.A("--embed-metadata");
            if (dlThumb.Checked) j.A("--embed-thumbnail");
            if (dlChapters.Checked) j.A("--embed-chapters");
            if (dlDesc.Checked) j.A("--write-description");
            if (dlJson.Checked) j.A("--write-info-json");
            switch (dlSponsor.SelectedIndex)
            {
                case 1: j.A("--sponsorblock-remove", "sponsor"); break;
                case 2: j.A("--sponsorblock-remove", "sponsor,intro,outro,selfpromo"); break;
                case 3: j.A("--sponsorblock-remove", "all"); break;
            }
            if (dlCookies.SelectedIndex > 0) j.A("--cookies-from-browser", dlCookies.Text);
            if (dlRate.Text.Trim().Length > 0) j.A("-r", dlRate.Text.Trim());
            if (dlMatch.Text.Trim().Length > 0) j.A("--match-filters", "title~=(?i)" + Regex.Escape(dlMatch.Text.Trim()));
            foreach (var extra in ExtraArgs(Tool.Ytdlp)) j.Args.Add(extra);
            j.A("--");
            j.Args.AddRange(urls);
            return j;
        }

        string Template()
        {
            string num = dlNumber.Checked ? "%(playlist_index|)s%(playlist_index& - |)s" : "";
            switch (dlTemplate.Text)
            {
                case "Titlu": return num + "%(title)s.%(ext)s";
                case "Titlu - Canal": return num + "%(title)s - %(uploader)s.%(ext)s";
                case "Număr în playlist - Titlu": return "%(playlist_index|)s%(playlist_index& - |)s%(title)s.%(ext)s";
                case "Folder cu numele canalului, apoi titlu": return "%(uploader)s/" + num + "%(title)s.%(ext)s";
                case "Folder cu numele playlistului, apoi număr - titlu": return "%(playlist_title,uploader)s/%(playlist_index|)s%(playlist_index& - |)s%(title)s.%(ext)s";
                case "Data încărcării - Titlu": return "%(upload_date>%Y-%m-%d)s - " + num + "%(title)s.%(ext)s";
                default: return dlTemplate.Text.Trim().Length > 0 ? dlTemplate.Text.Trim() : "%(title)s.%(ext)s";
            }
        }

        void StartDownload()
        {
            var j = BuildDownloadJob();
            if (j == null) return;
            var folder = string.IsNullOrWhiteSpace(dlFolder.Text) ? Settings.DownloadFolder : dlFolder.Text.Trim();
            try { Directory.CreateDirectory(folder); } catch (Exception ex) { ShowError("Nu pot folosi folderul de salvare: " + ex.Message); return; }
            Run(j);
        }

        void ListFormats()
        {
            var j = BuildDownloadJob(true);
            if (j == null) return;
            int start = 0;
            j.After = (job, code) =>
            {
                FlushLog();
                var lines = logBox.Text.Substring(Math.Min(start, logBox.TextLength)).Replace("\r", "").Split('\n');
                dlExactFormat.Items.Clear();
                dlExactFormat.Items.Add("");
                bool table = false;
                foreach (var l in lines)
                {
                    if (l.StartsWith("ID ")) { table = true; continue; }
                    if (!table || l.StartsWith("---") || l.Trim().Length == 0 || l.StartsWith("Gata") || l.StartsWith("===")) continue;
                    dlExactFormat.Items.Add(Regex.Replace(l.Trim(), @"\s+\|?\s*", " "));
                }
                if (dlExactFormat.Items.Count > 1)
                {
                    dlExactFormat.Items.Insert(1, "bv*+ba/b — cel mai bun video cu cel mai bun audio");
                    Announce("Am găsit " + (dlExactFormat.Items.Count - 2) + " formate. Le alegi din lista Format exact, din secțiunea Opțiuni avansate de descărcare.");
                }
            };
            FlushLog();
            start = logBox.TextLength;
            Run(j);
        }
    }
}
