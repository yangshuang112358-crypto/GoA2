using System;
using UnityEngine;
namespace Goa2.Presentation.UI3D {
 // Owned by the game screen, not by a button rebuilt during Render().
 public sealed class SettingsAudio : MonoBehaviour {
  private AudioSource source;
  private AudioClip enter,leave,open;
  private float lastHover=-10;
  private bool hovering;
  public static event Action<string,float> Played;
  private void Awake() {
   source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.volume=.7f;
   enter=Resources.Load<AudioClip>("UI3D/Audio/settings-enter");
   leave=Resources.Load<AudioClip>("UI3D/Audio/settings-leave");
   open=Resources.Load<AudioClip>("UI3D/Audio/settings-open");
   if(FindFirstObjectByType<AudioListener>()==null)gameObject.AddComponent<AudioListener>();
  }
  public void Play(string cue) {
   // Rebuilding the drawer sends a fresh PointerEnter without a physical re-entry.
   if(cue=="enter"){if(hovering)return;hovering=true;}
   else if(cue=="leave"){if(!hovering)return;hovering=false;}
   if(cue!="open" && Time.realtimeSinceStartup-lastHover<.075f)return;
   var clip=cue=="enter"?enter:cue=="leave"?leave:open;if(clip==null)return;
   if(cue!="open")lastHover=Time.realtimeSinceStartup;
   source.PlayOneShot(clip);Played?.Invoke(cue,Time.realtimeSinceStartup);
  }
 }
}
