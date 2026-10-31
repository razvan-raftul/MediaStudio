using System;
using System.IO;
using System.Media;

namespace MediaStudio
{
    /// <summary>
    /// Sunetele de la sfârșitul operațiunilor, făcute de program (nu depind de schema de sunete din Windows,
    /// care poate fi pe „Fără sunete”). Câteva acorduri scurte și blânde; la fiecare operațiune se alege altul.
    /// </summary>
    static class Sounds
    {
        static readonly Random rnd = new Random();
        static int last = -1;
        static SoundPlayer player;   // ținut în viață cât cântă

        // acorduri arpegiate, frecvențe în Hz
        static readonly double[][] okSets =
        {
            new[] { 523.25, 659.25, 783.99 },          // Do Mi Sol
            new[] { 587.33, 739.99, 880.00 },          // Re Fa# La
            new[] { 659.25, 830.61, 987.77 },          // Mi Sol# Si
            new[] { 698.46, 880.00, 1046.50 },         // Fa La Do
            new[] { 440.00, 554.37, 659.25, 880.00 },  // La Do# Mi La
            new[] { 783.99, 659.25, 1046.50 },         // Sol Mi Do
            new[] { 493.88, 739.99, 987.77 },          // Si Fa# Si
            new[] { 523.25, 783.99, 1046.50 },         // Do Sol Do
        };
        static readonly double[] failSet = { 392.00, 311.13 };   // Sol, Mi bemol: coboară, ca o întrebare

        /// <summary>Sunetul de final: ok = a mers. Nu face nimic dacă sunetul e oprit din Setări.</summary>
        public static void Done(bool ok)
        {
            if (!Settings.DoneSound) return;
            try
            {
                double[] notes;
                if (ok)
                {
                    int i;
                    do i = rnd.Next(okSets.Length); while (i == last);
                    last = i;
                    notes = okSets[i];
                }
                else notes = failSet;
                var p = new SoundPlayer(new MemoryStream(Make(notes, ok ? 0.11 : 0.24)));
                p.Load();
                p.Play();
                player = p;
            }
            catch { }
        }

        /// <summary>Un WAV mono de 16 biți: fiecare notă pornește puțin după cea dinainte și se stinge încet, ca un clopoțel.</summary>
        static byte[] Make(double[] notes, double gap)
        {
            const int rate = 44100;
            const double noteLen = 0.9;
            int n = (int)(rate * (gap * (notes.Length - 1) + noteLen + 0.05));
            var buf = new double[n];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = (int)(k * gap * rate);
                double f = notes[k];
                for (int i = 0; i < (int)(noteLen * rate) && start + i < n; i++)
                {
                    double t = (double)i / rate;
                    double env = Math.Min(1.0, t / 0.006) * Math.Exp(-t * 5.5);
                    double v = Math.Sin(2 * Math.PI * f * t)
                             + 0.25 * Math.Sin(2 * Math.PI * 2 * f * t) * Math.Exp(-t * 8)
                             + 0.08 * Math.Sin(2 * Math.PI * 3 * f * t) * Math.Exp(-t * 12);
                    buf[start + i] += v * env;
                }
            }
            double peak = 0;
            foreach (var v in buf) peak = Math.Max(peak, Math.Abs(v));
            double scale = peak > 0 ? 0.28 / peak : 0;   // destul de încet: cam o pătrime din volumul maxim

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int bytes = n * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (var v in buf) w.Write((short)Math.Max(-32767, Math.Min(32767, v * scale * 32767)));
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
