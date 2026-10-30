// Live readout floating behind the table: model, each device's depth / twist / tip shape,
// the route, and what the leading tip is doing. Port of VesselRouteStatusView.swift.

using System.Text;
using BloodVessels;
using UnityEngine;

public sealed class VesselStatusPanel : MonoBehaviour
{
    public VesselPlaygroundController Playground;
    public TextMesh Text;

    int shownVersion = -1;
    int shownModel = -1;
    DeviceKind shownActive;
    readonly StringBuilder builder = new();

    void Update()
    {
        var model = Playground != null ? Playground.Model : null;
        if (Text == null) return;
        if (model == null)
        {
            Text.text = Playground != null && Playground.LoadError != null ? Playground.LoadError : "Loading…";
            return;
        }
        if (model.Version == shownVersion && Playground.ModelIndex == shownModel && model.ActiveDevice == shownActive) return;
        shownVersion = model.Version;
        shownModel = Playground.ModelIndex;
        shownActive = model.ActiveDevice;

        builder.Clear();
        builder.AppendLine($"<b>{model.Spec.Title}</b>  ·  {model.Spec.Note}");
        foreach (var kind in DeviceKinds.All)
        {
            var d = model.Device(kind);
            string lead = model.Leader == kind ? "  <color=#ffa040>leading</color>" : "";
            string hand = kind == DeviceKind.Wire ? "right" : "left";
            string active = model.ActiveDevice == kind ? " *" : "";
            builder.AppendLine($"{kind.Label()}{active} ({hand}): {d.Insertion * 100:0.0} cm · {d.TwistDegrees}° · {d.Tip.Label()} tip{lead}");
        }
        string junctions = model.JunctionsPassed == 1 ? "junction" : "junctions";
        builder.AppendLine($"{model.RouteDescription} · {model.JunctionsPassed} {junctions} passed");
        builder.Append(model.TipAtJunction ? "<color=#ffe040>At a junction — twist to aim the tip, then push</color>"
            : model.TipAtDeadEnd ? "<color=#ffa040>End of vessel — pull back to choose another branch</color>"
            : "Advancing");
        Text.text = builder.ToString();
    }
}
