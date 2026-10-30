//
//  VesselModelPicker.swift
//  Mar2025VascCath
//
//  Segmented picker for the vessel model (VesselModelCatalog). Switching
//  reloads the centerlines and puts both devices back at the access site;
//  the immersive view rebuilds the scene to match.
//

import SwiftUI

struct VesselModelPicker: View {
    let model: CathWirePlaygroundModel

    @State private var failedModel: String?

    private var selection: Binding<VesselModelSpec.ID> {
        Binding(
            get: { model.spec.id },
            set: { id in
                failedModel = VesselPlayground.select(id) ? nil : VesselModelSpec.spec(id).title
            }
        )
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Picker("Vessel model", selection: selection) {
                ForEach(VesselModelSpec.all) { spec in
                    Text(spec.title).tag(spec.id)
                }
            }
            .pickerStyle(.segmented)

            Text(failedModel.map { "\($0) could not be loaded." } ?? model.spec.note)
                .font(.callout)
                .foregroundStyle(failedModel == nil ? .secondary : Color.red)
        }
    }
}
