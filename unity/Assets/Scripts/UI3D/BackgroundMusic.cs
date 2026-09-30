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

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0;
            source.volume = 0;
            source.clip = Resources.Load<AudioClip>("UI3D/Audio/atlantean-dusk");
            if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            if (source.clip == null) Debug.LogWarning("Background music asset was not found.");
        }

        private void OnEnable() { if (source != null && source.clip != null) source.Play(); }
        private void Update()
        {
            // Muting is immediate; other volume changes ease in without abrupt jumps.
            source.volume = Volume <= 0 ? 0 : Mathf.MoveTowards(source.volume, Volume, Time.unscaledDeltaTime * .35f);
        }
        private void OnDisable() { if (source != null) source.Stop(); }
    }
}
