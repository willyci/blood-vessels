//
//  FlexDevice.swift
//  Mar2025VascCath
//
//  A flexible endovascular device (guidewire or catheter) that follows the
//  vessel route. Its shape is the route itself plus a pre-shaped tip
//  (TipShape) whose bend direction rotates with `twist` — that tip is what
//  steers into branches.
//

import Foundation

struct FlexDevice: Equatable, Sendable {
    enum Kind: String, CaseIterable, Identifiable, Sendable {
        case wire, catheter
        var id: String { rawValue }
        var label: String { self == .wire ? "Wire" : "Catheter" }
    }

    let kind: Kind
    /// Tip distance past the access site along the route (m).
    var insertion: Float = 0
    /// Rotation about the device axis (rad), relative to the route's
    /// parallel-transport frame. Unbounded so continuous twisting stays smooth.
    var twist: Float = 0

    let radius: Float
    var tip: TipShape
    /// Tip size relative to the stock shape (the model's device scale).
    var tipScale: Float = 1
    /// Length drawn outside the vessel, behind the access site.
    let externalLength: Float

    /// 0.035" (0.9 mm) angled guidewire.
    static let guidewire = FlexDevice(kind: .wire, radius: 0.00045, tip: .angled, externalLength: 0.09)

    /// 5 Fr (1.7 mm) Berenstein catheter.
    static let catheter = FlexDevice(kind: .catheter, radius: 0.00085, tip: .bern, externalLength: 0.06)

    /// Same device with radius, tip size and external length scaled, for
    /// vessels thinner than the stock device (tip angles unchanged).
    func scaled(by factor: Float) -> FlexDevice {
        FlexDevice(kind: kind, insertion: insertion, twist: twist, radius: radius * factor,
                   tip: tip, tipScale: tipScale * factor, externalLength: externalLength * factor)
    }

    /// Twist wrapped to 0..<360 for display.
    var twistDegrees: Int {
        let deg = Int((twist * 180 / .pi).rounded()) % 360
        return deg < 0 ? deg + 360 : deg
    }
}
