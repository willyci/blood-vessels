//
//  TubeMeshBuilder.swift
//  Mar2025VascCath
//
//  Sweeps a circle along a polyline (one ring per sample, oriented by the
//  sample's twist normal) and closes the far end with a cap. UV.x runs
//  around the circumference so a stripe texture shows twist.
//

import RealityKit
import simd

struct TubeGeometry {
    var positions: [SIMD3<Float>] = []
    var normals: [SIMD3<Float>] = []
    var uvs: [SIMD2<Float>] = []
    var indices: [UInt32] = []

    /// - Parameters:
    ///   - centers: ring centers, start → tip.
    ///   - twistNormals: per-ring reference direction (projected onto the ring plane).
    init(centers: [SIMD3<Float>], twistNormals: [SIMD3<Float>], radius: Float, sides: Int = 10) {
        let n = centers.count
        guard n >= 2, twistNormals.count == n, sides >= 3 else { return }
        let ringSize = sides + 1   // duplicate seam vertex for UV wrap

        for i in 0..<n {
            let tangent = simd_normalize(centers[min(n - 1, i + 1)] - centers[max(0, i - 1)])
            var normal = twistNormals[i] - tangent * simd_dot(twistNormals[i], tangent)
            if simd_length(normal) < 1e-6 { normal = abs(tangent.y) < 0.9 ? [0, 1, 0] : [1, 0, 0] }
            normal = simd_normalize(normal)
            let binormal = simd_cross(tangent, normal)
            for j in 0...sides {
                let angle = Float(j) / Float(sides) * 2 * .pi
                let radial = normal * cos(angle) + binormal * sin(angle)
                positions.append(centers[i] + radial * radius)
                normals.append(radial)
                uvs.append([Float(j) / Float(sides), Float(i) / Float(n - 1)])
            }
        }
        for i in 0..<(n - 1) {
            for j in 0..<sides {
                let a = UInt32(i * ringSize + j), b = a + UInt32(ringSize)
                indices += [a, b, a + 1, a + 1, b, b + 1]
            }
        }

        // Tip cap: fan around a center vertex.
        let tipTangent = simd_normalize(centers[n - 1] - centers[n - 2])
        let center = UInt32(positions.count)
        positions.append(centers[n - 1] + tipTangent * radius * 0.5)
        normals.append(tipTangent)
        uvs.append([0.5, 1])
        let lastRing = UInt32((n - 1) * ringSize)
        for j in 0..<UInt32(sides) {
            indices += [lastRing + j, center, lastRing + j + 1]
        }
    }

    func meshResource() throws -> MeshResource {
        var descriptor = MeshDescriptor()
        descriptor.positions = MeshBuffer(positions)
        descriptor.normals = MeshBuffer(normals)
        descriptor.textureCoordinates = MeshBuffer(uvs)
        descriptor.primitives = .triangles(indices)
        return try MeshResource.generate(from: [descriptor])
    }
}
