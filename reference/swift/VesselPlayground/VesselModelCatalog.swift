//
//  VesselModelCatalog.swift
//  Mar2025VascCath
//
//  The vessel models the playground can load. Each pairs a centerline file
//  (the route the devices follow, from scripts/extract_centerlines.swift)
//  with the meshes drawn around it, plus how it sits in the room.
//
//  Artery and vein are separate closed vessels with no shared lumen, so
//  each gets its own entry: both are drawn, the devices run in the one
//  named.
//

import Foundation

struct VesselModelSpec: Identifiable, Sendable {
    enum ID: String, CaseIterable, Identifiable, Sendable {
        case tube, avm, artery, vein
        var id: String { rawValue }
    }

    struct Mesh: Sendable {
        enum Tint: Sendable { case artery, vein }
        let resource: String
        let ext: String
        let tint: Tint
    }

    let id: ID
    let title: String
    /// One line under the picker: what the model is and where to enter.
    let note: String
    /// Centerline JSON resource (no extension).
    let centerlines: String
    let meshes: [Mesh]
    /// Model → world size. The new models are 11–14 cm across.
    let displayScale: Float
    /// Device radius, tip and external length relative to the stock
    /// 0.035" wire / 5 Fr catheter, so they fit thin vessels.
    let deviceScale: Float
    // Orientation isn't listed: every model is laid flat with its access
    // site on the right (VesselLayout).
}

extension VesselModelSpec {
    static let all: [VesselModelSpec] = [tube, avm, artery, vein]

    static func spec(_ id: ID) -> VesselModelSpec { all.first { $0.id == id }! }

    /// Tinkercad pipe; access at the trunk's max-y end.
    static let tube = VesselModelSpec(
        id: .tube, title: "Tube",
        note: "Branching tube. Devices enter on the right.",
        centerlines: "VesselCenterlines",
        meshes: [Mesh(resource: "VesselTube", ext: "obj", tint: .artery)],
        displayScale: 3, deviceScale: 1)

    /// AVM, nearly flat in its XY plane. Access: the feeder at its
    /// bottom-right corner (+x, -y).
    static let avm = VesselModelSpec(
        id: .avm, title: "AVM",
        note: "Arteriovenous malformation. Devices enter through the feeder on the right.",
        centerlines: "AVMCenterlines",
        meshes: [Mesh(resource: "avm_vessel_simple", ext: "obj", tint: .artery)],
        displayScale: 4, deviceScale: 0.35)

    /// Artery + vein pair, flat in its XZ plane. Access at the low-x, low-z
    /// end of the paired trunks.
    private static let pairMeshes = [Mesh(resource: "artery", ext: "stl", tint: .artery),
                                     Mesh(resource: "vein", ext: "stl", tint: .vein)]

    static let artery = VesselModelSpec(
        id: .artery, title: "Artery",
        note: "Devices run in the artery (red), entering on the right; the vein is shown in blue.",
        centerlines: "ArteryCenterlines", meshes: pairMeshes,
        displayScale: 3.5, deviceScale: 0.5)

    static let vein = VesselModelSpec(
        id: .vein, title: "Vein",
        note: "Devices run in the vein (blue), entering on the right; the artery is shown in red.",
        centerlines: "VeinCenterlines", meshes: pairMeshes,
        displayScale: 3.5, deviceScale: 0.5)
}
