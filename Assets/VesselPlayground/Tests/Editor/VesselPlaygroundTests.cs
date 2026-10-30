// Port of the visionOS VesselPlaygroundTests.swift.
//
// Synthetic Y junction (mm): access end (0,0,0) → junction (0,20,0) → trunk end (0,40,0),
// plus a side branch from the junction to (10,30,0). The route's twist-zero direction at the
// access site is +z, so twist 90° bends the tip toward +x — into the side branch.
//
// Tight fork: the same access leg, then two legs both ~8° off straight ahead, to (3,40,0)
// and (-3,40,0).

using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BloodVessels.Tests
{
    public class VesselPlaygroundTests
    {
        CathWirePlaygroundModel sut;

        [SetUp]
        public void SetUp() => sut = new CathWirePlaygroundModel(VesselGraph.FromJson(YJunctionJson, mirrorX: false));

        // MARK: - Graph / route

        [Test]
        public void GraphLoad_Millimetres_ConvertsToMetres() =>
            Assert.AreEqual(0.02f, sut.Graph.Nodes[1].Position.y, 1e-6f);

        [Test]
        public void GraphLoad_MirrorX_NegatesX()
        {
            var mirrored = VesselGraph.FromJson(YJunctionJson, mirrorX: true);
            Assert.AreEqual(-0.01f, mirrored.Nodes[3].Position.x, 1e-6f);
        }

        [Test]
        public void Route_Initial_StartsAtAccessSite() =>
            Assert.AreEqual(0, Vector3.Distance(sut.Route.FrameAt(0).Position, Vector3.zero), 1e-6f);

        [Test]
        public void RouteFrame_BeforeAccessSite_ExtendsStraightOut() =>
            Assert.AreEqual(-0.01f, sut.Route.FrameAt(-0.01f).Position.y, 1e-6f);

        // MARK: - Branch selection

        [Test]
        public void Push_TipTwistedTowardBranch_EntersSideBranch()
        {
            sut.Twist(DeviceKind.Wire, Mathf.PI / 2);
            Advance(DeviceKind.Wire, 30);
            Assert.AreEqual("Side branch 1", sut.RouteDescription);
        }

        [Test]
        public void Push_TipTwistedAwayFromBranch_StaysOnTrunk()
        {
            Advance(DeviceKind.Wire, 30);
            Assert.AreEqual("Main trunk", sut.RouteDescription);
        }

        [Test]
        public void Push_StraightTipTwistedTowardBranch_StaysOnTrunk()
        {
            sut.SetTip(TipShape.Straight, DeviceKind.Wire);
            sut.Twist(DeviceKind.Wire, Mathf.PI / 2);
            Advance(DeviceKind.Wire, 30);
            Assert.AreEqual("Main trunk", sut.RouteDescription);
        }

        [Test]
        public void Push_TipAtJunction_ReportsJunction()
        {
            Advance(DeviceKind.Wire, 20);
            Assert.IsTrue(sut.TipAtJunction);
        }

        [Test]
        public void Catheter_AdvancedOverWire_FollowsWireRoute()
        {
            sut.Twist(DeviceKind.Wire, Mathf.PI / 2);
            Advance(DeviceKind.Wire, 30);
            Advance(DeviceKind.Catheter, 25);
            Assert.AreEqual("Side branch 1", sut.RouteDescription);
        }

        [Test]
        public void PullBack_BehindJunction_UncommitsBranch()
        {
            sut.Twist(DeviceKind.Wire, Mathf.PI / 2);
            Advance(DeviceKind.Wire, 30);
            Advance(DeviceKind.Wire, -15);
            Assert.AreEqual(1, sut.Route.Legs.Count);
        }

        [Test]
        public void Push_PastDeadEnd_ClampsShortOfEnd()
        {
            Advance(DeviceKind.Wire, 60);
            Assert.AreEqual(0.04f - CathWirePlaygroundModel.EndMargin, sut.Wire.Insertion, 1e-4f);
        }

        [Test]
        public void Push_PastDeadEnd_ReportsDeadEnd()
        {
            Advance(DeviceKind.Wire, 60);
            Assert.IsTrue(sut.TipAtDeadEnd);
        }

        [Test]
        public void Push_NegativeFromStart_StaysAtZero()
        {
            sut.Push(DeviceKind.Catheter, -0.01f);
            Assert.AreEqual(0, sut.Catheter.Insertion);
        }

        [Test]
        public void Reset_AfterBranching_ReturnsToTrunkStart()
        {
            sut.Twist(DeviceKind.Wire, Mathf.PI / 2);
            Advance(DeviceKind.Wire, 30);
            sut.Reset();
            Assert.AreEqual(1, sut.Route.Legs.Count);
        }

        [Test]
        public void Push_TightFork_BendTowardPlusX_TakesPlusXLeg()
        {
            sut = new CathWirePlaygroundModel(VesselGraph.FromJson(TightForkJson, mirrorX: false));
            sut.Twist(DeviceKind.Wire, Mathf.PI / 2);
            Advance(DeviceKind.Wire, 30);
            Assert.AreEqual(1, sut.Route.Legs[1].Segment);
        }

        [Test]
        public void Push_TightFork_BendTowardMinusX_TakesMinusXLeg()
        {
            sut = new CathWirePlaygroundModel(VesselGraph.FromJson(TightForkJson, mirrorX: false));
            sut.Twist(DeviceKind.Wire, -Mathf.PI / 2);
            Advance(DeviceKind.Wire, 30);
            Assert.AreEqual(2, sut.Route.Legs[1].Segment);
        }

        // MARK: - Vessel models

        [Test]
        public void Scaled_HalfScale_HalvesRadius() =>
            Assert.AreEqual(FlexDevice.Guidewire.Radius / 2, FlexDevice.Guidewire.Scaled(0.5f).Radius, 1e-9f);

        [Test]
        public void Scaled_HalfScale_KeepsTipShape() =>
            Assert.AreEqual(FlexDevice.Catheter.Tip, FlexDevice.Catheter.Scaled(0.5f).Tip);

        [Test]
        public void Scaled_HalfScale_HalvesTipScale() =>
            Assert.AreEqual(0.5f, FlexDevice.Catheter.Scaled(0.5f).TipScale, 1e-9f);

        [Test]
        public void Load_AvmSpec_ScalesDevices()
        {
            sut.Load(VesselModelSpec.Avm, sut.Graph);
            Assert.AreEqual(FlexDevice.Catheter.Radius * VesselModelSpec.Avm.DeviceScale, sut.Catheter.Radius, 1e-9f);
        }

        [Test]
        public void Load_AfterBranching_ResetsRoute()
        {
            sut.Twist(DeviceKind.Wire, Mathf.PI / 2);
            Advance(DeviceKind.Wire, 30);
            sut.Load(VesselModelSpec.Avm, sut.Graph);
            Assert.AreEqual(1, sut.Route.Legs.Count);
        }

        [Test]
        public void FlatOrientation_YJunction_PlaneTurnsHorizontal()
        {
            var q = VesselLayout.FlatOrientation(sut.Graph);
            var heights = sut.Graph.AllPoints.Select(p => (q * p).y).ToArray();
            Assert.AreEqual(0, heights.Max() - heights.Min(), 1e-5f);
        }

        [Test]
        public void FlatOrientation_YJunction_AccessOnRight()
        {
            var q = VesselLayout.FlatOrientation(sut.Graph);
            float maxX = sut.Graph.AllPoints.Max(p => (q * p).x);
            Assert.AreEqual(maxX, (q * sut.Graph.Nodes[0].Position).x, 1e-5f);
        }

        // MARK: - Tip shapes

        [Test]
        public void TipOptions_Wire_AreStraightJAndAngled() =>
            CollectionAssert.AreEqual(new[] { TipShape.Straight, TipShape.JTip, TipShape.Angled },
                TipShapes.OptionsFor(DeviceKind.Wire));

        [Test]
        public void TipOptions_Catheter_AreBernCobraAndMikaelsson() =>
            CollectionAssert.AreEqual(new[] { TipShape.Bern, TipShape.Cobra, TipShape.Mikaelsson },
                TipShapes.OptionsFor(DeviceKind.Catheter));

        [Test]
        public void Next_LastCatheterTip_WrapsToFirst() =>
            Assert.AreEqual(TipShape.Bern, TipShape.Mikaelsson.Next(DeviceKind.Catheter));

        [Test]
        public void Curve_JTip_EndsBackBesideShaft()
        {
            var end = TipShape.JTip.Curve(1, 0.0002f).Last();
            Assert.LessOrEqual(Vector2.Distance(end, new Vector2(0, 0.006f)), 0.0001f);   // 3 mm radius half-circle
        }

        [Test]
        public void Curve_Straight_IsSinglePoint() =>
            Assert.AreEqual(1, TipShape.Straight.Curve(1, 0.001f).Count);

        [Test]
        public void SetTip_AfterPush_KeepsInsertion()
        {
            Advance(DeviceKind.Catheter, 10);
            sut.SetTip(TipShape.Cobra, DeviceKind.Catheter);
            Assert.AreEqual(0.010f, sut.Catheter.Insertion, 1e-5f);
        }

        [Test]
        public void Reset_AfterSetTip_KeepsTip()
        {
            sut.SetTip(TipShape.JTip, DeviceKind.Wire);
            sut.Reset();
            Assert.AreEqual(TipShape.JTip, sut.Wire.Tip);
        }

        [Test]
        public void Load_AfterSetTip_KeepsTip()
        {
            sut.SetTip(TipShape.Mikaelsson, DeviceKind.Catheter);
            sut.Load(VesselModelSpec.Avm, sut.Graph);
            Assert.AreEqual(TipShape.Mikaelsson, sut.Catheter.Tip);
        }

        // MARK: - Geometry

        [Test]
        public void TubeGeometry_ThreeRingsFourSides_VertexCount() =>
            Assert.AreEqual(3 * 5 + 1, MakeTube().Positions.Count);   // rings with seam + cap center

        [Test]
        public void TubeGeometry_ThreeRingsFourSides_IndexCount() =>
            Assert.AreEqual(2 * 4 * 6 + 4 * 3, MakeTube().Indices.Count);   // body quads + cap fan

        [Test]
        public void DeviceShape_EveryTip_StaysInsideLumen()
        {
            Advance(DeviceKind.Wire, 15);
            foreach (var shape in new[] { TipShape.Straight, TipShape.JTip, TipShape.Angled, TipShape.Bern, TipShape.Cobra, TipShape.Mikaelsson })
            {
                sut.SetTip(shape, DeviceKind.Wire);
                var frame = sut.Route.FrameAt(sut.Wire.Insertion);
                float farthest = DeviceShape.Samples(sut.Wire, sut.Route).Skip(0).Reverse().Take(80)
                    .Max(s => Vector3.Cross(s.Position - frame.Position, frame.Tangent).magnitude);
                Assert.LessOrEqual(farthest, 0.003f - sut.Wire.Radius + 1e-5f, shape.Label());
            }
        }

        [Test]
        public void DeviceShape_BentTip_StaysInsideLumen()
        {
            sut.Twist(DeviceKind.Wire, Mathf.PI / 2);
            Advance(DeviceKind.Wire, 10);
            var tip = DeviceShape.Samples(sut.Wire, sut.Route).Last().Position;
            Assert.LessOrEqual(Vector3.Distance(tip, sut.Route.FrameAt(sut.Wire.Insertion).Position), 0.003f);
        }

        // MARK: - Helpers

        void Advance(DeviceKind kind, int mm)
        {
            float step = mm < 0 ? -0.001f : 0.001f;
            for (int i = 0; i < Mathf.Abs(mm); i++) sut.Push(kind, step);
        }

        static TubeGeometry MakeTube() =>
            new(new[] { Vector3.zero, Vector3.up, 2 * Vector3.up }, Enumerable.Repeat(Vector3.right, 3).ToArray(), 0.1f, sides: 4);

        static string Line(Vector3 a, Vector3 b)
        {
            int n = Mathf.RoundToInt(Vector3.Distance(a, b));
            return string.Join(",", Enumerable.Range(0, n + 1).Select(i =>
            {
                var p = a + (b - a) * i / n;
                return FormattableString.Invariant($"[{p.x},{p.y},{p.z}]");
            }));
        }

        static string Radii(int count) => string.Join(",", Enumerable.Repeat("3", count));

        static string Graph(string nodes, params (int id, int from, int to, Vector3 a, Vector3 b)[] segments) =>
            "{\"units\":\"mm\",\"insertionNode\":0,\"nodes\":[" + nodes + "],\"segments\":[" +
            string.Join(",", segments.Select(s =>
            {
                int count = Mathf.RoundToInt(Vector3.Distance(s.a, s.b)) + 1;
                return $"{{\"id\":{s.id},\"from\":{s.from},\"to\":{s.to},\"points\":[{Line(s.a, s.b)}],\"radii\":[{Radii(count)}]}}";
            })) + "]}";

        static readonly string YJunctionJson = Graph(
            "{\"id\":0,\"kind\":\"end\",\"pos\":[0,0,0]},{\"id\":1,\"kind\":\"junction\",\"pos\":[0,20,0]}," +
            "{\"id\":2,\"kind\":\"end\",\"pos\":[0,40,0]},{\"id\":3,\"kind\":\"end\",\"pos\":[10,30,0]}",
            (0, 0, 1, new Vector3(0, 0, 0), new Vector3(0, 20, 0)),
            (1, 1, 2, new Vector3(0, 20, 0), new Vector3(0, 40, 0)),
            (2, 1, 3, new Vector3(0, 20, 0), new Vector3(10, 30, 0)));

        static readonly string TightForkJson = Graph(
            "{\"id\":0,\"kind\":\"end\",\"pos\":[0,0,0]},{\"id\":1,\"kind\":\"junction\",\"pos\":[0,20,0]}," +
            "{\"id\":2,\"kind\":\"end\",\"pos\":[3,40,0]},{\"id\":3,\"kind\":\"end\",\"pos\":[-3,40,0]}",
            (0, 0, 1, new Vector3(0, 0, 0), new Vector3(0, 20, 0)),
            (1, 1, 2, new Vector3(0, 20, 0), new Vector3(3, 40, 0)),
            (2, 1, 3, new Vector3(0, 20, 0), new Vector3(-3, 40, 0)));
    }
}
