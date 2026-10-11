#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Собирает мяуканье рогатки из НАСТОЯЩЕЙ записи кота (не синтез).

Источник: crafter-station/elements, registry-item "sfx-cat-meow"
  https://github.com/crafter-station/elements/blob/main/public/r/sfx-cat-meow.json
  Описание в реестре: "Real cat meow for notifications and fun UI interactions. CC0 license."
  Репозиторий под MIT, сам звук помечен CC0 -> можно свободно использовать.
  Внутри registry-json звук лежит как data:audio/mpeg;base64 (обычный текст,
  поэтому файл доступен на GitHub без LFS). Декодированная копия лежит рядом:
  Tools/audio_tools/sources/cat_meow_cc0.mp3

Что делаем:
  CatMeowShort.wav  - тот же кот, обрезан по тишине, нормализован. Короткое «мяу»
                      в момент натяжения рогатки. Без изменения тона.
  CatMeowLong.wav   - тот же кот: тон ниже на LONG_PITCH_SEMITONES полутонов
                      (замедление/ресемплинг - приём «большого кота»), затем
                      растянут гранулярным OLA до LONG_TARGET_S, утолщён вторым
                      слоем и подкрашен коротким залом зала. Большое «мяяяууу»
                      в момент вылета снаряда.

Формат на выходе совпадает с тем, что уже импортирует Unity:
  44100 Hz, моно, PCM 16 бит, пик 0.85. .meta-файлы не пересоздаются, GUID-ы
  остаются теми же, поэтому ссылки в сценах продолжают работать.

Запуск:  python3 Tools/audio_tools/import_meows.py
Требуется: numpy + soundfile (libsndfile умеет mp3).
"""
from __future__ import annotations

import os
import sys
import wave

import numpy as np
import soundfile as sf

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC_MP3 = os.path.join(ROOT, "Tools", "audio_tools", "sources", "cat_meow_cc0.mp3")
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio", "Slingshot")
SHORT_OUT = os.path.join(OUT_DIR, "CatMeowShort.wav")
LONG_OUT = os.path.join(OUT_DIR, "CatMeowLong.wav")

SR = 44100
PEAK = 0.85

SHORT_KEEP_S = 0.52        # сколько оставляем от основного «мяу» (сек)
LONG_TARGET_S = 1.75       # итоговая длина большого мяу
LONG_DRY_S = 1.22          # сколько занимает само «мяяяуу» (дальше - хвост зала)
LONG_PITCH_SEMITONES = -6.0  # на сколько ниже тон (минус = ниже/больше)
LONG_THICK_GAIN = 0.42      # громкость второго слоя (утолщение)
LONG_THICK_SEMITONES = -4.0  # второй слой ещё ниже
LONG_THICK_DELAY_S = 0.018
LONG_RELEASE_POS = 0.62     # с какой доли сухого звука начинаем плавно «отпускать»
TAIL_RMS_RATIO = 0.12       # громкость хвоста зала относительно сухого звука
REVERB_LEN_S = 0.55
REVERB_DECAY_S = 0.11


# ----------------------------------------------------------------- утилиты
def load_mono(path: str) -> np.ndarray:
    x, sr = sf.read(path, always_2d=True, dtype="float64")
    if sr != SR:
        x = resample(x, sr, SR)
    return x.mean(axis=1)


def resample(x: np.ndarray, sr_in: int, sr_out: int) -> np.ndarray:
    """Линейная интерполяция по времени: растягивает/сжимает вместе с тоном."""
    n_out = int(round(len(x) * sr_out / sr_in))
    t_in = np.arange(len(x)) / sr_in
    t_out = np.arange(n_out) / sr_out
    return np.interp(t_out, t_in, x)


def time_stretch(x: np.ndarray, factor: float, grain: int = 2048, hop_a: int = 512) -> np.ndarray:
    """Гранулярный OLA: длина x factor, тон не меняется."""
    if abs(factor - 1.0) < 1e-3:
        return x.copy()
    hop_s = max(1, int(round(hop_a * factor)))
    win = np.hanning(grain)
    n_frames = max(1, (len(x) - grain) // hop_a + 1)
    out_len = (n_frames - 1) * hop_s + grain
    out = np.zeros(out_len)
    acc = np.zeros(out_len)
    for i in range(n_frames):
        seg = x[i * hop_a: i * hop_a + grain] * win
        if len(seg) < grain:
            seg = np.pad(seg, (0, grain - len(seg)))
        s = i * hop_s
        out[s: s + grain] += seg
        acc[s: s + grain] += win
    acc[acc < 1e-6] = 1.0
    return out / acc


def trim(x: np.ndarray, thresh_rel: float = 0.06, pad_s: float = 0.012) -> np.ndarray:
    """Обрезает тишину по краям."""
    a = np.abs(x)
    if a.max() < 1e-6:
        return x
    idx = np.where(a > a.max() * thresh_rel)[0]
    pad = int(pad_s * SR)
    lo = max(0, idx[0] - pad)
    hi = min(len(x), idx[-1] + pad)
    return x[lo:hi]


def fade_edges(x: np.ndarray, fade_in_s: float, fade_out_s: float) -> np.ndarray:
    y = x.copy()
    n_in = min(int(fade_in_s * SR), len(y))
    n_out = min(int(fade_out_s * SR), len(y))
    if n_in:
        y[:n_in] *= np.linspace(0.0, 1.0, n_in)
    if n_out:
        y[-n_out:] *= np.linspace(1.0, 0.0, n_out)
    return y


def normalize(x: np.ndarray, peak: float = PEAK) -> np.ndarray:
    m = np.abs(x).max()
    return x * (peak / m) if m > 1e-9 else x


def resample_len(x: np.ndarray, n_out: int) -> np.ndarray:
    if n_out <= 0:
        return x
    t_in = np.arange(len(x)) / len(x)
    t_out = np.arange(n_out) / n_out
    return np.interp(t_out, t_in, x)


def reverb_wet(x: np.ndarray, length_s: float = REVERB_LEN_S,
               decay_s: float = REVERB_DECAY_S, seed: int = 7) -> np.ndarray:
    """Свёртка с затухающим шумом (DC-усиление 1): влажный сигнал целиком.

    Складывается с сухим с начала же - тогда нет стыка «звук/послезвучие»,
    а хвост зала естественно продолжается после конца самого мяу.
    """
    rng = np.random.default_rng(seed)
    n = int(length_s * SR)
    ir = rng.normal(0.0, 1.0, n) * np.exp(-np.arange(n) / (decay_s * SR))
    ir /= np.abs(ir).sum()
    return np.convolve(x, ir)[: int(LONG_TARGET_S * SR)]


def release_env(n: int, pos: float) -> np.ndarray:
    """Плавный сход к нулю от доли pos до конца (косинус)."""
    env = np.ones(n)
    start = int(n * pos)
    m = n - start
    if m > 1:
        env[start:] = 0.5 * (1.0 + np.cos(np.linspace(0.0, np.pi, m)))
    return env


def f0_track(x: np.ndarray, step_s: float = 0.05, win_s: float = 0.04) -> list[tuple[float, float]]:
    """Грубая оценка основного тона по автокорреляции (для отчёта)."""
    out = []
    win = int(win_s * SR)
    for i in range(0, max(1, len(x) - win), int(step_s * SR)):
        seg = x[i:i + win]
        if len(seg) < win // 2 or np.abs(seg).max() < 0.01:
            continue
        seg = seg - seg.mean()
        c = np.correlate(seg, seg, "full")[len(seg) - 1:]
        lo, hi = int(SR / 1200), int(SR / 80)
        if hi >= len(c):
            continue
        k = int(np.argmax(c[lo:hi])) + lo
        out.append((i / SR, SR / k))
    return out


def save(path: str, x: np.ndarray) -> None:
    y = np.clip(x, -1.0, 1.0)
    pcm = (y * 32767.0).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"  -> {os.path.relpath(path, ROOT)}  {len(pcm) / SR:.2f} s  peak {np.abs(y).max():.2f}")


def main() -> int:
    if not os.path.exists(SRC_MP3):
        print("Нет исходника:", SRC_MP3, file=sys.stderr)
        return 1
    raw = load_mono(SRC_MP3)
    print(f"исходник: {len(raw) / SR:.2f} s при {SR} Hz")

    # --- короткое мяу: как записано, только обрезка тишины и нормализация ---
    meow = trim(raw)
    if len(meow) > int(SHORT_KEEP_S * SR):
        meow = meow[: int(SHORT_KEEP_S * SR)]
    short = fade_edges(meow, 0.004, 0.06)
    short = normalize(short)
    print("CatMeowShort (реальная запись, обрезка + нормализация):")
    save(SHORT_OUT, short)

    # --- большое мяу: тон ниже, длиннее, с утолщением и залом ---
    p = 2.0 ** (LONG_PITCH_SEMITONES / 12.0)                 # коэффициент тона
    pitched = resample_len(meow, int(round(len(meow) / p)))  # ниже тон, длиннее
    stretch = (LONG_DRY_S * SR) / len(pitched)
    dry = time_stretch(pitched, stretch)

    p2 = 2.0 ** ((LONG_PITCH_SEMITONES + LONG_THICK_SEMITONES) / 12.0)
    thick = resample_len(meow, int(round(len(meow) / p2)))   # слой ещё ниже
    thick = resample_len(thick, len(dry))
    d = int(LONG_THICK_DELAY_S * SR)
    thick = np.concatenate([np.zeros(d), thick[: len(dry) - d]])
    dry = dry + LONG_THICK_GAIN * thick
    dry = dry * release_env(len(dry), LONG_RELEASE_POS)

    wet = reverb_wet(dry)
    tail_part = wet[len(dry):]           # измеряем громкость по «послезвучию»
    wet *= (TAIL_RMS_RATIO * np.sqrt((dry ** 2).mean())) / max(np.sqrt((tail_part ** 2).mean()), 1e-9)
    long = np.zeros(max(len(dry), len(wet)))
    long[: len(dry)] += dry
    long[: len(wet)] += wet
    want = int(LONG_TARGET_S * SR)
    long = long[:want] if len(long) > want else np.pad(long, (0, want - len(long)))
    long = fade_edges(long, 0.006, 0.10)
    long = normalize(long)
    print(f"CatMeowLong (тон {LONG_PITCH_SEMITONES:+.0f} пт + слой {LONG_THICK_SEMITONES:+.0f} пт, "
          f"растяжка x{stretch:.2f}, зал):")
    save(LONG_OUT, long)

    for label, sig in (("short", short), ("long", long)):
        tr = f0_track(sig)
        if tr:
            f0s = [f for _, f in tr]
            span = f" | f0 {tr[0][1]:.0f} -> {tr[-1][1]:.0f} Hz (мин {min(f0s):.0f} макс {max(f0s):.0f})"
        else:
            span = ""
        print(f"  {label}: {len(sig) / SR:.2f} s  RMS {np.sqrt((sig ** 2).mean()):.3f}{span}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
