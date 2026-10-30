// Push/twist logic for the vessel playground. Both devices share one route. Whichever tip
// is furthest in leads: when it reaches a junction it picks the branch its bent tip points
// into, and the other device follows that committed route. Pulling the leading tip back
// behind a junction un-commits the branch so it can be chosen again.
// Port of CathWirePlaygroundModel.swift.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BloodVessels
{
    public sealed class CathWirePlaygroundModel
    {
        public VesselModelSpec Spec { get; private set; }
        public VesselGraph Graph { get; private set; }
        /// Model frame → world: laid flat, access site on the right.
        public Quaternion Orientation { get; private set; }
        public VesselRoute Route { get; private set; }
        public FlexDevice Wire { get; private set; } = FlexDevice.Guidewire;
        public FlexDevice Catheter { get; private set; } = FlexDevice.Catheter;

        /// Device the on-screen controls (and a single-hand input) drive.
        public DeviceKind ActiveDevice = DeviceKind.Wire;

        /// Bumped on every change, so views can rebuild only when something moved.
        public int Version { get; private set; }

        /// Segments of the main trunk (access site → farthest end), for naming.
        HashSet<int> trunkSegments;

        /// Branch is entered only when the tip points within this angle of it; otherwise the
        /// tip carries straight on along the trunk. Default for shaped tips; each TipShape
        /// sets its own.
        public const float BranchCaptureAngle = 55 * Mathf.Deg2Rad;
        /// Tips stop this far short of a dead end.
        public const float EndMargin = 0.001f;
        /// "At junction" readout tolerance.
        public const float JunctionTolerance = 0.003f;

        public CathWirePlaygroundModel(VesselGraph graph, VesselModelSpec spec = null) => Load(spec ?? VesselModelSpec.Tube, graph);

        /// Switches to another vessel model: new graph, devices back at the access site, sized
        /// for that model's vessels. The chosen tip shapes are kept.
        public void Load(VesselModelSpec spec, VesselGraph graph)
        {
            Spec = spec;
            Graph = graph;
            Orientation = VesselLayout.FlatOrientation(graph);
            trunkSegments = LongestPath(graph);
            Reset();
        }

        // MARK: - Queries

        public FlexDevice Device(DeviceKind kind) => kind == DeviceKind.Wire ? Wire : Catheter;

        public DeviceKind Leader => Wire.Insertion >= Catheter.Insertion ? DeviceKind.Wire : DeviceKind.Catheter;

        public float LeadingInsertion => Mathf.Max(Wire.Insertion, Catheter.Insertion);

        public bool TipAtJunction => Route.EndsAtJunction && Route.Length - LeadingInsertion < JunctionTolerance;

        public bool TipAtDeadEnd => !Route.EndsAtJunction && Route.Length - LeadingInsertion <= EndMargin + 0.0005f;

        /// Junctions passed by the route so far, counted from the access site.
        public int JunctionsPassed => Route.Legs.Count - 1;

        public string RouteDescription
        {
            get
            {
                for (int i = 0; i < Route.Legs.Count; i++)
                    if (!trunkSegments.Contains(Route.Legs[i].Segment)) return $"Side branch {i}";
                return "Main trunk";
            }
        }

        /// Model-frame direction the device's bent tip points at its current position.
        public Vector3 TipBendDirection(DeviceKind kind)
        {
            var d = Device(kind);
            return Route.FrameAt(d.Insertion).Direction(d.Twist);
        }

        // MARK: - Commands

        public void Push(DeviceKind kind, float delta)
        {
            var d = Device(kind);
            float target = Mathf.Max(0, d.Insertion + delta);

            // Tip passing the end of the route at a junction → choose a branch.
            while (target > Route.Length && Route.EndsAtJunction)
            {
                var bend = Route.FrameAt(Route.Length).Direction(d.Twist);
                var next = ChooseNextLeg(bend, d.Tip.CaptureAngle());
                if (next == null) break;
                Route.Append(next.Value);
            }
            target = Mathf.Min(target, Route.EndsAtJunction ? Route.Length : Route.Length - EndMargin);
            d.Insertion = target;
            Set(d);
            PruneRoute();
        }

        public void Twist(DeviceKind kind, float delta)
        {
            var d = Device(kind);
            d.Twist += delta;
            Set(d);
        }

        /// Swaps the device's tip shape in place; insertion, twist and the committed route
        /// are kept.
        public void SetTip(TipShape shape, DeviceKind kind)
        {
            var d = Device(kind);
            d.Tip = shape;
            Set(d);
        }

        /// Devices back at the access site; the chosen tip shapes are kept.
        public void Reset()
        {
            Route = new VesselRoute(Graph);
            var wire = FlexDevice.Guidewire.Scaled(Spec.DeviceScale);
            wire.Tip = Wire.Tip;
            var catheter = FlexDevice.Catheter.Scaled(Spec.DeviceScale);
            catheter.Tip = Catheter.Tip;
            Wire = wire;
            Catheter = catheter;
            Version++;
        }

        // MARK: - Branch selection

        /// Scores each leg leaving the junction by how well the tip's bend lines up with the
        /// leg's sideways heading. A single near-straight leg is the default when no side
        /// branch is within the capture angle; when several legs run near-straight (a tight
        /// fork), the bend picks among all of them. A tip that can't steer (null capture
        /// angle) takes the straightest leg.
        public VesselRoute.Leg? ChooseNextLeg(Vector3 bendDirection, float? captureAngle = BranchCaptureAngle)
        {
            var tangent = Route.FrameAt(Route.Length).Tangent;
            var candidates = Route.NextLegs().Select(leg =>
            {
                var heading = Route.Heading(leg);
                var lateral = heading - tangent * Vector3.Dot(heading, tangent);
                float sideways = lateral.magnitude;
                // Bend alignment; a leg with no sideways component (straight on, or doubling
                // straight back) scores neutral.
                float score = sideways > 1e-3f ? Vector3.Dot(lateral / sideways, bendDirection) : 0;
                return (leg, alignment: Vector3.Dot(heading, tangent), sideways, score);
            }).ToList();
            if (candidates.Count == 0) return null;

            if (captureAngle == null) return candidates.OrderByDescending(c => c.alignment).First().leg;
            // Within ~20° of straight ahead (not a hairpin back).
            var straight = candidates.Where(c => c.sideways < 0.35f && c.alignment > 0).ToList();
            if (straight.Count != 1) return candidates.OrderByDescending(c => c.score).First().leg;

            var sides = candidates.Where(c => c.leg != straight[0].leg).ToList();
            if (sides.Count > 0)
            {
                var side = sides.OrderByDescending(c => c.score).First();
                if (side.score >= Mathf.Cos(captureAngle.Value)) return side.leg;
            }
            return straight[0].leg;
        }

        // MARK: - Private

        void Set(FlexDevice d)
        {
            if (d.Kind == DeviceKind.Wire) Wire = d; else Catheter = d;
            Version++;
        }

        /// Drop legs the leading tip has pulled back out of.
        void PruneRoute()
        {
            while (Route.Legs.Count > 1 && LeadingInsertion <= Route.LegStarts[Route.LegStarts.Length - 1] + 0.0001f)
                Route.RemoveLastLeg();
        }

        /// Segment ids on the longest path from the access site (the main trunk).
        static HashSet<int> LongestPath(VesselGraph graph)
        {
            (float length, List<int> segments) Walk(int node, int? from)
            {
                (float, List<int>) best = (0, new List<int>());
                foreach (var seg in graph.SegmentsAt(node))
                {
                    if (seg.Id == from) continue;
                    var sub = Walk(seg.OtherEnd(node), seg.Id);
                    if (sub.length + seg.Length > best.Item1)
                        best = (sub.length + seg.Length, new List<int> { seg.Id }.Concat(sub.segments).ToList());
                }
                return best;
            }
            return new HashSet<int>(Walk(graph.InsertionNode, null).segments);
        }
    }
}
