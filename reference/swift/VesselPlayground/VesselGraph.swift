//
//  VesselGraph.swift
//  Mar2025VascCath
//
//  Centerline graph of the vessel lumen, loaded from VesselCenterlines.json
//  (generated once by scripts/extract_centerlines.swift). Positions are
//  converted from the file's millimetres to metres in the OBJ model frame.
//

import Foundation
import simd

struct VesselGraph: Sendable {
    enum NodeKind: String, Codable, Sendable { case end, junction }

    struct Node: Sendable {
        let id: Int
        let kind: NodeKind
        let position: SIMD3<Float>
    }

    /// Polyline from node `from` to node `to`, sampled about every 1 mm.
    struct Segment: Sendable {
        let id: Int
        let from: Int
        let to: Int
        let points: [SIMD3<Float>]
        let radii: [Float]

        var length: Float {
            zip(points, points.dropFirst()).reduce(0) { $0 + simd_distance($1.0, $1.1) }
        }

        func otherEnd(from node: Int) -> Int { node == from ? to : from }
    }

    let nodes: [Int: Node]
    let segments: [Int: Segment]
    /// Access site — the open end the devices enter through.
    let insertionNode: Int

    /// Segments that meet at `node`.
    func segments(at node: Int) -> [Segment] {
        segments.values.filter { $0.from == node || $0.to == node }.sorted { $0.id < $1.id }
    }

    // MARK: - Loading

    private struct File: Decodable {
        struct FileNode: Decodable { let id: Int; let kind: NodeKind; let pos: [Float] }
        struct FileSegment: Decodable { let id: Int; let from: Int; let to: Int; let points: [[Float]]; let radii: [Float] }
        let units: String
        let insertionNode: Int
        let nodes: [FileNode]
        let segments: [FileSegment]
    }

    enum LoadError: Error { case missingResource, badUnits(String), badInsertionNode }

    init(json data: Data) throws {
        let file = try JSONDecoder().decode(File.self, from: data)
        let scale: Float
        switch file.units {
        case "mm": scale = 0.001
        case "m": scale = 1
        default: throw LoadError.badUnits(file.units)
        }
        func vec(_ a: [Float]) -> SIMD3<Float> { SIMD3(a[0], a[1], a[2]) * scale }

        nodes = Dictionary(uniqueKeysWithValues: file.nodes.map {
            ($0.id, Node(id: $0.id, kind: $0.kind, position: vec($0.pos)))
        })
        segments = Dictionary(uniqueKeysWithValues: file.segments.map {
            ($0.id, Segment(id: $0.id, from: $0.from, to: $0.to,
                            points: $0.points.map(vec), radii: $0.radii.map { $0 * scale }))
        })
        guard nodes[file.insertionNode]?.kind == .end else { throw LoadError.badInsertionNode }
        insertionNode = file.insertionNode
    }

    static func loadBundled(named name: String = "VesselCenterlines") throws -> VesselGraph {
        guard let url = Bundle.main.url(forResource: name, withExtension: "json") else {
            throw LoadError.missingResource
        }
        return try VesselGraph(json: Data(contentsOf: url))
    }
}
