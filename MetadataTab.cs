using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        TextBox mdIn, mdTitle, mdArtist, mdAlbum, mdAlbumArtist, mdYear, mdGenre, mdTrack, mdComment, mdCover, mdOut;
        CheckBox mdClear, mdRemoveCover;

        void BuildMetadataTab(TabPage page)
        {
            var b = new Builder(page);
            mdIn = b.Path(null, "&Fișier", PathKind.OpenFile, Dialogs.MediaFilter);
            b.Buttons(Builder.B("&Citește metadatele existente", (s, e) => ReadMetadata()));
            mdTitle = b.Text(null, "&Titlu");
            mdArtist = b.Text(null, "&Artist");
            mdAlbum = b.Text(null, "Al&bum");
            mdAlbumArtist = b.Text(null, "Artistul albumului");
            mdYear = b.Text(null, "A&n");
            mdGenre = b.Text(null, "&Gen");
            mdTrack = b.Text(null, "Număr pistă");
            mdComment = b.Text(null, "C&omentariu", "", true);
            mdCover = b.Path(null, "Imagine de cop&ertă", PathKind.OpenFile, Dialogs.ImageFilter);
            mdRemoveCover = b.Check(null, "Elimină coperta existentă");
            mdClear = b.Check(null, "Șterge toate celelalte metadate");
            mdOut = b.Path(null, "Fișier rezultat (gol = lângă original)", PathKind.SaveFile, Dialogs.MediaFilter);
            b.Buttons(Builder.B("&Salvează metadatele", (s, e) => Run(MetadataJob())));
        }

        void ReadMetadata()
        {
            if (!Need(mdIn.Text, "un fișier")) return;
            var o = Runner.Capture(Tool.Ffprobe, "-v error -show_entries format_tags -of default=noprint_wrappers=1 " + Runner.Quote(mdIn.Text.Trim()));
            if (o == null) { ShowError(Tools.MissingText(Tool.Ffprobe)); return; }
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in o.Replace("\r", "").Split('\n'))
            {
                var t = l.StartsWith("TAG:") ? l.Substring(4) : l;
                int i = t.IndexOf('=');
                if (i > 0) tags[t.Substring(0, i)] = t.Substring(i + 1);
            }
            Func<string[], string> g = keys => { foreach (var k in keys) { string v; if (tags.TryGetValue(k, out v)) return v; } return ""; };
            mdTitle.Text = g(new[] { "title" });
            mdArtist.Text = g(new[] { "artist" });
            mdAlbum.Text = g(new[] { "album" });
            mdAlbumArtist.Text = g(new[] { "album_artist" });
            mdYear.Text = g(new[] { "date", "year" });
            mdGenre.Text = g(new[] { "genre" });
            mdTrack.Text = g(new[] { "track" });
            mdComment.Text = g(new[] { "comment", "description" });
            Announce(tags.Count == 0 ? "Fișierul nu are metadate." : "Am citit " + tags.Count + " câmpuri de metadate.");
        }

        Job MetadataJob()
        {
            if (!Need(mdIn.Text, "un fișier")) return null;
            var input = mdIn.Text.Trim();
            var ext = Path.GetExtension(input).ToLowerInvariant();
            var outp = mdOut.Text.Trim().Length > 0 ? mdOut.Text.Trim() : OutPath(input, null, " (metadate)", ext);
            var j = new Job(Tool.Ffmpeg, "Metadate: " + Path.GetFileName(input));
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-i", input);
            bool cover = mdCover.Text.Trim().Length > 0 && File.Exists(mdCover.Text.Trim());
            bool audioFile = new[] { ".mp3", ".m4a", ".flac", ".ogg", ".opus", ".wma", ".aac", ".wav" }.Contains(ext);
            if (cover) j.A("-i", mdCover.Text.Trim());
            if (audioFile && (cover || mdRemoveCover.Checked)) j.A("-map", "0:a"); else j.A("-map", "0");
            if (cover)
            {
                j.A("-map", "1", "-c", "copy");
                int vIndex = audioFile ? 0 : CountVideo(input);
                j.A("-disposition:v:" + vIndex, "attached_pic");
                if (ext == ".mp3") j.A("-id3v2_version", "3", "-metadata:s:v", "title=Album cover", "-metadata:s:v", "comment=Cover (front)");
            }
            else
            {
                j.A("-c", "copy");
                if (mdRemoveCover.Checked && !audioFile) Log("Notă: eliminarea copertei funcționează doar la fișierele audio; la video am păstrat imaginile.");
            }
            if (mdClear.Checked) j.A("-map_metadata", "-1");
            Action<string, TextBox> md = (k, t) => { if (t.Text.Trim().Length > 0 || mdClear.Checked) j.A("-metadata", k + "=" + t.Text.Trim()); };
            md("title", mdTitle); md("artist", mdArtist); md("album", mdAlbum); md("album_artist", mdAlbumArtist);
            md("date", mdYear); md("genre", mdGenre); md("track", mdTrack); md("comment", mdComment);
            j.A(outp);
            return j;
        }

        int CountVideo(string file)
        {
            try { return Probe(file).Count(s => s.Type == "video"); } catch { return 0; }
        }
    }
}
