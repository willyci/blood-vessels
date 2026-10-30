// Centerline graph of the vessel lumen, loaded from a centerline JSON (made once by
// reference/scripts/extract_centerlines.swift). Positions are converted from the file's
// millimetres to metres in the model's mesh frame.
//
// Port of VesselGraph.swift. `mirrorX` negates x so the graph lines up with the OBJ meshes,
// which Unity imports mirrored in x (right-handed file → left-handed Unity).

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BloodVessels
{
    public enum NodeKind { End, Junction }

    public sealed class VesselNode
    {
        public int Id;
        public NodeKind Kind;
        public Vector3 Position;
    }

    /// Polyline from node `From` to node `To`, sampled about every 1 mm.
    public sealed class VesselSegment
    {
        public int Id, From, To;
        public Vector3[] Points;
        public float[] Radii;

        public float Length
        {
            get
            {
                float length = 0;
                for (int i = 1; i < Points.Length; i++) length += Vector3.Distance(Points[i - 1], Points[i]);
                return length;
            }
        }

        public int OtherEnd(int node) => node == From ? To : From;
    }

    public sealed class VesselGraph
    {
        public IReadOnlyDictionary<int, VesselNode> Nodes { get; }
        public IReadOnlyDictionary<int, VesselSegment> Segments { get; }
        /// Access site — the open end the devices enter through.
        public int InsertionNode { get; }

        VesselGraph(Dictionary<int, VesselNode> nodes, Dictionary<int, VesselSegment> segments, int insertionNode)
        {
            Nodes = nodes;
            Segments = segments;
            InsertionNode = insertionNode;
        }

        /// Segments that meet at `node`, by id.
        public List<VesselSegment> SegmentsAt(int node) =>
            Segments.Values.Where(s => s.From == node || s.To == node).OrderBy(s => s.Id).ToList();

        public IEnumerable<Vector3> AllPoints => Segments.Values.SelectMany(s => s.Points);

        public static VesselGraph FromJson(string json, bool mirrorX)
        {
            var file = JObject.Parse(json);
            float scale = (string)file["units"] switch
            {
                "mm" => 0.001f,
                "m" => 1f,
                var units => throw new FormatException($"Unknown centerline units '{units}'"),
            };
            float sx = mirrorX ? -scale : scale;
            Vector3 Vec(JToken a) => new((float)a[0] * sx, (float)a[1] * scale, (float)a[2] * scale);

            var nodes = file["nodes"].ToDictionary(
                n => (int)n["id"],
                n => new VesselNode
                {
                    Id = (int)n["id"],
                    Kind = (string)n["kind"] == "junction" ? NodeKind.Junction : NodeKind.End,
                    Position = Vec(n["pos"]),
                });
            var segments = file["segments"].ToDictionary(
                s => (int)s["id"],
                s => new VesselSegment
                {
                    Id = (int)s["id"],
                    From = (int)s["from"],
                    To = (int)s["to"],
                    Points = s["points"].Select(Vec).ToArray(),
                    Radii = s["radii"].Select(r => (float)r * scale).ToArray(),
                });

            int insertion = (int)file["insertionNode"];
            if (!nodes.TryGetValue(insertion, out var access) || access.Kind != NodeKind.End)
                throw new FormatException("insertionNode must be an end node");
            return new VesselGraph(nodes, segments, insertion);
        }
    }
}
