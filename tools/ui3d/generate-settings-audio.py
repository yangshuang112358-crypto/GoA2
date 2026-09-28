"""Original deterministic stone/bronze UI cues. No sampled game assets."""
import math
from pathlib import Path
import random
import struct
import wave

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'unity/Assets/Scripts/UI3D/Resources/UI3D/Audio'
OUT.mkdir(parents=True, exist_ok=True)
RATE = 48000
for index, (name, duration) in enumerate([('enter', .24), ('leave', .20), ('open', .52)]):
    rng = random.Random(932 + index)
    samples = []
    low = 0.
    for i in range(round(duration * RATE)):
        t = i / RATE
        noise = rng.uniform(-1, 1)
        low += .08 * (noise - low)
        attack = min(1., t / .006)
        tail = min(1., (duration - t) / .04)
        if name == 'open':
            body = .36 * math.sin(2 * math.pi * (135*t-42*t*t)) * math.exp(-t*20)
            stone = low * .70 * math.exp(-t*12)
            bronze = sum(math.sin(2*math.pi*f*t)*math.exp(-t*d)*a for f,d,a in [(620,9,.08),(1027,13,.05),(1489,18,.025)])
            click = noise * .13 * math.exp(-t*160)
            value = body + stone + bronze + click
        else:
            rising = name == 'enter'
            pitch = 810 if rising else 570
            direction = 470 if rising else -380
            body = .11 * math.sin(2*math.pi*(pitch*t+direction*t*t)) * math.exp(-t*18)
            value = body + low*.37*math.exp(-t*16) + noise*.045*math.exp(-t*55)
        samples.append(max(-.95, min(.95, value * attack * tail)))
    with wave.open(str(OUT/f'settings-{name}.wav'), 'wb') as f:
        f.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        f.writeframes(b''.join(struct.pack('<h', round(v*32767)) for v in samples))
    print(name, len(samples), 'samples', 'peak', round(max(map(abs,samples)),3))
