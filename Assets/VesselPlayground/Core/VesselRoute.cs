// The path the devices currently occupy: an ordered chain of centerline segments starting
// at the access site. Sampled into one polyline with parallel-transport frames so a
// device's twist is measured against a frame that does not spin as the vessel curves.
// Port of VesselRoute.swift.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BloodVessels
{
    public sealed class VesselRoute
    {
        /// One segment of the chain, walked From→To when `Forward`.
        public readonly struct Leg : System.IEquatable<Leg>
        {
            public readonly int Segment;
            public readonly bool Forward;
            public Leg(int segment, bool forward) { Segment = segment; Forward = forward; }
            public bool Equals(Leg other) => Segment == other.Segment && Forward == other.Forward;
            public override bool Equals(object obj) => obj is Leg other && Equals(other);
            public override int GetHashCode() => Segment * 2 + (Forward ? 1 : 0);
            public static bool operator ==(Leg a, Leg b) => a.Equals(b);
            public static bool operator !=(Leg a, Leg b) => !a.Equals(b);
        }

        /// Position + orientation at an arc length along the route.
        public readonly struct Frame
        {
            public readonly Vector3 Position, Tangent;
            /// Twist-zero direction, perpendicular to `Tangent`.
            public readonly Vector3 Normal;
            /// Lumen radius (0 outside the vessel).
            public readonly float Radius;

            public Frame(Vector3 position, Vector3 tangent, Vector3 normal, float radius)
            {
                Position = position; Tangent = tangent; Normal = normal; Radius = radius;
            }

            public Vector3 Binormal => Vector3.Cross(Tangent, Normal);

            /// Unit direction perpendicular to the tangent, rotated `twist` from `Normal`.
            public Vector3 Direction(float twist) => Normal * Mathf.Cos(twist) + Binormal * Mathf.Sin(twist);
        }

        public VesselGraph Graph { get; }
        readonly List<Leg> legs = new();
        public IReadOnlyList<Leg> Legs => legs;

        Vector3[] points = { };
        float[] radii = { };
        Vector3[] tangents = { };
        Vector3[] normals = { };
        /// Cumulative arc length at each sample.
        float[] arcLengths = { 0 };
        /// Arc length where each leg starts.
        public float[] LegStarts { get; private set; } = { };

        public VesselRoute(VesselGraph graph)
        {
            Graph = graph;
            var first = graph.SegmentsAt(graph.InsertionNode)[0];
            legs.Add(new Leg(first.Id, first.From == graph.InsertionNode));
            Resample();
        }

        public float Length => arcLengths[arcLengths.Length - 1];

        /// Node at the far end of the last leg.
        public int EndNode => EndNodeOf(legs[legs.Count - 1]);

        public bool EndsAtJunction => Graph.Nodes.TryGetValue(EndNode, out var n) && n.Kind == NodeKind.Junction;

        // MARK: - Editing

        public void Append(Leg leg)
        {
            legs.Add(leg);
            Resample();
        }

        /// Drops the last leg; the first leg (from the access site) always stays.
        public void RemoveLastLeg()
        {
            if (legs.Count <= 1) return;
            legs.RemoveAt(legs.Count - 1);
            Resample();
        }

        /// Legs leaving the route's end junction, excluding the way it came in.
        public List<Leg> NextLegs()
        {
            if (!EndsAtJunction) return new List<Leg>();
            int node = EndNode;
            int arrivedOn = legs[legs.Count - 1].Segment;
            return Graph.SegmentsAt(node).Where(s => s.Id != arrivedOn)
                .Select(s => new Leg(s.Id, s.From == node)).ToList();
        }

        /// Unit direction a candidate leg heads in, sampled `lookAhead` metres past the
        /// junction so a curved branch reads by its overall heading.
        public Vector3 Heading(Leg leg, float lookAhead = 0.006f)
        {
            var pts = OrderedPoints(leg);
            if (pts.Length == 0) return Vector3.zero;
            float travelled = 0;
            var target = pts[pts.Length - 1];
            for (int i = 1; i < pts.Length; i++)
            {
                travelled += Vector3.Distance(pts[i - 1], pts[i]);
                if (travelled >= lookAhead) { target = pts[i]; break; }
            }
            return (target - pts[0]).normalized;
        }

        // MARK: - Sampling

        /// Frame at arc length `s`. Before the access site (s < 0) the route continues
        /// straight out of the vessel along the entry direction.
        public Frame FrameAt(float s)
        {
            if (points.Length < 2)
                return new Frame(points.Length > 0 ? points[0] : Vector3.zero, Vector3.up, Vector3.right, 0);
            if (s <= 0)
                return new Frame(points[0] + tangents[0] * s, tangents[0], normals[0], s < 0 ? 0 : radii[0]);

            s = Mathf.Min(s, Length);
            int lo = 0, hi = arcLengths.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (arcLengths[mid] <= s) lo = mid; else hi = mid;
            }
            float span = arcLengths[hi] - arcLengths[lo];
            float f = span > 0 ? (s - arcLengths[lo]) / span : 0;
            var tangent = Vector3.Lerp(tangents[lo], tangents[hi], f).normalized;
            var normal = Vector3.Lerp(normals[lo], normals[hi], f);
            normal = (normal - tangent * Vector3.Dot(normal, tangent)).normalized;
            return new Frame(Vector3.Lerp(points[lo], points[hi], f), tangent, normal,
                Mathf.Lerp(radii[lo], radii[hi], f));
        }

        // MARK: - Private

        int EndNodeOf(Leg leg)
        {
            var seg = Graph.Segments[leg.Segment];
            return leg.Forward ? seg.To : seg.From;
        }

        Vector3[] OrderedPoints(Leg leg)
        {
            if (!Graph.Segments.TryGetValue(leg.Segment, out var seg)) return new Vector3[0];
            return leg.Forward ? seg.Points : seg.Points.Reverse().ToArray();
        }

        void Resample()
        {
            var pts = new List<Vector3>();
            var rad = new List<float>();
            var legStartIndices = new List<int>();
            foreach (var leg in legs)
            {
                if (!Graph.Segments.TryGetValue(leg.Segment, out var seg)) continue;
                IEnumerable<Vector3> p = seg.Points;
                IEnumerable<float> r = seg.Radii;
                if (!leg.Forward) { p = p.Reverse(); r = r.Reverse(); }
                if (pts.Count > 0) { p = p.Skip(1); r = r.Skip(1); }   // shared junction point
                legStartIndices.Add(Mathf.Max(0, pts.Count - 1));
                pts.AddRange(p);
                rad.AddRange(r);
            }
            points = pts.ToArray();
            radii = rad.ToArray();

            int n = points.Length;
            arcLengths = new float[Mathf.Max(n, 1)];
            for (int i = 1; i < n; i++) arcLengths[i] = arcLengths[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            LegStarts = legStartIndices.Select(i => arcLengths[i]).ToArray();

            tangents = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var d = points[Mathf.Min(n - 1, i + 1)] - points[Mathf.Max(0, i - 1)];
                tangents[i] = d.sqrMagnitude > 0 ? d.normalized : Vector3.up;
            }

            // Parallel transport: carry the normal along, removing only the component the
            // tangent change introduces (fine at 1 mm spacing).
            normals = new Vector3[n];
            if (n == 0) return;
            var t0 = tangents[0];
            var reference = Mathf.Abs(t0.z) < 0.9f ? Vector3.forward : Vector3.right;
            var normal = (reference - t0 * Vector3.Dot(reference, t0)).normalized;
            for (int i = 0; i < n; i++)
            {
                normal = (normal - tangents[i] * Vector3.Dot(normal, tangents[i])).normalized;
                normals[i] = normal;
            }
        }
    }
}
