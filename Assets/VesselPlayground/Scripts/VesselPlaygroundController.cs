// Scene side of the vessel playground: loads the selected vessel model (meshes + centerline
// graph), lays it flat on the table with its access site on the right, and redraws the
// guidewire and catheter whenever the model changes. Mirrors the visionOS
// VesselPlaygroundImmersiveView + VesselSceneFactory.
//
// Hierarchy built at runtime under this object:
//   VesselModel (world placement + flat orientation) → Scaled (display scale)
//     → ModelFrame (vessel centered) → meshes, Wire, Catheter, Centerlines

using System;
using System.Collections.Generic;
using System.Linq;
using BloodVessels;
using UnityEngine;

[Serializable]
public sealed class VesselModelEntry
{
    public VesselModelSpec Spec = new();
    public TextAsset Centerlines;
    public GameObject[] ArteryMeshes = { };
    public GameObject[] VeinMeshes = { };
}

public sealed class VesselPlaygroundController : MonoBehaviour
{
    [Header("Models (picker order)")]
    public VesselModelEntry[] Models = { };

    [Header("Materials")]
    public Material ArteryMaterial;
    public Material VeinMaterial;
    public Material WireMaterial;
    public Material CatheterMaterial;
    public Material OverlayMaterial;

    [Header("Placement")]
    [Tooltip("Center of the tabletop surface (world).")]
    public Vector3 TableTopCenter = new(0, 0.9f, 0.7f);
    [Tooltip("Gap between the model's lowest point and the tabletop (m).")]
    public float Clearance = 0.005f;

    [Header("Rendering")]
    [Tooltip("Devices are drawn this many times their real radius so a sub-mm wire is visible. " +
             "Rendering only: the path, tip shape and lumen fit use the real radius.")]
    public float DeviceDrawScale = 2.5f;
    public bool ShowCenterlines;

    public CathWirePlaygroundModel Model { get; private set; }
    public int ModelIndex { get; private set; }
    public string LoadError { get; private set; }

    Transform modelRoot, modelFrame;
    GameObject overlay;
    readonly Dictionary<DeviceKind, Mesh> deviceMeshes = new();
    int drawnVersion = -1;

    void Awake() => Select(0);

    void LateUpdate()
    {
        if (Model == null) return;
        if (overlay != null && overlay.activeSelf != ShowCenterlines) overlay.SetActive(ShowCenterlines);
        if (Model.Version == drawnVersion) return;
        drawnVersion = Model.Version;
        foreach (var kind in DeviceKinds.All) RedrawDevice(kind);
    }

    // MARK: - Commands (also wired to the poke buttons)

    /// Loads model `index` (wrapping); devices go back to the access site, tips are kept.
    public void Select(int index)
    {
        if (Models.Length == 0) return;
        index = ((index % Models.Length) + Models.Length) % Models.Length;
        var entry = Models[index];
        try
        {
            var graph = VesselGraph.FromJson(entry.Centerlines.text, mirrorX: true);
            if (Model == null) Model = new CathWirePlaygroundModel(graph, entry.Spec);
            else Model.Load(entry.Spec, graph);
        }
        catch (Exception error)
        {
            LoadError = $"{entry.Spec.Title} could not be loaded: {error.Message}";
            Debug.LogError($"[VesselPlayground] {LoadError}");
            return;
        }
        LoadError = null;
        ModelIndex = index;
        BuildModel(entry);
    }

    public void NextModel() => Select(ModelIndex + 1);

    public void CycleTip(DeviceKind kind) => Model?.SetTip(Model.Device(kind).Tip.Next(kind), kind);
    public void CycleWireTip() => CycleTip(DeviceKind.Wire);
    public void CycleCatheterTip() => CycleTip(DeviceKind.Catheter);

    public void ToggleCenterlines() => ShowCenterlines = !ShowCenterlines;

    public void ResetDevices() => Model?.Reset();

    /// World direction the devices advance in at the access site: the chord over the
    /// route's first 2 cm (steadier than the very first tangent), turned by the model's
    /// room orientation — leftward, since the access site is on the right. Hand and
    /// controller pushes are measured along it.
    public Vector3 InsertionAxis
    {
        get
        {
            if (Model == null || modelRoot == null) return Vector3.left;
            var route = Model.Route;
            var chord = route.FrameAt(Mathf.Min(0.02f, route.Length)).Position - route.FrameAt(0).Position;
            return chord.sqrMagnitude > 1e-12f ? (modelRoot.rotation * chord).normalized : Vector3.left;
        }
    }

    // MARK: - Building

    void BuildModel(VesselModelEntry entry)
    {
        if (modelRoot != null) Remove(modelRoot.gameObject);
        deviceMeshes.Clear();

        modelRoot = new GameObject("VesselModel").transform;
        modelRoot.SetParent(transform, false);
        modelRoot.rotation = Model.Orientation;
        var scaled = new GameObject("Scaled").transform;
        scaled.SetParent(modelRoot, false);
        scaled.localScale = Vector3.one * entry.Spec.DisplayScale;
        modelFrame = new GameObject("ModelFrame").transform;
        modelFrame.SetParent(scaled, false);

        var vessel = new GameObject("Vessel").transform;
        vessel.SetParent(modelFrame, false);
        foreach (var mesh in entry.ArteryMeshes) AddMesh(mesh, vessel, ArteryMaterial);
        foreach (var mesh in entry.VeinMeshes) AddMesh(mesh, vessel, VeinMaterial);

        // Center the vessel on the root, then rest its lowest point on the table.
        var renderers = vessel.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            modelFrame.localPosition = -modelFrame.InverseTransformPoint(Bounds(renderers).center);
            var bounds = Bounds(renderers);
            modelRoot.position += new Vector3(TableTopCenter.x - bounds.center.x,
                TableTopCenter.y + Clearance - bounds.min.y, TableTopCenter.z - bounds.center.z);
        }

        foreach (var kind in DeviceKinds.All) deviceMeshes[kind] = MakeDevice(kind);
        overlay = MakeCenterlineOverlay(Model.Graph);
        overlay.SetActive(ShowCenterlines);
        drawnVersion = -1;
    }

    static Bounds Bounds(Renderer[] renderers)
    {
        var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    static void AddMesh(GameObject prefab, Transform parent, Material material)
    {
        if (prefab == null) return;
        var instance = Instantiate(prefab, parent, false);
        foreach (var r in instance.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
    }

    Mesh MakeDevice(DeviceKind kind)
    {
        var go = new GameObject(kind.Label());
        go.transform.SetParent(modelFrame, false);
        var mesh = new Mesh { name = kind.Label() };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = kind == DeviceKind.Wire ? WireMaterial : CatheterMaterial;
        return mesh;
    }

    void RedrawDevice(DeviceKind kind)
    {
        var device = Model.Device(kind);
        var samples = DeviceShape.Samples(device, Model.Route);
        new TubeGeometry(samples.Select(s => s.Position).ToList(), samples.Select(s => s.TwistNormal).ToList(),
            device.Radius * DeviceDrawScale).ApplyTo(deviceMeshes[kind]);
    }

    /// Centerlines as thin tubes; junctions yellow, ends green, access site blue.
    GameObject MakeCenterlineOverlay(VesselGraph graph)
    {
        var root = new GameObject("Centerlines");
        root.transform.SetParent(modelFrame, false);
        foreach (var segment in graph.Segments.Values)
        {
            var go = new GameObject($"Segment {segment.Id}");
            go.transform.SetParent(root.transform, false);
            var mesh = new Mesh();
            new TubeGeometry(segment.Points, Enumerable.Repeat(Vector3.forward, segment.Points.Length).ToList(),
                0.00025f, sides: 6).ApplyTo(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = OverlayMaterial;
            SetColor(go, Color.yellow);
        }
        foreach (var node in graph.Nodes.Values)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Remove(marker.GetComponent<Collider>());
            marker.name = $"Node {node.Id}";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = node.Position;
            marker.transform.localScale = Vector3.one * 0.0016f;
            marker.GetComponent<Renderer>().sharedMaterial = OverlayMaterial;
            SetColor(marker, node.Id == graph.InsertionNode ? new Color(0.2f, 0.5f, 1f)
                : node.Kind == NodeKind.Junction ? Color.yellow : Color.green);
        }
        return root;
    }

    /// Destroy that also works when the scene is driven from the Editor (previews, tools).
    static void Remove(UnityEngine.Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    static void SetColor(GameObject go, Color color)
    {
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        go.GetComponent<Renderer>().SetPropertyBlock(block);
    }
}
