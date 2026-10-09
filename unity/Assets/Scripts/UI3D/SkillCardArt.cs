#nullable enable
using System;
using System.Collections.Generic;
using Goa2.Domain;
using UnityEngine;

namespace Goa2.Presentation.UI3D
{
    // Presentation-only, explicit card-to-art mapping. It never selects cards or
    // reads a hidden hand. The caller supplies an already-authorized definition.
    public static class SkillCardArt
    {
        [Serializable] public sealed class Entry { public string CardId=""; public string IconKey=""; public string NameKey=""; }
        [Serializable] private sealed class Library { public Entry[] Entries=Array.Empty<Entry>(); }
        private static Dictionary<string,Entry>? entries;
        private static readonly Dictionary<string,Texture2D?> textures=new Dictionary<string,Texture2D?>();
        private static void Load(){
            if(entries!=null)return;entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
            foreach(var hero in new[]{"wasp","shargatha","brogan","arien","tigerclaw","sabina"}){
                var source=Resources.Load<TextAsset>("UI3D/CardArt/"+hero+"/manifest");if(source==null)continue;
                foreach(var entry in JsonUtility.FromJson<Library>(source.text).Entries)entries.Add(entry.CardId,entry);
            }
        }
        public static Entry? Find(CardDefinition? card){Load();return card!=null && entries!.TryGetValue(card.Id,out var entry)?entry:null;}
        public static Texture2D? Texture(string path){if(!textures.TryGetValue(path,out var texture))textures[path]=texture=Resources.Load<Texture2D>("UI3D/CardArt/"+path);return texture;}
        public static int TierCount(CardDefinition? card)=>Mathf.Clamp(card?.Level??0,0,4);
    }
}
