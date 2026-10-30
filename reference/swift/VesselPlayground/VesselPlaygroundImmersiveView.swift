//
//  VesselPlaygroundImmersiveView.swift
//  Mar2025VascCath
//
//  The selected vessel model floating in front of the user with the
//  guidewire and catheter inside it. Device meshes are rebuilt whenever the
//  model's insertion, twist, or route changes; the whole model is rebuilt
//  when the picker switches vessels.
//

import SwiftUI
import RealityKit

struct VesselPlaygroundImmersiveView: View {
    let model: CathWirePlaygroundModel
    let session: VesselPlaygroundSession

    @Environment(AppModel.self) private var appModel
    /// VesselStylusInput (visionOS 26+), kept alive for the scene's lifetime.
    @State private var stylusInput: AnyObject?

    private static let rootName = "vesselPlaygroundRoot"

    var body: some View {
        RealityView { content in
            let root = Entity()
            root.name = Self.rootName
            // Table height: the models lie flat, so the user looks down onto
            // them like a patient on the table rather than edge-on.
            root.position = [0, 0.9, -0.7]
            content.add(root)
            // Separate from the root so it stays level while the model rotates.
            content.add(VesselTable.make(topHeight: root.position.y))
            rebuildModel(in: root, content: content)
            if #available(visionOS 26.0, *) {
                let input = VesselStylusInput(model: model, session: session)
                stylusInput = input
                await input.attach(to: content, appModel: appModel)
            }
        } update: { content in
            guard let root = content.entities.first(where: { $0.name == Self.rootName }) else { return }
            // Picker switched models: the scaled child is named after its spec.
            if root.children.first?.name != model.spec.id.rawValue {
                rebuildModel(in: root, content: content)
            }
            for kind in FlexDevice.Kind.allCases {
                if let entity = root.findEntity(named: VesselSceneFactory.entityName(for: kind)) as? ModelEntity {
                    VesselSceneFactory.updateDevice(entity, device: model.device(kind), route: model.route)
                }
            }
            root.findEntity(named: VesselSceneFactory.overlayName)?.isEnabled = model.showCenterlines
        }
        .task(id: session.handInputEnabled) {
            guard session.handInputEnabled else { return }
            await VesselHandInput(model: model).run()
        }
        .task(id: session.stylusInputEnabled) {
            guard session.stylusInputEnabled, #available(visionOS 26.0, *) else { return }
            await GameControllerManager.shared.runSession()
        }
        .onAppear { session.spaceState = .open }
        .onDisappear {
            session.spaceState = .closed
            if #available(visionOS 26.0, *) { VesselStylusInput.detach() }
        }
    }

    /// root (world placement + flat, entry-right orientation) → scaled
    /// (display scale, named after the spec) → model frame (vessel centered
    /// on the root). Then the table is re-fitted under the new model.
    private func rebuildModel(in root: Entity, content: RealityViewContent) {
        let spec = model.spec
        root.children.removeAll()
        root.orientation = model.orientation

        let scaled = Entity()
        scaled.name = spec.id.rawValue
        scaled.scale = SIMD3(repeating: spec.displayScale)
        root.addChild(scaled)

        let modelFrame = Entity()
        scaled.addChild(modelFrame)

        let vessel = VesselSceneFactory.makeVessel(spec)
        modelFrame.addChild(vessel)
        modelFrame.position = -vessel.visualBounds(relativeTo: modelFrame).center

        for kind in FlexDevice.Kind.allCases {
            let entity = VesselSceneFactory.makeDevice(kind)
            VesselSceneFactory.updateDevice(entity, device: model.device(kind), route: model.route)
            modelFrame.addChild(entity)
        }
        let overlay = VesselSceneFactory.makeCenterlineOverlay(graph: model.graph)
        overlay.isEnabled = model.showCenterlines
        modelFrame.addChild(overlay)

        if let table = content.entities.first(where: { $0.name == VesselTable.name }) {
            VesselTable.fit(table, under: vessel, center: root.position)
        }
    }
}
