using UnityEngine;

namespace Goa2.Presentation.UI3D
{
    // One source per persistent game screen. UI rebuilds and rule changes never restart it.
    public sealed class BackgroundMusic : MonoBehaviour
    {
        public static float Volume
        {
            get => PlayerPrefs.GetFloat("goa-music-volume", .28f);
            set { PlayerPrefs.SetFloat("goa-music-volume", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        private AudioSource source;
        private AudioSource draftSource;
        private bool draft;
        public void SetDraft(bool value){draft=value;if(draft && draftSource!=null && !draftSource.isPlaying)draftSource.Play();}

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0;
            source.volume = 0;
            source.clip = Resources.Load<AudioClip>("UI3D/Audio/atlantean-dusk");
            draftSource=gameObject.AddComponent<AudioSource>();draftSource.playOnAwake=false;draftSource.loop=true;draftSource.spatialBlend=0;draftSource.volume=0;draftSource.clip=Resources.Load<AudioClip>("UI3D/Audio/atlantean-draft");
            if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            if (source.clip == null) Debug.LogWarning("Background music asset was not found.");
        }

        private void OnEnable() { if (source != null && source.clip != null) source.Play(); }
        private void Update()
        {
            // Muting is immediate; other volume changes ease in without abrupt jumps.
            source.volume = Volume <= 0 ? 0 : Mathf.MoveTowards(source.volume, draft?0:Volume, Time.unscaledDeltaTime * .35f);
            draftSource.volume=Volume<=0?0:Mathf.MoveTowards(draftSource.volume,draft?Volume:0,Time.unscaledDeltaTime*.35f);
            if(!draft && draftSource.volume<=0 && draftSource.isPlaying)draftSource.Stop();
        }
        private void OnDisable() { if (source != null) source.Stop();if(draftSource!=null)draftSource.Stop(); }
    }
}
