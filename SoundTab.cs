using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        TextBox snAudio;
        ComboBox snAction;
        NumericUpDown snMixClip, snMixAudio;

        static readonly string[] SnActions =
        {
            "Pun sunetul nou în locul sunetului din clip",
            "Amestec sunetul nou cu sunetul din clip (se aud amândouă)",
            "Adaug sunetul nou ca a doua pistă de sunet (în player alegi pe care o asculți)"
        };

        /// <summary>Partea „Un sunet nou” din fila Poze și sunet peste clip: sunetul, ce fac cu el, volumele, Pune sunetul.</summary>
        void BuildSoundSection(Builder g)
        {
            snAudio = g.Path(null, "&Sunetul nou", PathKind.OpenFile, Dialogs.AudioFilter);

            // ce fac cu el apare după ce e ales sunetul
            Panel boxAudio;
            var h = g.Gate(out boxAudio);
            snAction = h.Combo(null, "Ce fac cu sunetul &nou", SnActions, 0);
            snMixClip = h.Number(null, "Volumul sunetului din clip (la sută, 0 înseamnă că nu se aude)", 0, 200, 30, 5);
            snMixAudio = h.Number(null, "Volumul sunetului nou (la sută)", 0, 200, 100, 5);
            h.Buttons(Builder.B("Pune s&unetul", (s, e) => Run(SoundJob())));

            // volumele au rost doar la amestec
            Action sync = () =>
            {
                bool mix = snAction.SelectedIndex == 1;
                Builder.ShowRow(snMixClip, mix);
                Builder.ShowRow(snMixAudio, mix);
            };
            snAction.SelectedIndexChanged += (s, e) => sync();
            sync();
            ShowWhenFile(snAudio, boxAudio);
        }

        Job SoundJob()
        {
            if (!Need(ovVideo.Text, "videoclipul")) return null;
            if (!Need(snAudio.Text, "sunetul nou")) return null;
            var input = ovVideo.Text.Trim();
            if (IsAudioOnly(input)) { Announce("Sunetul nou se pune doar peste un videoclip. Ai ales un fișier care are doar sunet."); return null; }
            var audio = snAudio.Text.Trim();
            int act = snAction.SelectedIndex;
            var ext = Path.GetExtension(input).ToLowerInvariant();
            const string folder = "Sunet peste videoclip";   // folderul rămâne cu acest nume

            var j = new Job(Tool.Ffmpeg, SnActions[Math.Max(0, act)] + ": " + Path.GetFileName(input)) { ProbeForDuration = input };
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input, "-i", audio);

            if (act == 1)
            {
                // amestec: se aud amândouă, fiecare cu volumul lui; ține cât clipul
                var streams = Probe(input);
                bool clipAudio = streams.Any(x => x.Type == "audio");
                bool clipVideo = streams.Any(x => x.Type == "video");
                string vc = Sec((double)snMixClip.Value / 100), va = Sec((double)snMixAudio.Value / 100);
                string mix = clipAudio
                    ? "[0:a:0]volume=" + vc + "[a0];[1:a:0]volume=" + va + "[a1];[a0][a1]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[aout]"
                    : "[1:a:0]volume=" + va + "[aout]";
                j.A("-filter_complex", mix);
                if (clipVideo) j.A("-map", "0:v:0", "-c:v", "copy");
                j.A("-map", "[aout]", "-c:a", "aac", "-b:a", "192k");
                if (!clipAudio) j.A("-shortest");
                if (ext != ".mp4" && ext != ".mov" && ext != ".mkv" && ext != ".m4v") ext = clipVideo ? ".mp4" : ".m4a";
                if (ext != ".mkv") j.A("-movflags", "+faststart");
                j.A(AutoOut(input, folder, "amestec", ext));
                return j;
            }

            if (act == 0) j.A("-map", "0:v?", "-map", "0:s?", "-map", "1:a", "-c", "copy", "-shortest");
            else j.A("-map", "0", "-map", "1:a", "-c", "copy");
            // Imaginea se copiază neatinsă, dar sunetul nou se convertește în formatul potrivit clipului,
            // ca să meargă orice fișier audio (wav, flac, mp3...), nu doar cele care încap direct în clip.
            if (ext == ".mkv") { }                                            // mkv primește orice sunet
            else if (ext == ".webm") j.A("-c:a", "libopus", "-b:a", "192k");
            else if (ext == ".avi") j.A("-c:a", "libmp3lame", "-b:a", "256k");
            else j.A("-c:a", "aac", "-b:a", "256k");                          // mp4, mov, m4v și restul
            j.A(AutoOut(input, folder, act == 0 ? "sunet_inlocuit" : "pista_adaugata", Path.GetExtension(input)));
            return j;
        }
    }
}
