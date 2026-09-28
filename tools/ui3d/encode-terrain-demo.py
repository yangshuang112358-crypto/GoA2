"""Encode continuous GameView frames, mixing game's WAV cues at logged UI event times.

This is event-synchronized audio, not desktop-loopback or microphone capture.
"""
from array import array
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
import wave

p = argparse.ArgumentParser()
p.add_argument('folder', type=Path)
p.add_argument('--ffmpeg', type=Path)
args = p.parse_args()
root = Path(__file__).resolve().parents[2]
ffmpeg = args.ffmpeg or root/'artifacts/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
duration = float(re.search(r'duration=([\d.]+)', (args.folder/'timing.txt').read_text()).group(1))
rate = 48000
track = array('f', [0]) * (round((duration + .1)*rate))
events = []
for line in (args.folder/'audio-events.tsv').read_text().splitlines():
    when, cue = line.split('\t')
    when = float(when)
    events.append({'seconds': when, 'cue': cue})
    with wave.open(str(root/f'unity/Assets/Scripts/UI3D/Resources/UI3D/Audio/settings-{cue}.wav'), 'rb') as wav:
        assert wav.getframerate() == rate and wav.getnchannels() == 1 and wav.getsampwidth() == 2
        data = array('h', wav.readframes(wav.getnframes()))
        if sys.byteorder != 'little':
            data.byteswap()
    start = round(when*rate)
    for i, value in enumerate(data):
        if start+i < len(track):
            track[start+i] += value/32768*.7
pcm = array('h', (round(max(-.98, min(.98, n))*32767) for n in track))
if sys.byteorder != 'little':
    pcm.byteswap()
with wave.open(str(args.folder/'game-ui-events.wav'), 'wb') as wav:
    wav.setparams((1, 2, rate, 0, 'NONE', 'not compressed'))
    wav.writeframes(pcm.tobytes())
start = max(0, events[0]['seconds']-.6)
filters = (
    '[0:v]split=2[full][detail];[full]scale=1280:800:flags=lanczos[base];'
    '[detail]crop=150:100:iw-155:0,scale=600:400:flags=lanczos,'
    f'pad=608:408:4:4:color=0x93764a,format=rgba,fade=t=in:st={start}:d=0.5:alpha=1[zoom];'
    '[base][zoom]overlay=30:302[out]'
)
output = args.folder/'terrain-ui-mobile.mp4'
subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-y',
                '-i', str(args.folder/'terrain-ui-raw.mp4'), '-i', str(args.folder/'game-ui-events.wav'),
                '-filter_complex', filters, '-map', '[out]', '-map', '1:a:0',
                '-c:v', 'libx264', '-preset', 'medium', '-crf', '22', '-pix_fmt', 'yuv420p',
                '-c:a', 'aac', '-b:a', '128k', '-t', str(duration), '-movflags', '+faststart', str(output)], check=True)
subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(output), '-f', 'null', '-'], check=True)
manifest = {'file': output.name, 'seconds': duration, 'bytes': output.stat().st_size,
            'sha256': hashlib.sha256(output.read_bytes()).hexdigest(),
            'video': 'Actual Unity GameView, continuous single position; scripted input; no cuts. Last section adds fading live settings inset.',
            'audio': 'Original in-game WAV cues mixed at captured SettingsAudio.Played times, volume 0.7; not OS loopback capture.',
            'events': events, 'full_decode_passed': True}
(args.folder/'mobile-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps(manifest, ensure_ascii=False, indent=2))
