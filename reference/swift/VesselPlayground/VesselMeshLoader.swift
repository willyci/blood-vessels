//
//  VesselMeshLoader.swift
//  Mar2025VascCath
//
//  Minimal readers for the bundled vessel meshes: OBJ (v + f records only)
//  and binary STL. Faces are unwelded with flat normals — the meshes are
//  faceted anyway — and scaled from the files' millimetres to metres to
//  match VesselGraph.
//

import Foundation
import RealityKit
import simd

enum VesselMeshLoader {
    enum LoadError: Error { case missingResource, badSTL, empty }

    static func loadBundled(named name: String, extension ext: String,
                            unitScale: Float = 0.001) throws -> MeshResource {
        guard let url = Bundle.main.url(forResource: name, withExtension: ext) else {
            throw LoadError.missingResource
        }
        let triangles = ext.lowercased() == "stl"
            ? try stlTriangles(Data(contentsOf: url))
            : objTriangles(try String(contentsOf: url, encoding: .utf8))
        return try mesh(triangles.map { $0 * unitScale })
    }

    /// Triangle corners from OBJ text, three per face (polygons fanned).
    static func objTriangles(_ text: String) -> [SIMD3<Float>] {
        var vertices: [SIMD3<Float>] = []
        var corners: [SIMD3<Float>] = []
        for line in text.split(whereSeparator: \.isNewline) {
            let parts = line.split(whereSeparator: { $0 == " " || $0 == "\t" })
            guard let head = parts.first else { continue }
            if head == "v", parts.count >= 4,
               let x = Float(parts[1]), let y = Float(parts[2]), let z = Float(parts[3]) {
                vertices.append(SIMD3(x, y, z))
            } else if head == "f", parts.count >= 4 {
                let idx = parts.dropFirst().compactMap { Int($0.split(separator: "/")[0]) }
                    .map { $0 < 0 ? vertices.count + $0 : $0 - 1 }
                guard idx.count >= 3, idx.allSatisfy({ vertices.indices.contains($0) }) else { continue }
                for k in 1..<(idx.count - 1) {
                    corners += [vertices[idx[0]], vertices[idx[k]], vertices[idx[k + 1]]]
                }
            }
        }
        return corners
    }

    /// Triangle corners from a binary STL: 80-byte header, UInt32 count, then
    /// 50 bytes per facet (normal, three corners, attribute word).
    static func stlTriangles(_ data: Data) throws -> [SIMD3<Float>] {
        guard data.count >= 84 else { throw LoadError.badSTL }
        let count = Int(data.withUnsafeBytes { $0.loadUnaligned(fromByteOffset: 80, as: UInt32.self) })
        guard data.count >= 84 + count * 50 else { throw LoadError.badSTL }
        return data.withUnsafeBytes { bytes in
            var corners: [SIMD3<Float>] = []
            corners.reserveCapacity(count * 3)
            for facet in 0..<count {
                for corner in 0..<3 {
                    let offset = 84 + facet * 50 + 12 + corner * 12
                    corners.append(SIMD3(bytes.loadUnaligned(fromByteOffset: offset, as: Float.self),
                                         bytes.loadUnaligned(fromByteOffset: offset + 4, as: Float.self),
                                         bytes.loadUnaligned(fromByteOffset: offset + 8, as: Float.self)))
                }
            }
            return corners
        }
    }

    /// Flat-shaded mesh from triangle corners (three per face).
    static func mesh(_ corners: [SIMD3<Float>]) throws -> MeshResource {
        guard corners.count >= 3 else { throw LoadError.empty }
        var normals: [SIMD3<Float>] = []
        normals.reserveCapacity(corners.count)
        for t in stride(from: 0, to: corners.count - 2, by: 3) {
            let cross = simd_cross(corners[t + 1] - corners[t], corners[t + 2] - corners[t])
            let normal = simd_length(cross) > 0 ? simd_normalize(cross) : [0, 1, 0]
            normals += [normal, normal, normal]
        }
        let positions = Array(corners.prefix(normals.count))

        var descriptor = MeshDescriptor(name: "vessel")
        descriptor.positions = MeshBuffer(positions)
        descriptor.normals = MeshBuffer(normals)
        descriptor.primitives = .triangles((0..<UInt32(positions.count)).map { $0 })
        return try MeshResource.generate(from: [descriptor])
    }
}
