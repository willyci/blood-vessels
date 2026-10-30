//
//  TipShapePicker.swift
//  Mar2025VascCath
//
//  One segmented picker per device for its tip shape (TipShape). Switching
//  reshapes the tip in place; the device stays where it is.
//

import SwiftUI

struct TipShapePicker: View {
    let model: CathWirePlaygroundModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            ForEach(FlexDevice.Kind.allCases) { kind in
                HStack(spacing: 12) {
                    Label("\(kind.label) tip", systemImage: kind == .wire ? "line.diagonal" : "scribble")
                        .font(.headline)
                        .frame(width: 150, alignment: .leading)
                    Picker("\(kind.label) tip", selection: selection(for: kind)) {
                        ForEach(TipShape.options(for: kind)) { shape in
                            Text(shape.label).tag(shape)
                        }
                    }
                    .pickerStyle(.segmented)
                }
            }
        }
    }

    private func selection(for kind: FlexDevice.Kind) -> Binding<TipShape> {
        Binding(get: { model.device(kind).tip }, set: { model.setTip($0, for: kind) })
    }
}
