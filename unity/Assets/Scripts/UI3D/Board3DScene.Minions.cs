#nullable enable
using System;
using System.Collections.Generic;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Goa2.Presentation.UI3D
{
    public sealed partial class Board3DScene
    {
        private readonly Dictionary<string, Material> minionMaterials = new Dictionary<string, Material>();
        public int ModeledMinionCount { get; private set; }
        public static string MinionAsset(string kind) => kind == "heavy" ? "Heavy" : kind == "ranged" ? "Ranged" : "Melee";
        public static float MinionHeight(string kind) => HeroHeight * .8f;

        private bool BuildMinion(string kind, Team team, Hex cell, string id)
        {
            var model = Resources.Load<GameObject>("UI3D/Minions/" + MinionAsset(kind));
            if (model == null) return false; // Readable token fallback if an asset is missing.
            var instance = Object.Instantiate(model, host.transform, false);
            instance.name = "minion " + kind + " " + id;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = Layer;
                child.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { Destroy(instance); return false; }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float height = MinionHeight(kind);
            float scale = height / Mathf.Max(.001f, bounds.size.y);
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localRotation = Quaternion.Euler(0, team == Team.Blue ? 0 : 180, 0);
            var bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            instance.transform.localPosition = Board3DGeometry.World(cell, .04f) - instance.transform.localRotation * (bottomCenter * scale);
            foreach (var renderer in renderers)
            {
                var source = renderer.sharedMaterials;
                var palette = new Material[source.Length];
                for (int i = 0; i < source.Length; i++) palette[i] = MinionMaterial(source[i] == null ? "Steel" : source[i].name, team);
                renderer.sharedMaterials = palette;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            RegisterMinionRig(instance,id,kind);
            ModeledMinionCount++;
            return true;
        }

        private Material MinionMaterial(string sourceName, Team team)
        {
            // Match semantic slots; FBX importer may append a material suffix.
            string slot = sourceName.Split(' ')[0];
            string key = team + ":" + slot;
            if (minionMaterials.TryGetValue(key, out var cached)) return cached;
            string html;
            switch (slot)
            {
                case "TeamCloth": html = team == Team.Blue ? "#367CAA" : "#AC505A"; break;
                case "TeamInset": html = team == Team.Blue ? "#65BDE1" : "#E08B70"; break;
                case "SteelLight": html = "#A1B2BB"; break;
                case "Bronze": html = "#B18A4B"; break;
                case "Leather": html = "#493E3B"; break;
                case "Shadow": html = "#101E2D"; break;
                case "Glow": html = "#B4EEFF"; break;
                case "Parchment": html = "#C9B68D"; break;
                default: html = "#586B80"; break;
            }
            var shader = Resources.Load<Shader>("UI3D/Minion");
            if (shader == null) throw new InvalidOperationException("Missing minion presentation shader");
            var material = Own(new Material(shader) { name = "Minion " + key, color = ColorOf(html) });
            material.SetFloat("_Metallic", slot == "Bronze" || slot.StartsWith("Steel", StringComparison.Ordinal) ? .65f : .08f);
            material.SetFloat("_Emission", slot == "Glow" ? .38f : 0);
            minionMaterials.Add(key, material);
            return material;
        }
    }
}
