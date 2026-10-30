//
//  VesselLayout.swift
//  Mar2025VascCath
//
//  How a vessel model sits in the room: laid flat (its thinnest direction
//  pointing up, so the branches spread out horizontally like a patient on
//  a table) with the access site on the user's right. Computed from the
//  centerlines so every model, including future ones, follows the same rule.
//

import simd

enum VesselLayout {
    /// Model frame → world rotation: the centerlines' least-spread principal
    /// axis → world +y, the center → access-site direction → world +x.
    static func flatOrientation(for graph: VesselGraph) -> simd_quatf {
        let points = graph.segments.values.flatMap(\.points)
        guard points.count > 3, let access = graph.nodes[graph.insertionNode]?.position else {
            return simd_quatf(angle: 0, axis: [0, 1, 0])
        }
        let center = points.reduce(SIMD3<Float>.zero, +) / Float(points.count)

        var covariance = simd_float3x3()
        for p in points {
            let d = p - center
            covariance += simd_float3x3(columns: (d * d.x, d * d.y, d * d.z))
        }
        var up = leastSpreadAxis(of: covariance)
        // Sign is arbitrary for a symmetric matrix; fix it so results are stable.
        if up[maxMagnitudeIndex(up)] < 0 { up = -up }

        var right = access - center
        right -= up * simd_dot(right, up)
        if simd_length(right) < 1e-6 { right = simd_cross(up, [0, 0, 1]) }
        right = simd_normalize(right)
        let toward = simd_cross(right, up)   // world +z, toward the user

        // Rows are the model-frame vectors that should land on world x, y, z.
        return simd_quatf(simd_float3x3(rows: [right, up, toward]))
    }

    /// Eigenvector of the smallest eigenvalue of a symmetric 3×3 matrix
    /// (cyclic Jacobi rotations).
    static func leastSpreadAxis(of m: simd_float3x3) -> SIMD3<Float> {
        var a = m
        var v = matrix_identity_float3x3
        for _ in 0..<32 {
            // Largest off-diagonal element.
            var (p, q) = (0, 1)
            if abs(a[2][0]) > abs(a[q][p]) { (p, q) = (0, 2) }
            if abs(a[2][1]) > abs(a[q][p]) { (p, q) = (1, 2) }
            if abs(a[q][p]) < 1e-12 { break }
            let theta = (a[q][q] - a[p][p]) / (2 * a[q][p])
            let t = (theta >= 0 ? 1 : -1) / (abs(theta) + (theta * theta + 1).squareRoot())
            let c = 1 / (t * t + 1).squareRoot(), s = t * c
            var r = matrix_identity_float3x3
            r[p][p] = c; r[q][q] = c; r[q][p] = s; r[p][q] = -s
            a = r.transpose * a * r
            v = v * r
        }
        let diagonal = [a[0][0], a[1][1], a[2][2]]
        let smallest = diagonal.indices.min { diagonal[$0] < diagonal[$1] }!
        return simd_normalize(v[smallest])
    }

    private static func maxMagnitudeIndex(_ v: SIMD3<Float>) -> Int {
        let m = [abs(v.x), abs(v.y), abs(v.z)]
        return m.indices.max { m[$0] < m[$1] }!
    }
}
