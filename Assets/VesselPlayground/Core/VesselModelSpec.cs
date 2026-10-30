// A vessel model the playground can load: how big it is drawn and how far the devices are
// scaled down to fit its vessels. The meshes and centerline file that go with it are
// assigned in the scene (VesselPlaygroundController). Port of VesselModelCatalog.swift.

using System;

namespace BloodVessels
{
    [Serializable]
    public sealed class VesselModelSpec
    {
        public string Title = "Tube";
        /// One line shown in the status panel: what the model is and where to enter.
        public string Note = "Branching tube. Devices enter on the right.";
        /// Model → world size.
        public float DisplayScale = 3;
        /// Device radius, tip and external length relative to the stock 0.035" wire /
        /// 5 Fr catheter, so they fit thin vessels.
        public float DeviceScale = 1;

        public static VesselModelSpec Tube => new();

        public static VesselModelSpec Avm => new()
        {
            Title = "AVM",
            Note = "Arteriovenous malformation. Devices enter through the feeder on the right.",
            DisplayScale = 4, DeviceScale = 0.35f,
        };

        public static VesselModelSpec Artery => new()
        {
            Title = "Artery",
            Note = "Devices run in the artery (red), entering on the right; the vein is shown in blue.",
            DisplayScale = 3.5f, DeviceScale = 0.5f,
        };

        public static VesselModelSpec Vein => new()
        {
            Title = "Vein",
            Note = "Devices run in the vein (blue), entering on the right; the artery is shown in red.",
            DisplayScale = 3.5f, DeviceScale = 0.5f,
        };
    }
}
