#!/usr/bin/env python3
"""
Generates the sound effects of the BPG project:

  Assets/Resources/Audio/Slingshot/SlingshotStretch.wav  (seamless loop)
  Assets/Resources/Audio/Slingshot/SlingshotShot.wav     (one shot)
  Assets/Resources/Audio/Victory/Victory1Stars.wav       (level cleared, 1 star)
  Assets/Resources/Audio/Victory/Victory2Stars.wav       (level cleared, 2 stars)
  Assets/Resources/Audio/Victory/Victory3Stars.wav       (level cleared, 3 stars)

The three victory jingles are deliberately different: the better the run, the
longer and brighter the fanfare (one short call, a two note answer, or a full
rising arpeggio with a chord and sparkles).

The clips are synthesized from scratch (no third party samples): the stretch is
a rubber-band creak that rises with the pull, the shot is a snap + twang with a
short whoosh of the released shell. Only the Python standard library is used,
the .meta files are written once and then belong to Unity.
"""
import math
import os
import random
import struct
import sys
import wave

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT_DIR = os.path.join(ROOT, 'Assets/Resources/Audio/Slingshot')
VICTORY_DIR = os.path.join(ROOT, 'Assets/Resources/Audio/Victory')
SAMPLE_RATE = 44100

META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: {load_type}
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 0
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
    loadInBackground: 0
    ambisonic: 0
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  loadInBackground: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


# ------------------------------------------------------------------ helpers ----
def one_pole_low_pass(samples, cutoff):
    alpha = 1.0 - math.exp(-2.0 * math.pi * cutoff / SAMPLE_RATE)
    out = []
    previous = 0.0
    for sample in samples:
        previous += alpha * (sample - previous)
        out.append(previous)
    return out


def one_pole_high_pass(samples, cutoff):
    alpha = 1.0 - math.exp(-2.0 * math.pi * cutoff / SAMPLE_RATE)
    out = []
    previous_in = 0.0
    previous_out = 0.0
    for sample in samples:
        previous_out = alpha * (previous_out + sample - previous_in)
        previous_in = sample
        out.append(previous_out)
    return out


def band_pass(samples, cutoff, q=1.2):
    """State variable filter, cutoff may be a constant or a per-sample list."""
    out = []
    low = 0.0
    band = 0.0
    damping = 1.0 / max(0.1, q)
    for index, sample in enumerate(samples):
        frequency = cutoff[index] if isinstance(cutoff, list) else cutoff
        f = 2.0 * math.sin(math.pi * min(0.24, frequency / SAMPLE_RATE))
        high = sample - low - damping * band
        band += f * high
        low += f * band
        out.append(band)
    return out


def noise(count, seed):
    generator = random.Random(seed)
    return [generator.gauss(0.0, 1.0) for _ in range(count)]


def normalize(samples, peak=0.92):
    current = max(abs(sample) for sample in samples) or 1.0
    scale = peak / current
    return [sample * scale for sample in samples]


def fade_edges(samples, fade_in=0.004, fade_out=0.05):
    for i, sample in enumerate(samples):
        gain = 1.0
        if fade_in > 0 and i < fade_in * SAMPLE_RATE:
            gain = min(gain, i / (fade_in * SAMPLE_RATE))
        remaining = len(samples) - 1 - i
        if fade_out > 0 and remaining < fade_out * SAMPLE_RATE:
            gain = min(gain, remaining / (fade_out * SAMPLE_RATE))
        samples[i] = sample * gain
    return samples


def write_wav(path, samples):
    frames = bytearray()
    for sample in samples:
        value = int(max(-1.0, min(1.0, sample)) * 32767)
        frames += struct.pack('<h', value)
    with wave.open(path, 'wb') as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        handle.writeframes(bytes(frames))


def stats(name, samples):
    peak = max(abs(sample) for sample in samples)
    rms = math.sqrt(sum(sample * sample for sample in samples) / len(samples))
    print('%-22s %5.2f s  peak %.3f  rms %.3f' %
          (name, len(samples) / SAMPLE_RATE, peak, rms))
    buckets = 24
    size = max(1, len(samples) // buckets)
    bars = []
    for bucket in range(buckets):
        chunk = samples[bucket * size:(bucket + 1) * size]
        level = math.sqrt(sum(sample * sample for sample in chunk) / max(1, len(chunk)))
        bars.append('#' * int(round(level * 60 / max(0.0001, peak))))
    for index, bar in enumerate(bars):
        print('   %5.2fs %s' % (index * size / SAMPLE_RATE, bar))


def add_tone(buffer, start, length, frequency, decay, amplitude,
             harmonics=((1.0, 1.0), (2.0, 0.38), (3.0, 0.16), (4.0, 0.07)),
             attack=0.006):
    """Adds an exponentially decaying additive tone into `buffer`, in place."""
    offset = int(start * SAMPLE_RATE)
    size = int(length * SAMPLE_RATE)
    attack = max(1e-4, attack)
    for index in range(size):
        position = offset + index
        if position >= len(buffer):
            return
        t = index / SAMPLE_RATE
        envelope = (1.0 - math.exp(-t / attack)) * math.exp(-t / decay)
        value = 0.0
        for multiplier, gain in harmonics:
            frequency_component = frequency * multiplier
            if frequency_component >= SAMPLE_RATE * 0.45:
                continue
            value += math.sin(2.0 * math.pi * frequency_component * t) * gain
        buffer[position] += value * envelope * amplitude


def add_sparkle(buffer, start, frequency, amplitude=0.25, decay=0.09):
    """A tiny high bell used as confetti at the end of the jingles."""
    add_tone(buffer, start, decay * 4.0, frequency, decay, amplitude,
             harmonics=((1.0, 1.0), (2.7, 0.35), (4.1, 0.15)), attack=0.002)


def add_shimmer(buffer, start, duration=0.28, amplitude=0.16, cutoff=7000.0):
    """Short bright noise burst, the cartoon "tada" air."""
    offset = int(start * SAMPLE_RATE)
    size = int(duration * SAMPLE_RATE)
    hiss = one_pole_high_pass(noise(size, int(start * 1000) + 7), cutoff)
    for index in range(size):
        position = offset + index
        if position >= len(buffer):
            return
        t = index / SAMPLE_RATE
        envelope = math.exp(-t / (duration * 0.35)) * (1.0 - math.exp(-t / 0.004))
        buffer[position] += hiss[index] * envelope * amplitude


NOTE = {
    'C4': 261.63, 'E4': 329.63, 'G4': 392.00, 'A4': 440.00, 'C5': 523.25,
    'D5': 587.33, 'E5': 659.25, 'G5': 783.99, 'A5': 880.00, 'C6': 1046.50,
    'E6': 1318.51, 'G6': 1567.98, 'C7': 2093.00,
}


def build_victory(stars):
    """
    Victory jingle: 3 stars is a full rising fanfare with a chord and sparkles,
    2 stars a shorter two note answer, 1 star a single modest call.
    """
    if stars >= 3:
        duration, peak, sparkles = 1.75, 0.95, (('C7', 0.56), ('G6', 0.68), ('C7', 0.80))
    elif stars == 2:
        duration, peak, sparkles = 1.20, 0.90, (('E6', 0.40), ('C7', 0.56))
    else:
        duration, peak, sparkles = 0.95, 0.85, ()

    buffer = [0.0] * int(SAMPLE_RATE * duration)

    if stars >= 3:
        for index, note in enumerate(('C5', 'E5', 'G5')):
            add_tone(buffer, 0.02 + 0.13 * index, 0.5, NOTE[note], 0.16, 0.5)
        add_shimmer(buffer, 0.48)
        for note, gain in (('C5', 0.45), ('E5', 0.35), ('G5', 0.32), ('C6', 0.5)):
            add_tone(buffer, 0.46, 1.3, NOTE[note], 0.75, gain)
        add_tone(buffer, 0.46, 1.1, NOTE['C4'], 0.55, 0.55)
    elif stars == 2:
        for index, note in enumerate(('C5', 'G5')):
            add_tone(buffer, 0.02 + 0.16 * index, 0.55, NOTE[note], 0.19, 0.55)
        add_tone(buffer, 0.38, 0.9, NOTE['E5'], 0.5, 0.4)
        add_tone(buffer, 0.38, 0.9, NOTE['C5'], 0.5, 0.35)
    else:
        add_tone(buffer, 0.02, 0.6, NOTE['C5'], 0.22, 0.55)
        add_tone(buffer, 0.24, 0.9, NOTE['G4'], 0.45, 0.45)
        add_tone(buffer, 0.24, 0.9, NOTE['E4'], 0.45, 0.3)

    for note, when in sparkles:
        add_sparkle(buffer, when, NOTE[note], 0.22 if stars >= 3 else 0.18)

    buffer = one_pole_low_pass(buffer, 12000.0)
    buffer = normalize(buffer, peak)
    return fade_edges(buffer, fade_in=0.002, fade_out=0.12)


# ------------------------------------------------------------------- sounds ----
def build_stretch(duration=1.6):
    """Rubber-band creak that loops seamlessly: creaks + rising tension."""
    count = int(SAMPLE_RATE * duration)
    time = [i / SAMPLE_RATE for i in range(count)]

    # Creak: band filtered noise with a periodic "grip and slip" envelope, so
    # the clip can repeat without a seam.
    slips = 5.0
    slip_length = 0.055
    envelope = []
    for t in time:
        phase = (t * slips / duration) % 1.0
        distance = min(phase, 1.0 - phase) / 1.0
        slip = math.exp(-((distance * duration / slips) / slip_length) ** 2)
        envelope.append(slip)

    rasp = [math.sin(2.0 * math.pi * 31.0 * t) * 0.45 + 0.55 for t in time]
    sweep = [820.0 + 380.0 * math.sin(2.0 * math.pi * t / duration) for t in time]
    creak = band_pass(noise(count, 11), sweep, q=2.6)
    creak = [creak[i] * envelope[i] * rasp[i] * 0.9 for i in range(count)]

    # Base hiss of the stretched rubber, barely audible but it glues the creaks.
    hiss = one_pole_high_pass(noise(count, 12), 1600.0)
    hiss = [sample * 0.06 * (0.6 + 0.4 * math.sin(2.0 * math.pi * 1.0 * t / duration))
            for sample, t in zip(hiss, time)]

    # Tension tone: two low harmonics that wobble, both periodic per loop.
    tension = []
    for t in time:
        wobble = 0.5 + 0.5 * math.sin(2.0 * math.pi * 2.0 * t / duration)
        tone = math.sin(2.0 * math.pi * 96.0 * t) * 0.8
        tone += math.sin(2.0 * math.pi * 143.0 * t) * 0.35
        tension.append(tone * 0.10 * (0.55 + 0.45 * wobble))

    mixed = [creak[i] + hiss[i] + tension[i] for i in range(count)]
    mixed = one_pole_low_pass(mixed, 5200.0)
    mixed = normalize(mixed, 0.85)

    # Crossfade the tail into the head: the result loops without a click.
    seam = int(0.03 * SAMPLE_RATE)
    for i in range(seam):
        weight = i / seam
        mixed[i] = mixed[i] * weight + mixed[count - seam + i] * (1.0 - weight)
    return mixed[:count - seam]


def build_shot(duration=0.62):
    """Release of the slingshot: snap, rubber twang, shell whoosh, low thump."""
    count = int(SAMPLE_RATE * duration)
    time = [i / SAMPLE_RATE for i in range(count)]

    # 1. Snap of the band leaving the pouch.
    snap = one_pole_high_pass(noise(count, 21), 900.0)
    snap = [sample * math.exp(-t / 0.013) * 0.42 for sample, t in zip(snap, time)]

    # 2. Twang: fast downward sweep, the "boing" of the rubber.
    twang = []
    phase = 0.0
    for t in time:
        frequency = 150.0 + 980.0 * math.exp(-t / 0.075)
        phase += 2.0 * math.pi * frequency / SAMPLE_RATE
        twang.append(math.sin(phase) * math.exp(-t / 0.105) * 1.05)

    # 3. Whoosh of the shell flying away: noise with a falling band.
    sweep = [2600.0 * math.exp(-t / 0.11) + 420.0 for t in time]
    whoosh = band_pass(noise(count, 22), sweep, q=1.1)
    whoosh = [sample * (1.0 - math.exp(-t / 0.02)) * math.exp(-t / 0.17) * 0.52
              for sample, t in zip(whoosh, time)]

    # 4. Low thump for weight.
    thump = []
    phase = 0.0
    for t in time:
        frequency = 82.0 * math.exp(-t / 0.09) + 54.0
        phase += 2.0 * math.pi * frequency / SAMPLE_RATE
        thump.append(math.sin(phase) * math.exp(-t / 0.075) * 0.7)

    slap = []
    phase = 0.0
    for t in time:
        delayed = max(0.0, t - 0.055)
        frequency = 620.0 * math.exp(-delayed / 0.05) + 210.0
        phase += 2.0 * math.pi * frequency / SAMPLE_RATE
        slap.append(math.sin(phase) * math.exp(-delayed / 0.05) * (0.28 if t >= 0.055 else 0.0))

    mixed = [snap[i] + twang[i] + whoosh[i] + thump[i] + slap[i] for i in range(count)]
    # Soft clip glues the layers together instead of letting them sum to a click.
    mixed = [math.tanh(sample * 1.25) * 0.85 for sample in mixed]
    mixed = one_pole_low_pass(mixed, 9000.0)
    mixed = normalize(mixed, 0.94)
    return fade_edges(mixed, fade_in=0.0015, fade_out=0.08)


# -------------------------------------------------------------------- output ----
def new_guid(existing):
    import secrets
    while True:
        guid = secrets.token_hex(16)
        if guid not in existing:
            existing.add(guid)
            return guid


def collect_guids():
    guids = set()
    for root, dirs, files in os.walk(os.path.join(ROOT, 'Assets')):
        for name in files:
            if not name.endswith('.meta'):
                continue
            with open(os.path.join(root, name), encoding='utf-8', errors='ignore') as handle:
                for line in handle:
                    if line.startswith('guid:'):
                        guids.add(line.split(':', 1)[1].strip())
                        break
    return guids


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(VICTORY_DIR, exist_ok=True)
    guids = collect_guids()

    clips = [
        (OUT_DIR, 'SlingshotStretch.wav', build_stretch(), 0),
        (OUT_DIR, 'SlingshotShot.wav', build_shot(), 0),
        (VICTORY_DIR, 'Victory1Stars.wav', build_victory(1), 0),
        (VICTORY_DIR, 'Victory2Stars.wav', build_victory(2), 0),
        (VICTORY_DIR, 'Victory3Stars.wav', build_victory(3), 0),
    ]

    for directory, name, samples, load_type in clips:
        path = os.path.join(directory, name)
        write_wav(path, samples)
        stats(name, samples)
        meta_path = path + '.meta'
        if not os.path.exists(meta_path):
            with open(meta_path, 'w', encoding='utf-8', newline='\n') as handle:
                handle.write(META_TEMPLATE.format(guid=new_guid(guids), load_type=load_type))
            print('  wrote', os.path.relpath(meta_path, ROOT))
        print('  wrote', os.path.relpath(path, ROOT), os.path.getsize(path), 'bytes')


if __name__ == '__main__':
    sys.exit(main())
