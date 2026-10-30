//
//  DeviceControlPanel.swift
//  Mar2025VascCath
//
//  On-screen push/twist controls for the selected device. Every button
//  repeats while held, so a long press advances or rotates continuously.
//

import SwiftUI

struct DeviceControlPanel: View {
    @Bindable var model: CathWirePlaygroundModel

    private static let pushSteps: [Float] = [-0.005, -0.001, 0.001, 0.005]   // m
    private static let twistSteps: [Float] = [-15, -3, 3, 15]                // degrees

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Picker("Device", selection: $model.activeDevice) {
                ForEach(FlexDevice.Kind.allCases) { kind in
                    Text(kind.label).tag(kind)
                }
            }
            .pickerStyle(.segmented)

            controlRow(title: "Push", systemImage: "arrow.left.and.right") {
                ForEach(Self.pushSteps, id: \.self) { step in
                    RepeatingStepButton(label: Self.pushLabel(step)) {
                        model.push(model.activeDevice, by: step)
                    }
                }
            }

            controlRow(title: "Twist", systemImage: "arrow.triangle.2.circlepath") {
                ForEach(Self.twistSteps, id: \.self) { step in
                    RepeatingStepButton(label: step > 0 ? "+\(Int(step))°" : "\(Int(step))°") {
                        model.twist(model.activeDevice, by: step * .pi / 180)
                    }
                }
            }
        }
        .padding(20)
        .background(.regularMaterial, in: .rect(cornerRadius: 16))
    }

    private func controlRow<Buttons: View>(title: String, systemImage: String,
                                           @ViewBuilder buttons: () -> Buttons) -> some View {
        HStack(spacing: 12) {
            Label(title, systemImage: systemImage)
                .font(.headline)
                .frame(width: 110, alignment: .leading)
            buttons()
        }
    }

    /// "Pull 5 mm" style labels: negative = retract.
    private static func pushLabel(_ step: Float) -> String {
        let mm = Int((abs(step) * 1000).rounded())
        return step < 0 ? "− \(mm) mm" : "+ \(mm) mm"
    }
}

/// Fixed-width step button that repeats while held.
private struct RepeatingStepButton: View {
    let label: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(label)
                .font(.body.monospacedDigit())
                .frame(width: 84)
                .padding(.vertical, 6)
        }
        .buttonStyle(.bordered)
        .buttonRepeatBehavior(.enabled)
    }
}
