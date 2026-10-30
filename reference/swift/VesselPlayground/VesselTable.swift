//
//  VesselTable.swift
//  Mar2025VascCath
//
//  A simple procedure table under the vessel model: a white matte top, so the
//  translucent red/blue vessels read with full color and the dark wire and
//  green catheter stand out against it,
//  on brushed-steel legs down to the floor. It stays level in world space
//  (the model above it is rotated to lie flat) and is re-fitted under the
//  model's lowest point whenever the picker switches models.
//

import RealityKit
import simd

@MainActor
enum VesselTable {
    static let name = "vesselTable"

    static let width: Float = 1.2
    static let depth: Float = 0.7
    static let topThickness: Float = 0.03
    /// Space between the model's lowest point and the tabletop.
    static let clearance: Float = 0.005

    private static let legInset: Float = 0.06
    private static let legSize: Float = 0.04

    /// Table whose origin is the center of its top surface; legs are built
    /// for a top at `topHeight` above the floor.
    static func make(topHeight: Float) -> Entity {
        let table = Entity()
        table.name = name

        let top = ModelEntity(
            mesh: .generateBox(width: width, height: topThickness, depth: depth, cornerRadius: 0.01),
            materials: [CathLabPalette.material(CathLabPalette.shellWhite)]
        )
        top.position.y = -topThickness / 2
        table.addChild(top)

        let legHeight = max(topHeight - topThickness, 0.01)
        let legMaterial = CathLabPalette.material(CathLabPalette.brushed)
        let dx = width / 2 - legInset, dz = depth / 2 - legInset
        for (x, z) in [(-dx, -dz), (dx, -dz), (-dx, dz), (dx, dz)] {
            let leg = ModelEntity(
                mesh: .generateBox(width: legSize, height: legHeight, depth: legSize, cornerRadius: 0.006),
                materials: [legMaterial]
            )
            leg.position = [x, -topThickness - legHeight / 2, z]
            table.addChild(leg)
        }
        return table
    }

    /// Replaces `table`'s geometry so its top sits just under `vessel`
    /// (world space), centered below `center`.
    static func fit(_ table: Entity, under vessel: Entity, center: SIMD3<Float>) {
        let bottom = vessel.visualBounds(relativeTo: nil).min.y
        let topHeight = bottom - clearance
        table.children.removeAll()
        for child in Array(make(topHeight: topHeight).children) { table.addChild(child) }
        table.position = [center.x, topHeight, center.z]
    }
}
