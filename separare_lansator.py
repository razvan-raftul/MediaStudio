"""Separarea pieselor audio și schimbarea tonalității, pentru Media Studio (tabul Voce și instrumente).
Din el se face separare.exe în workflow-ul „separare” (cu Demucs, BS-RoFormer, modelele lor și ffmpeg alături).
Vocea și negativul se scot cu BS-RoFormer (modelul Viperx 1297, același fel de model ca pe X-Minus), prin pachetul audio-separator;
tobele, basul, chitara, pianul și restul se scot cu Demucs. Dacă modelul RoFormer lipsește, totul merge cu Demucs, ca înainte.

Folosire:
  separare.exe --intrare PIESA --iesire FOLDER [--element COD ETICHETA]... [--impreuna ETICHETA]
               [--semitonuri N] [--format mp3|wav] [--model htdemucs|htdemucs_6s|htdemucs_ft] [--fara-roformer]

Codurile elementelor: voce, tobe, bas, chitara, pian, restul, negativ (tot fără voce), original (piesa întreagă).
Cu --impreuna, elementele alese se pun împreună într-un singur fișier (cu eticheta dată).
Programul scrie pe ieșire, câte un rând: „PROGRES n” (0–100), „FISIER cale”, „EROARE text”, „GATA”.
"""
import argparse
import multiprocessing
import os
import shutil
import subprocess
import sys

BAZA = os.path.dirname(sys.executable if getattr(sys, 'frozen', False) else os.path.abspath(__file__))
SR = 44100
SURSE = {'voce': 'vocals', 'tobe': 'drums', 'bas': 'bass', 'chitara': 'guitar', 'pian': 'piano', 'restul': 'other'}
FARA_FEREASTRA = 0x08000000 if os.name == 'nt' else 0
ROFORMER = 'model_bs_roformer_ep_317_sdr_12.9755.ckpt'
DIR_ROFORMER = os.path.join(BAZA, 'modele', 'roformer')


def spune(*a):
    print(*a, flush=True)


def ffmpeg():
    for c in (os.path.join(BAZA, 'ffmpeg.exe'), os.path.join(BAZA, 'ffmpeg')):
        if os.path.isfile(c):
            return c
    return shutil.which('ffmpeg') or 'ffmpeg'


def are_rubberband():
    try:
        p = subprocess.run([ffmpeg(), '-hide_banner', '-filters'], capture_output=True, text=True, creationflags=FARA_FEREASTRA)
        return ' rubberband ' in p.stdout
    except Exception:
        return False


def filtru_tonalitate(semitonuri):
    """Filtrul ffmpeg care mută piesa cu atâtea semitonuri, fără să-i schimbe tempoul."""
    if not semitonuri:
        return None
    r = 2 ** (semitonuri / 12)
    if are_rubberband():
        return f'rubberband=pitch={r:.6f}:formant=preserved:pitchq=quality'
    # fără rubberband: o accelerăm (urcă tonul) și apoi îi refacem tempoul
    return f'asetrate={SR * r:.3f},aresample={SR},atempo={1 / r:.6f}'


def codare(fmt):
    if fmt == 'wav':
        return ['-c:a', 'pcm_s16le']
    return ['-c:a', 'libmp3lame', '-b:a', '320k']


def citeste(cale):
    """Piesa ca numpy (2, n) float32, stereo, 44100 Hz (ffmpeg citește mp3, wav, flac, m4a, ogg...)."""
    import numpy as np
    p = subprocess.run([ffmpeg(), '-v', 'error', '-i', cale, '-vn', '-f', 'f32le', '-ac', '2', '-ar', str(SR), '-'],
                       capture_output=True, creationflags=FARA_FEREASTRA)
    if p.returncode != 0 or not p.stdout:
        raise RuntimeError('ffmpeg: ' + p.stderr.decode('utf-8', 'replace').strip()[-400:])
    return np.frombuffer(p.stdout, dtype=np.float32).reshape(-1, 2).T.copy()


def scrie(cale, date, semitonuri, fmt):
    """date: numpy (2, n) float32. Scrie fișierul, cu tonalitatea schimbată dacă trebuie."""
    import numpy as np
    filtre = []
    f = filtru_tonalitate(semitonuri)
    if f:
        filtre.append(f)
    filtre.append('alimiter=limit=0.98:level=disabled')
    cmd = [ffmpeg(), '-v', 'error', '-y', '-f', 'f32le', '-ar', str(SR), '-ac', '2', '-i', '-', '-af', ','.join(filtre)] + codare(fmt) + [cale]
    p = subprocess.run(cmd, input=np.ascontiguousarray(date.T, dtype=np.float32).tobytes(), capture_output=True,
                       creationflags=FARA_FEREASTRA)
    if p.returncode != 0:
        raise RuntimeError('ffmpeg: ' + p.stderr.decode('utf-8', 'replace').strip()[-400:])
    spune('FISIER', cale)


def doar_tonalitate(intrare, cale, semitonuri, fmt):
    filtre = filtru_tonalitate(semitonuri)
    cmd = [ffmpeg(), '-v', 'error', '-y', '-i', intrare, '-vn'] + (['-af', filtre] if filtre else []) + codare(fmt) + [cale]
    p = subprocess.run(cmd, capture_output=True, creationflags=FARA_FEREASTRA)
    if p.returncode != 0:
        raise RuntimeError('ffmpeg: ' + p.stderr.decode('utf-8', 'replace').strip()[-400:])
    spune('FISIER', cale)


def are_roformer():
    return os.path.isfile(os.path.join(DIR_ROFORMER, ROFORMER))


def separa_roformer(date, de_la, pana_la):
    """Vocea și negativul cu BS-RoFormer. Întoarce (voce, negativ), numpy (2, n), cât piesa."""
    import inspect
    import logging
    import tempfile
    import numpy as np
    import tqdm as _tqdm_pachet
    import tqdm.auto as _tqdm_auto

    original = _tqdm_pachet.tqdm

    class _Progres(original):
        """În locul barei tqdm: spune procentul (din de_la până la pana_la) pe măsură ce se lucrează piesa."""
        def __init__(self, *a, **k):
            k['disable'] = False
            k['file'] = open(os.devnull, 'w')
            super().__init__(*a, **k)
            self._ultim = -1

        def update(self, n=1):
            r = super().update(n)
            if self.total:
                p = de_la + int((pana_la - de_la) * min(1.0, self.n / self.total))
                if p != self._ultim:
                    self._ultim = p
                    spune('PROGRES', p)
            return r

    _tqdm_pachet.tqdm = _Progres
    _tqdm_auto.tqdm = _Progres
    os.environ['PATH'] = BAZA + os.pathsep + os.environ.get('PATH', '')     # pydub găsește ffmpeg-ul de lângă
    import torch
    torch.set_num_threads(max(1, os.cpu_count() or 1))
    from audio_separator.separator import Separator

    with tempfile.TemporaryDirectory(prefix='separare-') as tmp:
        intrare = os.path.join(tmp, 'piesa.wav')
        p = subprocess.run([ffmpeg(), '-v', 'error', '-y', '-f', 'f32le', '-ar', str(SR), '-ac', '2', '-i', '-', '-c:a', 'pcm_f32le', intrare],
                           input=np.ascontiguousarray(date.T, dtype=np.float32).tobytes(), capture_output=True, creationflags=FARA_FEREASTRA)
        if p.returncode != 0:
            raise RuntimeError('ffmpeg: ' + p.stderr.decode('utf-8', 'replace').strip()[-400:])
        iesire = os.path.join(tmp, 'iesire')
        os.makedirs(iesire)
        param = dict(model_file_dir=DIR_ROFORMER, output_dir=iesire, output_format='WAV', sample_rate=SR,
                     log_level=logging.WARNING, use_soundfile=True,
                     # overlap 4 în loc de 8: de două ori mai repede pe procesor, aproape aceeași calitate
                     mdxc_params={'segment_size': 256, 'override_model_segment_size': False, 'batch_size': 1,
                                  'overlap': 4, 'pitch_shift': 0})
        acceptate = inspect.signature(Separator.__init__).parameters
        sep = Separator(**{k: v for k, v in param.items() if k in acceptate})
        sep.load_model(ROFORMER)
        fisiere = [f if os.path.isabs(f) else os.path.join(iesire, os.path.basename(f)) for f in sep.separate(intrare)]
        _tqdm_pachet.tqdm = original
        _tqdm_auto.tqdm = original
        voce = next((f for f in fisiere if '(vocals)' in os.path.basename(f).lower()), None)
        negativ = next((f for f in fisiere if '(instrumental)' in os.path.basename(f).lower()), None)
        if voce is None:
            raise RuntimeError('BS-RoFormer nu a dat vocea: ' + ', '.join(os.path.basename(f) for f in fisiere))
        v = citeste(voce)
        n = date.shape[1]
        v = v[:, :n] if v.shape[1] >= n else np.pad(v, ((0, 0), (0, n - v.shape[1])))
        if negativ is not None:
            neg = citeste(negativ)
            neg = neg[:, :n] if neg.shape[1] >= n else np.pad(neg, ((0, 0), (0, n - neg.shape[1])))
        else:
            neg = date - v
        return v, neg


def separa(date, model_nume, de_la=5, pana_la=90):
    """Desparte piesa cu Demucs. Întoarce {sursa: numpy (2, n)}."""
    import numpy as np
    import torch
    from pathlib import Path
    import demucs.apply as da
    from demucs.pretrained import get_model

    torch.set_num_threads(max(1, os.cpu_count() or 1))
    model = get_model(model_nume, repo=Path(BAZA, 'modele'))
    model.eval()
    parti = len(getattr(model, 'models', [model]))
    stare = {'apel': 0}

    class _Progres:
        """Înlocuiește tqdm din Demucs: spune procentul pe măsură ce se lucrează bucățile piesei."""
        @staticmethod
        def tqdm(lucrari, **_):
            lucrari = list(lucrari)
            apel = stare['apel']
            stare['apel'] += 1
            ultim = -1
            for i, x in enumerate(lucrari):
                yield x
                procent = de_la + int((pana_la - de_la) * (apel + (i + 1) / max(1, len(lucrari))) / parti)
                if procent != ultim:
                    ultim = procent
                    spune('PROGRES', min(pana_la, procent))

    da.tqdm = _Progres
    wav = torch.from_numpy(date)
    ref = wav.mean(0)
    medie, abatere = ref.mean(), ref.std() + 1e-8
    with torch.no_grad():
        iesire = da.apply_model(model, ((wav - medie) / abatere)[None], device='cpu', shifts=1, split=True,
                                overlap=0.25, progress=True, num_workers=0)[0]
    iesire = iesire * abatere + medie
    return {nume: iesire[i].numpy().astype(np.float32) for i, nume in enumerate(model.sources)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--intrare')
    ap.add_argument('--iesire')
    ap.add_argument('--element', nargs=2, action='append', default=[], metavar=('COD', 'ETICHETA'))
    ap.add_argument('--impreuna')
    ap.add_argument('--semitonuri', type=int, default=0)
    ap.add_argument('--format', choices=['mp3', 'wav'], default='mp3')
    ap.add_argument('--model')
    ap.add_argument('--fara-roformer', action='store_true', help='vocea și negativul tot cu Demucs')
    ap.add_argument('--verifica', action='store_true', help='spune ce poate (rubberband, modele) și iese')
    a = ap.parse_args()
    if a.verifica:
        spune('RUBBERBAND', are_rubberband())
        spune('MODELE', sorted(os.listdir(os.path.join(BAZA, 'modele'))) if os.path.isdir(os.path.join(BAZA, 'modele')) else [])
        spune('ROFORMER', are_roformer())
        return
    if not a.intrare or not a.iesire or not a.element:
        ap.error('trebuie --intrare, --iesire și cel puțin un --element')
    os.makedirs(a.iesire, exist_ok=True)
    titlu = os.path.splitext(os.path.basename(a.intrare))[0]
    ext = '.' + a.format

    def cale(eticheta):
        nume = f'{titlu} - {eticheta}{ext}'
        return os.path.join(a.iesire, ''.join('_' if c in '<>:"/\\|?*' else c for c in nume))

    coduri = [c for c, _ in a.element]
    spune('PROGRES', 0)
    if all(c == 'original' for c in coduri):
        for c, et in a.element:
            doar_tonalitate(a.intrare, cale(et), a.semitonuri, a.format)
        spune('PROGRES', 100)
        spune('GATA')
        return
    date = citeste(a.intrare)
    spune('PROGRES', 5)
    cu_roformer = not a.fara_roformer and are_roformer() and any(c in ('voce', 'negativ') for c in coduri)
    cu_demucs = not cu_roformer or any(c in SURSE and c != 'voce' for c in coduri)
    rof = None
    if cu_roformer:
        rof = separa_roformer(date, 5, 50 if cu_demucs else 90)
    surse = {}
    if cu_demucs:
        model = a.model or ('htdemucs_6s' if any(c in ('chitara', 'pian') for c in coduri) else 'htdemucs')
        surse = separa(date, model, 50 if cu_roformer else 5, 90)

    def element(cod):
        if cod == 'original':
            return date
        if rof is not None and cod == 'voce':
            return rof[0]
        if rof is not None and cod == 'negativ':
            return rof[1]
        if cod == 'negativ':
            return sum(v for k, v in surse.items() if k != 'vocals')
        return surse[SURSE[cod]]

    spune('PROGRES', 91)
    if a.impreuna:
        scrie(cale(a.impreuna), sum(element(c) for c in coduri), a.semitonuri, a.format)
    else:
        for i, (c, et) in enumerate(a.element):
            scrie(cale(et), element(c), a.semitonuri, a.format)
            spune('PROGRES', 91 + 9 * (i + 1) // len(a.element))
    spune('PROGRES', 100)
    spune('GATA')


if __name__ == '__main__':
    multiprocessing.freeze_support()
    if sys.stdout is None:
        sys.stdout = open(os.devnull, 'w')
        sys.stderr = sys.stdout
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    try:
        main()
    except Exception as ex:
        spune('EROARE', f'{type(ex).__name__}: {ex}')
        sys.exit(1)
