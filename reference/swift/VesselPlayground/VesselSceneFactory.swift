//
//  VesselSceneFactory.swift
//  Mar2025VascCath
//
//  Entity builders for the vessel playground: translucent vessel meshes, the
//  two device tubes (with a twist stripe), and the centerline debug overlay.
//

import RealityKit
import UIKit
import simd

@MainActor
enum VesselSceneFactory {
    static let vesselName = "vessel"
    static let overlayName = "centerlines"

    static func entityName(for kind: FlexDevice.Kind) -> String { "device-\(kind.rawValue)" }

    /// The model's meshes, translucent so the devices show through the wall
    /// (arteries red, veins blue).
    static func makeVessel(_ spec: VesselModelSpec) -> Entity {
        let group = Entity()
        group.name = vesselName
        for mesh in spec.meshes {
            do {
                let resource = try VesselMeshLoader.loadBundled(named: mesh.resource, extension: mesh.ext)
                group.addChild(ModelEntity(mesh: resource, materials: [vesselMaterial(mesh.tint)]))
            } catch {
                print("[VesselPlayground] \(mesh.resource).\(mesh.ext) failed to load: \(error)")
            }
        }
        return group
    }

    /// Empty device entity; its mesh is rebuilt by `updateDevice`.
    static func makeDevice(_ kind: FlexDevice.Kind) -> ModelEntity {
        let entity = ModelEntity()
        entity.name = entityName(for: kind)
        entity.model = ModelComponent(mesh: .generateSphere(radius: 0.0001), materials: [deviceMaterial(kind)])
        return entity
    }

    /// Devices are drawn this many times their real radius so a sub-mm wire
    /// is visible at arm's length. Rendering only: the path, tip bend and
    /// lumen clamp all still use the real radius.
    static let deviceDrawScale: Float = 2.5

    static func updateDevice(_ entity: ModelEntity, device: FlexDevice, route: VesselRoute) {
        let samples = DeviceShape.samples(for: device, on: route)
        let geometry = TubeGeometry(centers: samples.map(\.position),
                                    twistNormals: samples.map(\.twistNormal),
                                    radius: device.radius * deviceDrawScale)
        guard let mesh = try? geometry.meshResource() else { return }
        entity.model?.mesh = mesh
    }

    /// Centerlines as thin tubes, junctions yellow, ends green, access site blue.
    static func makeCenterlineOverlay(graph: VesselGraph) -> Entity {
        let overlay = Entity()
        overlay.name = overlayName
        let lineMaterial = UnlitMaterial(color: .systemYellow)
        for segment in graph.segments.values {
            let up: SIMD3<Float> = [0, 0, 1]
            let geometry = TubeGeometry(centers: segment.points,
                                        twistNormals: Array(repeating: up, count: segment.points.count),
                                        radius: 0.00025, sides: 6)
            if let mesh = try? geometry.meshResource() {
                overlay.addChild(ModelEntity(mesh: mesh, materials: [lineMaterial]))
            }
        }
        for node in graph.nodes.values {
            let color: UIColor = node.id == graph.insertionNode ? .systemBlue
                : node.kind == .junction ? .systemYellow : .systemGreen
            let marker = ModelEntity(mesh: .generateSphere(radius: 0.0008), materials: [UnlitMaterial(color: color)])
            marker.position = node.position
            overlay.addChild(marker)
        }
        return overlay
    }

    // MARK: - Materials

    private static func vesselMaterial(_ tint: VesselModelSpec.Mesh.Tint) -> PhysicallyBasedMaterial {
        var material = PhysicallyBasedMaterial()
        let color = tint == .artery ? UIColor(red: 0.85, green: 0.25, blue: 0.25, alpha: 1)
                                    : UIColor(red: 0.25, green: 0.4, blue: 0.9, alpha: 1)
        material.baseColor = .init(tint: color)
        material.roughness = 0.4
        material.blending = .transparent(opacity: 0.22)
        material.faceCulling = .none
        return material
    }

    /// Colors chosen to stand apart from the red/blue vessels and the white
    /// table: a near-black wire with a yellow twist stripe, and a bright
    /// green catheter (white stripe), translucent so the wire shows inside it.
    private static func deviceMaterial(_ kind: FlexDevice.Kind) -> PhysicallyBasedMaterial {
        var material = PhysicallyBasedMaterial()
        let base = kind == .wire ? UIColor(red: 0.08, green: 0.08, blue: 0.1, alpha: 1)
                                 : UIColor(red: 0.1, green: 0.8, blue: 0.35, alpha: 1)
        let stripe = kind == .wire ? UIColor.systemYellow : UIColor.white
        if let texture = stripeTexture(base: base, stripe: stripe) {
            material.baseColor = .init(tint: .white, texture: .init(texture))
        } else {
            material.baseColor = .init(tint: base)
        }
        material.metallic = .init(floatLiteral: kind == .wire ? 0.3 : 0)
        material.roughness = .init(floatLiteral: kind == .wire ? 0.3 : 0.5)
        if kind == .catheter {
            // See the wire running inside the catheter.
            material.blending = .transparent(opacity: 0.65)
        }
        return material
    }

    /// One stripe a quarter of the way around — rotates with twist.
    private static func stripeTexture(base: UIColor, stripe: UIColor) -> TextureResource? {
        let renderer = UIGraphicsImageRenderer(size: CGSize(width: 64, height: 4))
        let image = renderer.image { ctx in
            base.setFill(); ctx.fill(CGRect(x: 0, y: 0, width: 64, height: 4))
            stripe.setFill(); ctx.fill(CGRect(x: 0, y: 0, width: 16, height: 4))
        }
        guard let cg = image.cgImage else { return nil }
        return try? TextureResource(image: cg, options: .init(semantic: .color))
    }
}
