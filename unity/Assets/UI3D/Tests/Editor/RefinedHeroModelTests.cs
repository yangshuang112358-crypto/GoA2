using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Goa2.UI3D.Tests
{
    // Import-contract tests, not an artistic or animation-intersection approval.
    // Keep these independent of exact decoration, triangulation or vertex counts.
    public sealed class RefinedHeroModelTests
    {
        [UnityTest, Category("RequiresGraphics")]
        public IEnumerator RuntimeAuraWrappersPreserveFbxUnitsAndUpAxis()
        {
            yield return new EnterPlayMode();
            var directory=Directory.GetParent(UnityEngine.Application.dataPath).Parent.FullName;
            var catalog=ContentLoader.LoadDirectory(directory);
            var game=LocalGameFactory.Create(catalog,"refined-aura-import",new[]{"A","B","C","D"},42,true);
            var initial=game.View(0);
            Assert.That(game.Execute(0,new Command{Id="prepare",MatchId=initial.MatchId,ExpectedRevision=initial.Revision,ActorSeat=0,Kind=CommandKind.DebugPrepare,Value="wasp,arien,tigerclaw,sabina"}).Accepted,Is.True);
            string before=game.ExportSave();
            using(var board=new Board3DScene(catalog,game.View(0),new Hex[0],null,new Hex[0],new Board3DViewport()))
            {
                board.Render(800,600);
                var nodes=board.Camera.transform.parent.GetComponentsInChildren<Transform>();
                foreach(string hero in new[]{"arien","tigerclaw"}){
                    var effects=nodes.Where(t=>t.name=="hero aura "+hero).ToArray();
                    Assert.That(effects.Length,Is.EqualTo(3),hero+": layered aura missing");
                    foreach(var effect in effects){
                        var renderer=effect.GetComponentInChildren<Renderer>();
                        Assert.That(renderer,Is.Not.Null);
                        var bounds=renderer.bounds;
                        Assert.That(Mathf.Max(bounds.size.x,bounds.size.z),Is.InRange(.5f,2.5f),hero+": FBX unit conversion lost");
                        Assert.That(bounds.size.y,hero=="arien"?Is.LessThan(.20f):Is.InRange(1.4f,2.3f),hero+": FBX up axis lost");
                        Assert.That(renderer.sharedMaterial.shader.name,Is.EqualTo("Goa2/UI3D/HeroAura"));
                    }
                }
            }
            Assert.That(game.ExportSave(),Is.EqualTo(before));
            yield return new ExitPlayMode();
        }

        private static readonly HashSet<string> RuntimeMaterialSlots = new HashSet<string>
        {
            "WhiteArmor", "SkinYellow", "SkinGreen", "Skin", "BrownCloth",
            "Leather", "TealArmor", "ShadowCloth", "RedBeard", "Steel",
            "Bronze", "Glow", "Shadow", "Linen"
        };

        [TestCase("wasp")]
        [TestCase("shargatha")]
        [TestCase("brogan")]
        [TestCase("arien")]
        [TestCase("tigerclaw")]
        [TestCase("sabina")]
        public void ImportedHeroHasFinitePaintableGeometryAndNoStudioComponents(string hero)
        {
            var model = Load(hero);
            Assert.That(model.GetComponentsInChildren<Camera>(true), Is.Empty, hero + ": studio camera leaked into FBX");
            Assert.That(model.GetComponentsInChildren<Light>(true), Is.Empty, hero + ": studio light leaked into FBX");
            Assert.That(model.GetComponentsInChildren<Collider>(true), Is.Empty, hero + ": presentation must not add game collision");
            Assert.That(model.GetComponentsInChildren<Collider2D>(true), Is.Empty, hero + ": presentation must not add 2D collision");

            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderers, Is.Not.Empty, hero + ": no skinned runtime mesh");
            long triangles = 0;
            foreach (var renderer in renderers)
            {
                var mesh = renderer.sharedMesh;
                Assert.That(mesh, Is.Not.Null, hero + ": missing mesh on " + renderer.name);
                Assert.That(mesh.vertexCount, Is.GreaterThan(0), hero + ": empty geometry");
                Assert.That(mesh.vertices.All(Finite), Is.True, hero + ": non-finite position");
                Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount), hero + ": missing vertex normals");
                Assert.That(mesh.normals.All(n => Finite(n) && n.sqrMagnitude > .25f), Is.True, hero + ": invalid vertex normal");

                var uv = mesh.uv;
                Assert.That(uv.Length, Is.EqualTo(mesh.vertexCount), hero + ": UV0 must cover every vertex");
                Assert.That(uv.All(v => Finite(v.x) && Finite(v.y)), Is.True, hero + ": invalid UV0");
                Assert.That(uv.Max(v => v.x) - uv.Min(v => v.x), Is.GreaterThan(.01f), hero + ": collapsed U coordinates");
                Assert.That(uv.Max(v => v.y) - uv.Min(v => v.y), Is.GreaterThan(.01f), hero + ": collapsed V coordinates");

                var tangents = mesh.tangents;
                Assert.That(tangents.Length, Is.EqualTo(mesh.vertexCount), hero + ": missing tangents for authored normal maps");
                Assert.That(tangents.All(t => Finite(t.x) && Finite(t.y) && Finite(t.z) && Finite(t.w)
                    && new Vector3(t.x, t.y, t.z).sqrMagnitude > .25f
                    && Mathf.Abs(Mathf.Abs(t.w) - 1) < .01f), Is.True, hero + ": degenerate tangent basis");
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    Assert.That(mesh.GetTopology(submesh), Is.EqualTo(MeshTopology.Triangles), hero + ": unsupported primitive topology");
                    triangles += mesh.GetIndexCount(submesh) / 3;
                }
            }
            Assert.That(triangles, Is.InRange(1L, 40000L), hero + ": total runtime triangle budget");

            // Bounds on an instance include the FBX axis/unit conversion. Testing
            // raw mesh.bounds would incorrectly interpret Blender Z-up as Y-up.
            var instance = Object.Instantiate(model);
            try
            {
                var allRenderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(allRenderers, Is.Not.Empty);
                var bounds = allRenderers[0].bounds;
                foreach (var renderer in allRenderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                Assert.That(Finite(bounds.center) && Finite(bounds.size), Is.True, hero + ": invalid imported world bounds");
                Assert.That(bounds.size.y, Is.InRange(.8f, 3.3f), hero + ": import units or up axis changed");
                Assert.That(bounds.size.x, Is.InRange(.2f, 3.5f), hero + ": unreasonable width");
                Assert.That(bounds.size.z, Is.InRange(.15f, 3.5f), hero + ": unreasonable depth");
                Assert.That(bounds.min.y, Is.InRange(-.15f, .15f), hero + ": feet/base no longer near the ground anchor");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCase("wasp")]
        [TestCase("shargatha")]
        [TestCase("brogan")]
        [TestCase("arien")]
        [TestCase("tigerclaw")]
        [TestCase("sabina")]
        public void ImportedHeroHasValidSkinBindingsAndRecognizedMaterialSlots(string hero)
        {
            var model = Load(hero);
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderers, Is.Not.Empty, hero + ": no skinned runtime mesh");
            foreach (var renderer in renderers)
            {
                var mesh = renderer.sharedMesh;
                Assert.That(mesh, Is.Not.Null);
                var bones = renderer.bones;
                Assert.That(bones, Is.Not.Empty, hero + ": skeleton not imported");
                Assert.That(bones.All(b => b != null && (b == model.transform || b.IsChildOf(model.transform))),
                    Is.True, hero + ": null or external bone reference");
                Assert.That(renderer.rootBone, Is.Not.Null, hero + ": missing skin root");
                Assert.That(renderer.rootBone == model.transform || renderer.rootBone.IsChildOf(model.transform),
                    Is.True, hero + ": external skin root");
                Assert.That(mesh.bindposes.Length, Is.EqualTo(bones.Length), hero + ": bindpose/bone count mismatch");
                Assert.That(mesh.bindposes.All(Finite), Is.True, hero + ": invalid bindpose matrix");

                // Current importer permits up to four influences. Do not require
                // rigid single-bone weights: later smooth skinning remains valid.
                var weights = mesh.boneWeights;
                Assert.That(weights.Length, Is.EqualTo(mesh.vertexCount), hero + ": vertices without defined weights");
                int invalid = Array.FindIndex(weights, w => !ValidWeights(w, bones.Length));
                Assert.That(invalid, Is.EqualTo(-1), hero + ": invalid normalized skin weights at vertex " + invalid);

                var materials = renderer.sharedMaterials;
                Assert.That(materials.Length, Is.EqualTo(mesh.subMeshCount), hero + ": submesh/material mismatch");
                Assert.That(materials.All(m => m != null), Is.True, hero + ": missing material slot");
                foreach (var material in materials)
                {
                    // This is the same semantic prefix used by BuildHero; an
                    // unknown name would silently fall back to generic blue steel.
                    string slot = material.name.Split(' ')[0];
                    Assert.That(RuntimeMaterialSlots.Contains(slot), Is.True,
                        hero + ": unsupported runtime material slot " + material.name);
                }
            }
        }

        private static GameObject Load(string hero)
        {
            var model = Resources.Load<GameObject>("UI3D/Heroes/" + hero);
            Assert.That(model, Is.Not.Null, "Missing hero resource: " + hero);
            return model;
        }

        private static bool ValidWeights(BoneWeight weight, int boneCount)
        {
            return ValidInfluence(weight.weight0, weight.boneIndex0, boneCount)
                && ValidInfluence(weight.weight1, weight.boneIndex1, boneCount)
                && ValidInfluence(weight.weight2, weight.boneIndex2, boneCount)
                && ValidInfluence(weight.weight3, weight.boneIndex3, boneCount)
                && Mathf.Abs(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3 - 1) <= .002f;
        }

        private static bool ValidInfluence(float weight, int index, int count)
        {
            return Finite(weight) && weight >= 0 && weight <= 1
                && (weight == 0 || index >= 0 && index < count);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Matrix4x4 value)
        {
            for (int index = 0; index < 16; index++) if (!Finite(value[index])) return false;
            return true;
        }
    }
}
