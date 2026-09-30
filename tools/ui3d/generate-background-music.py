"""Original, deterministic ambient sketch; no sampled or external recordings.

Produces a seamless 16-bar stereo loop at 72 BPM, for the Goa2V1 prototype.
Usage: python tools/ui3d/generate-background-music.py output.wav
"""
import sys
import wave
from pathlib import Path

import numpy as np

RATE = 44100
BEAT = 60 / 72
BAR = BEAT * 4
LENGTH = round(16 * BAR * RATE)
mix = np.zeros((LENGTH, 2), dtype=np.float64)


def note(midi, start, duration, gain, pan=0, pluck=False):
    t = np.arange(round(duration * RATE)) / RATE
    frequency = 440 * 2 ** ((midi - 69) / 12)
    attack = .016 if pluck else .8
    envelope = np.minimum(t / attack, 1) * np.minimum((duration - t) / .8, 1)
    if pluck:
        envelope *= np.exp(-t * 1.7)
    tone = (np.sin(2 * np.pi * frequency * t)
            + .22 * np.sin(2 * np.pi * frequency * 2 * t)
            + .07 * np.sin(2 * np.pi * frequency * 3 * t))
    if not pluck:
        tone += .18 * np.sin(2 * np.pi * frequency * 1.0015 * t)
    mono = tone * envelope * gain
    indices = (np.arange(len(t)) + round(start * RATE)) % LENGTH
    mix[indices, 0] += mono * np.sqrt((1 - pan) / 2)
    mix[indices, 1] += mono * np.sqrt((1 + pan) / 2)
    # Quiet stereo echoes wrap around the loop boundary as well.
    if pluck:
        for delay, level in ((.375, .19), (.75, .09)):
            shifted = (indices + round(delay * RATE)) % LENGTH
            mix[shifted, 0] += mono * level * np.sqrt((1 + pan) / 2)
            mix[shifted, 1] += mono * level * np.sqrt((1 - pan) / 2)


chords = [(50, 57, 60, 64), (46, 53, 57, 60), (48, 55, 57, 64), (48, 55, 58, 62)]
melody = [74, 76, 77, 69, 72, 74, 69, 67, 69, 72, 76, 74, 70, 69, 67, 72]
for bar in range(16):
    chord = chords[(bar // 2) % 4]
    for i, pitch in enumerate(chord):
        note(pitch, bar * BAR, BAR + 1.4, .027, (i - 1.5) * .28)
    for beat in (0, 1.5, 2.5):
        pitch = chord[(bar + int(beat * 2)) % 4] + 12
        note(pitch, bar * BAR + beat * BEAT, 2.8, .073, (-1 if bar % 2 else 1) * .4, True)
    note(melody[bar], bar * BAR + .5 * BEAT, 3.2, .055, .12, True)

mix -= mix.mean(axis=0)
peak = np.max(np.abs(mix))
mix *= .48 / max(peak, 1e-9)
assert np.isfinite(mix).all()
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(output), 'wb') as wav:
    wav.setnchannels(2)
    wav.setsampwidth(2)
    wav.setframerate(RATE)
    wav.writeframes((mix * 32767).astype('<i2').tobytes())
print(f'{output}: {LENGTH / RATE:.3f}s stereo, peak={np.max(np.abs(mix)):.3f}')
