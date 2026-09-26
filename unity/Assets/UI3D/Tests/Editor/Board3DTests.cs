#nullable enable
using System.IO;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Presentation.UI3D;
using NUnit.Framework;
using UnityEngine;

namespace Goa2.UI3D.Tests
{
    public sealed class Board3DTests
    {
        [Test]
        public void All254MapCoordinatesRoundTripAndNeighborsAreEquidistant()
        {
            var root=Directory.GetParent(UnityEngine.Application.dataPath)!.Parent!.FullName;
            var catalog=ContentLoader.LoadDirectory(root);
            Assert.That(catalog.Cells.Count,Is.EqualTo(254));
            foreach(var cell in catalog.Cells)
            {
                var world=Board3DGeometry.World(cell.Position);
                Assert.That(Board3DGeometry.HexAt(world),Is.EqualTo(cell.Position));
                foreach(var neighbor in cell.Position.Neighbors())
                    Assert.That(Vector3.Distance(world,Board3DGeometry.World(neighbor)),Is.EqualTo(Mathf.Sqrt(3)).Within(.00001f));
                Assert.That(Board3DGeometry.HexAt(world+new Vector3(.2f,100,.2f)),Is.EqualTo(cell.Position));
            }
        }
        [TestCase(-1,11)] [TestCase(12,0)] [TestCase(-24,0)] [TestCase(25,1)]
        public void RotationWrapsExactlyTwelveSteps(int step,int expected) => Assert.That(Board3DGeometry.WrapStep(step),Is.EqualTo(expected));
        [Test]
        public void TwelveAnglesAreDistinctAndCloseWithoutDrift()
        {
            for(int i=0;i<12;i++)
            {
                var rotation=Board3DGeometry.Rotation(i);
                Assert.That(Quaternion.Angle(rotation,Board3DGeometry.Rotation(i+12)),Is.LessThan(.01));
                for(int j=i+1;j<12;j++) Assert.That(Quaternion.Angle(rotation,Board3DGeometry.Rotation(j)),Is.GreaterThan(29.9f));
            }
        }
        [Test]
        public void ProceduralTopNormalsFaceUpAndStayInsideCell()
        {
            var mesh=Board3DGeometry.Prism(48,0);
            try
            {
                Assert.That(mesh.normals[0].y,Is.GreaterThan(.99));
                foreach(var vertex in mesh.vertices) Assert.That(new Vector2(vertex.x,vertex.z).magnitude,Is.LessThanOrEqualTo(1.00001f));
            }
            finally {Object.DestroyImmediate(mesh);}
        }
    }
}
