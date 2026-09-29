#nullable enable
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private InteractionAudio? interactionAudio;
        private readonly InteractionAudioState audioState=new InteractionAudioState();
        private void Sound(string cue){if(interactionAudio==null)interactionAudio=gameObject.AddComponent<InteractionAudio>();interactionAudio.Play(cue);}
        private UnityEngine.Vector2 lastSkillSoundPosition=new UnityEngine.Vector2(-10000,-10000);
        private void HoverSkillSound(UnityEngine.Vector2 position){if((position-lastSkillSoundPosition).sqrMagnitude<4)return;lastSkillSoundPosition=position;Sound("hover");}
        private void CommandSound(CommandKind kind,bool accepted){Sound(!accepted?"reject":kind==CommandKind.SelectCard?"select":kind==CommandKind.CancelCardSelection?"cancel":"confirm");}
        private void ObserveAudio(){var cue=audioState.Observe(renderedView,seat);if(cue!="")Sound(cue);}
        private void BuildAudioSettings(VisualElement parent)
        {
            var slider=new Slider("交互音量",0,1){name="sfx-volume",value=InteractionAudio.Volume};
            slider.style.minHeight=36;
            slider.RegisterValueChangedCallback(e=>InteractionAudio.Volume=e.newValue);
            parent.Add(slider);
            parent.Add(Button("试听确认音",()=>Sound("confirm"),"quiet-button","sfx-preview"));
        }
    }
}
