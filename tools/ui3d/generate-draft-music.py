"""Original 24-second heroic selection loop. No commercial recordings or melodies."""
import sys,wave
from pathlib import Path
import numpy as np
rate=44100;duration=24;n=rate*duration
mix=np.zeros((n,2));rng=np.random.default_rng(710)
def layer(pitch,start,length,gain,pan=0,pluck=False):
    t=np.arange(round(length*rate))/rate;hz=440*2**((pitch-69)/12)
    env=np.minimum(t/(.02 if pluck else .45),1)*np.minimum((length-t)/.55,1)
    if pluck:env*=np.exp(-t*2.5)
    wave=sum(np.sin(2*np.pi*hz*k*t+np.sin(t*5)*.015)/(k*k) for k in range(1,6))
    wave+=.23*np.sin(2*np.pi*hz*1.003*t)
    samples=wave*env*gain;indices=(round(start*rate)+np.arange(len(t)))%n
    mix[indices,0]+=samples*np.sqrt((1-pan)/2);mix[indices,1]+=samples*np.sqrt((1+pan)/2)
for bar,chord in enumerate([(38,45,50,53),(34,41,46,50),(36,43,48,52),(33,40,45,52)]*2):
    start=bar*3
    for j,pitch in enumerate(chord):layer(pitch,start,3.6,.045,(j-1.5)*.25)
    for step in range(8):layer(chord[step%4]+24,start+step*.375,1.4,.05,(-1 if step%2 else 1)*.5,True)
    for beat in [0,1.5]:
        t=np.arange(round(.65*rate))/rate
        drum=np.sin(2*np.pi*(52*t+22*(1-np.exp(-t*14))/14))*np.exp(-t*8)
        drum+=rng.standard_normal(len(t))*.12*np.exp(-t*36)
        idx=(round((start+beat)*rate)+np.arange(len(t)))%n
        mix[idx,:]+=drum[:,None]*.09
for i,pitch in enumerate([62,65,69,67,64,62,60,61]):layer(pitch,i*3+.6,2.8,.05,.12)
mix-=mix.mean(axis=0);mix*=.49/max(abs(mix).max(),1e-8)
path=Path(sys.argv[1]);path.parent.mkdir(parents=True,exist_ok=True)
with wave.open(str(path),'wb') as wav:
    wav.setnchannels(2);wav.setsampwidth(2);wav.setframerate(rate);wav.writeframes((mix*32767).astype('<i2').tobytes())
print(f'{path}: {duration}s original stereo draft loop')
