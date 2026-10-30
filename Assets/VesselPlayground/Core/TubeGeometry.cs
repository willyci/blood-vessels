// Sweeps a circle along a polyline (one ring per sample, oriented by the sample's twist
// normal) and closes the far end with a cap. UV.x runs around the circumference so a
// stripe texture shows twist. Port of TubeMeshBuilder.swift.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BloodVessels
{
    public sealed class TubeGeometry
    {
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<int> Indices = new();

        /// `centers`: ring centers, start → tip. `twistNormals`: per-ring reference direction
        /// (projected onto the ring plane).
        public TubeGeometry(IReadOnlyList<Vector3> centers, IReadOnlyList<Vector3> twistNormals, float radius, int sides = 10)
        {
            int n = centers.Count;
            if (n < 2 || twistNormals.Count != n || sides < 3) return;
            int ringSize = sides + 1;   // duplicate seam vertex for UV wrap

            for (int i = 0; i < n; i++)
            {
                var tangent = (centers[Mathf.Min(n - 1, i + 1)] - centers[Mathf.Max(0, i - 1)]).normalized;
                var normal = twistNormals[i] - tangent * Vector3.Dot(twistNormals[i], tangent);
                if (normal.sqrMagnitude < 1e-12f) normal = Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right;
                normal.Normalize();
                var binormal = Vector3.Cross(tangent, normal);
                for (int j = 0; j <= sides; j++)
                {
                    float angle = (float)j / sides * 2 * Mathf.PI;
                    var radial = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                    Positions.Add(centers[i] + radial * radius);
                    Normals.Add(radial);
                    Uvs.Add(new Vector2((float)j / sides, (float)i / (n - 1)));
                }
            }
            // Unity treats clockwise triangles (seen from outside) as front faces.
            for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * ringSize + j, b = a + ringSize;
                Indices.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }

            // Tip cap: fan around a center vertex.
            var tipTangent = (centers[n - 1] - centers[n - 2]).normalized;
            int center = Positions.Count;
            Positions.Add(centers[n - 1] + tipTangent * (radius * 0.5f));
            Normals.Add(tipTangent);
            Uvs.Add(new Vector2(0.5f, 1));
            int lastRing = (n - 1) * ringSize;
            for (int j = 0; j < sides; j++) Indices.AddRange(new[] { lastRing + j, lastRing + j + 1, center });
        }

        /// Writes the geometry into `mesh` (reused across rebuilds to avoid allocations).
        public void ApplyTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = Positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Indices, 0);
            mesh.RecalculateBounds();
        }
    }
}
