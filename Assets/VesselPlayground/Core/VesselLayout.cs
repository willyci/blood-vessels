// How a vessel model sits in the room: laid flat (its thinnest direction pointing up, so
// the branches spread out horizontally like a patient on a table) with the access site on
// the user's right. Computed from the centerlines so every model, including future ones,
// follows the same rule. Port of VesselLayout.swift.

using System.Linq;
using UnityEngine;

namespace BloodVessels
{
    public static class VesselLayout
    {
        /// Model frame → world rotation: the centerlines' least-spread principal axis →
        /// world +y, the center → access-site direction → world +x.
        public static Quaternion FlatOrientation(VesselGraph graph)
        {
            var points = graph.AllPoints.ToArray();
            if (points.Length <= 3 || !graph.Nodes.TryGetValue(graph.InsertionNode, out var access))
                return Quaternion.identity;

            var center = Vector3.zero;
            foreach (var p in points) center += p;
            center /= points.Length;

            var covariance = new double[3, 3];
            foreach (var p in points)
            {
                var d = p - center;
                for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    covariance[r, c] += d[r] * d[c];
            }
            var up = LeastSpreadAxis(covariance);
            // Sign is arbitrary for a symmetric matrix; fix it so results are stable.
            int largest = Enumerable.Range(0, 3).OrderByDescending(i => Mathf.Abs(up[i])).First();
            if (up[largest] < 0) up = -up;

            var right = access.Position - center;
            right -= up * Vector3.Dot(right, up);
            if (right.sqrMagnitude < 1e-12f) right = Vector3.Cross(up, Vector3.forward);
            right.Normalize();

            // Inverse maps world x → right, y → up, z → right × up.
            return Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(right, up), up));
        }

        /// Eigenvector of the smallest eigenvalue of a symmetric 3×3 matrix (cyclic Jacobi
        /// rotations).
        public static Vector3 LeastSpreadAxis(double[,] m)
        {
            var a = (double[,])m.Clone();
            var v = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int iteration = 0; iteration < 32; iteration++)
            {
                // Largest off-diagonal element.
                int p = 0, q = 1;
                if (System.Math.Abs(a[0, 2]) > System.Math.Abs(a[p, q])) { p = 0; q = 2; }
                if (System.Math.Abs(a[1, 2]) > System.Math.Abs(a[p, q])) { p = 1; q = 2; }
                if (System.Math.Abs(a[p, q]) < 1e-15) break;

                double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                double t = (theta >= 0 ? 1 : -1) / (System.Math.Abs(theta) + System.Math.Sqrt(theta * theta + 1));
                double c = 1 / System.Math.Sqrt(t * t + 1), s = t * c;

                // a ← Rᵀ a R, v ← v R, with R the (p, q) Givens rotation.
                for (int k = 0; k < 3; k++)
                {
                    double akp = a[k, p], akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[k, q] = s * akp + c * akq;
                }
                for (int k = 0; k < 3; k++)
                {
                    double apk = a[p, k], aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk;
                    a[q, k] = s * apk + c * aqk;
                }
                for (int k = 0; k < 3; k++)
                {
                    double vkp = v[k, p], vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq;
                    v[k, q] = s * vkp + c * vkq;
                }
            }
            int smallest = Enumerable.Range(0, 3).OrderBy(i => a[i, i]).First();
            return new Vector3((float)v[0, smallest], (float)v[1, smallest], (float)v[2, smallest]).normalized;
        }
    }
}
