using System;
using System.Collections.Generic;
using UnityEngine;
namespace Goa2.Presentation.UI3D
{
    // Presentation only: deterministic sound synthesis never touches the rules RNG.
    public sealed class InteractionAudio : MonoBehaviour
    {
        public static readonly string[] Cues={"hover","select","cancel","open","close","target","confirm","reject","response","turn","planning","victory"};
        public static float Volume { get=>PlayerPrefs.GetFloat("goa-sfx-volume",.65f); set {PlayerPrefs.SetFloat("goa-sfx-volume",Mathf.Clamp01(value));PlayerPrefs.Save();} }
        public static event Action<string> Played;
        private AudioSource source;
        private readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        private readonly Dictionary<string,float> times=new Dictionary<string,float>();
        public static float Duration(string cue)=>cue=="victory"?.7f:cue=="response"?.34f:cue=="planning"?.3f:cue=="hover"?.045f:.14f;
        public static float[] Samples(string cue,int rate=44100)
        {
            float duration=Duration(cue);var data=new float[(int)(duration*rate)];var rng=new System.Random(873);
            float frequency=cue=="hover"?1250:cue=="target"?700:cue=="reject"?150:cue=="cancel"||cue=="close"?300:510;
            for(int i=0;i<data.Length;i++) {
                float t=(float)i/rate,x=t/duration;
                float envelope=Mathf.Min(1,t/.006f)*Mathf.Pow(1-x,2);
                float sweep=cue=="cancel"||cue=="close"?-170:140;
                float phase=2*Mathf.PI*(frequency*t+sweep*t*t/duration/2);
                float tone=Mathf.Sin(phase)+.3f*Mathf.Sin(phase*2.01f);
                if(cue=="confirm"||cue=="turn"||cue=="response"||cue=="victory"||cue=="planning")
                    tone+=.55f*Mathf.Sin(2*Mathf.PI*frequency*1.5f*t)+.3f*Mathf.Sin(2*Mathf.PI*frequency*2*t);
                float contact=(float)(rng.NextDouble()*2-1)*Mathf.Exp(-t*100)*.3f;
                data[i]=Mathf.Clamp((tone*.17f+contact)*envelope,-.7f,.7f);
            }
            return data;
        }
        private void Awake(){source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.volume=Volume;if(FindFirstObjectByType<AudioListener>()==null)gameObject.AddComponent<AudioListener>();}
        private void Update(){source.volume=Volume;}
        public void Play(string cue) {
            if(Array.IndexOf(Cues,cue)<0 || Volume<=0)return;
            float now=Time.realtimeSinceStartup;
            if(times.TryGetValue(cue,out var last) && now-last<(cue=="hover"?.12f:.065f))return;
            times[cue]=now;
            if(!clips.TryGetValue(cue,out var clip)){var data=Samples(cue);clip=AudioClip.Create("interaction-"+cue,data.Length,1,44100,false);clip.SetData(data,0);clips[cue]=clip;}
            source.volume=Volume;source.PlayOneShot(clip,cue=="hover"?.32f:.8f);Played?.Invoke(cue);
        }
        private void OnDestroy(){foreach(var clip in clips.Values)Destroy(clip);}
    }
}
