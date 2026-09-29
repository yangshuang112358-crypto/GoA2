using System;
using System.Collections.Generic;
using UnityEngine;
namespace Goa2.Presentation.UI3D
{
    public sealed class ActionSequenceAudio : MonoBehaviour
    {
        private AudioSource source;
        private readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        private float last=-10;
        public static float Duration(string cue)=>cue=="tick"?.07f:cue=="move"?.18f:cue=="drop"?.22f:.13f;
        private void Awake(){source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.volume=.65f;if(FindFirstObjectByType<AudioListener>()==null)gameObject.AddComponent<AudioListener>();}
        private void Update(){source.volume=.65f*InteractionAudio.Volume;}
        public void Play(string cue)
        {
            if(Time.realtimeSinceStartup-last<.065f)return;last=Time.realtimeSinceStartup;
            if(!clips.TryGetValue(cue,out var clip))
            {
                int rate=44100;float duration=Duration(cue);var data=new float[(int)(rate*duration)];var rng=new System.Random(319);
                for(int i=0;i<data.Length;i++)
                {
                    float t=(float)i/rate,phase=t%.034f;
                    float tick=(Mathf.Sin(phase*2*Mathf.PI*1700)*.16f+(float)(rng.NextDouble()*2-1)*.19f)*Mathf.Exp(-phase*155);
                    float tail=Mathf.Clamp01((duration-t)/.035f);data[i]=(tick+Mathf.Sin(t*2*Mathf.PI*115)*.07f*Mathf.Exp(-t*24))*tail;
                }
                clip=AudioClip.Create("action-"+cue,data.Length,1,rate,false);clip.SetData(data,0);clips[cue]=clip;
            }
            source.volume=.65f*InteractionAudio.Volume;source.PlayOneShot(clip);
        }
        private void OnDestroy(){foreach(var c in clips.Values)Destroy(c);}
    }
}
