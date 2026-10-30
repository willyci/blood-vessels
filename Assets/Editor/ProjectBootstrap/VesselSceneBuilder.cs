using System.IO;
using BloodVessels;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace ProjectBootstrap
{
    // Builds Assets/Scenes/VesselPlayground.unity: Meta camera rig with passthrough and tracked
    // hands, a procedure table, the live vessel playground (VesselPlaygroundController + input),
    // a status panel and poke buttons. Mirrors the visionOS playground's layout.
    //   Menu: Tools > Blood Vessels > Rebuild Vessel Playground Scene
    //   Headless: Unity -batchmode -buildTarget Android -projectPath <repo>
    //             -executeMethod ProjectBootstrap.VesselSceneBuilder.BuildHeadless -quit
    public static class VesselSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/VesselPlayground.unity";
        const string MaterialFolder = "Assets/Vessels/Materials";
        const string TextureFolder = "Assets/Vessels/Textures";
        const string ModelFolder = "Assets/Vessels/Models";
        const string CenterlineFolder = "Assets/Vessels/Centerlines";
        const string RigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string HandPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";

        // Table (metres), matching the visionOS VesselTable.
        const float TableTop = 0.9f, TableDistance = 0.7f;
        const float TableWidth = 1.2f, TableDepth = 0.7f, TableThickness = 0.03f;
        const float LegInset = 0.06f, LegSize = 0.04f;

        [MenuItem("Tools/Blood Vessels/Rebuild Vessel Playground Scene")]
        public static void BuildFromMenu() => Build();

        public static void BuildHeadless()
        {
            QuestSetup.ApplySettings();
            Build();
        }

        static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var rig = BuildRig();
            BuildTable();
            var playground = BuildPlayground();
            BuildInput(rig, playground);
            BuildStatusPanel(playground);
            BuildButtons(playground);

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[VesselSceneBuilder] Saved {ScenePath}.");
        }

        // MARK: - Camera rig, passthrough, hands

        static GameObject BuildRig()
        {
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab));
            rig.GetComponent<OVRManager>().isInsightPassthroughEnabled = true;
            rig.AddComponent<OVRPassthroughLayer>();   // underlay: the real room behind the scene

            // Passthrough shows wherever the camera clears to transparent.
            var eye = rig.transform.Find("TrackingSpace/CenterEyeAnchor").GetComponent<Camera>();
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = Color.clear;

            AddHand(rig.transform.Find("TrackingSpace/LeftHandAnchor"), right: false);
            AddHand(rig.transform.Find("TrackingSpace/RightHandAnchor"), right: true);
            return rig;
        }

        static void AddHand(Transform anchor, bool right)
        {
            var hand = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefab), anchor);
            hand.name = right ? "OVRHandPrefab (Right)" : "OVRHandPrefab (Left)";
            if (!right) return;   // the prefab defaults to the left hand
            SetInt(hand.GetComponent<OVRHand>(), "HandType", 1);
            SetInt(hand.GetComponent<OVRSkeleton>(), "_skeletonType", 1);
            SetInt(hand.GetComponent<OVRMesh>(), "_meshType", 1);
        }

        static void SetInt(Object target, string property, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // MARK: - Table

        static void BuildTable()
        {
            var table = new GameObject("Table").transform;
            table.position = new Vector3(0, TableTop, TableDistance);

            var topMaterial = OpaqueMaterial("TableTop", new Color(0.95f, 0.95f, 0.93f), 0.2f);
            var legMaterial = OpaqueMaterial("TableLeg", new Color(0.7f, 0.72f, 0.75f), 0.7f, metallic: 0.8f);

            Box("Top", table, new Vector3(0, -TableThickness / 2, 0),
                new Vector3(TableWidth, TableThickness, TableDepth), topMaterial);
            float legHeight = TableTop - TableThickness;
            float dx = TableWidth / 2 - LegInset, dz = TableDepth / 2 - LegInset;
            foreach (var (x, z) in new[] { (-dx, -dz), (dx, -dz), (-dx, dz), (dx, dz) })
                Box("Leg", table, new Vector3(x, -TableThickness - legHeight / 2, z),
                    new Vector3(LegSize, legHeight, LegSize), legMaterial);
        }

        static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
        }

        // MARK: - Playground

        static VesselPlaygroundController BuildPlayground()
        {
            var playground = new GameObject("Vessel Playground").AddComponent<VesselPlaygroundController>();
            playground.TableTopCenter = new Vector3(0, TableTop, TableDistance);

            var tube = Model("VesselTube");
            var avm = Model("avm_vessel_simple");
            var artery = Model("artery");
            var vein = Model("vein");
            playground.Models = new[]
            {
                Entry(VesselModelSpec.Tube, "VesselCenterlines", new[] { tube }, new GameObject[0]),
                Entry(VesselModelSpec.Avm, "AVMCenterlines", new[] { avm }, new GameObject[0]),
                Entry(VesselModelSpec.Artery, "ArteryCenterlines", new[] { artery }, new[] { vein }),
                Entry(VesselModelSpec.Vein, "VeinCenterlines", new[] { artery }, new[] { vein }),
            };

            playground.ArteryMaterial = TranslucentMaterial("VesselArtery", new Color(0.85f, 0.25f, 0.25f, 0.3f));
            playground.VeinMaterial = TranslucentMaterial("VesselVein", new Color(0.25f, 0.4f, 0.9f, 0.3f));
            // Near-black wire with a yellow twist stripe; bright green catheter with a white
            // stripe, translucent so the wire shows inside it.
            playground.WireMaterial = DeviceMaterial("DeviceWire", new Color(0.08f, 0.08f, 0.1f), Color.yellow, 1f, 0.3f);
            playground.CatheterMaterial = DeviceMaterial("DeviceCatheter", new Color(0.1f, 0.8f, 0.35f), Color.white, 0.65f, 0f);
            var overlay = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "CenterlineOverlay" };
            overlay.SetColor("_BaseColor", Color.yellow);
            playground.OverlayMaterial = Save(overlay);
            return playground;
        }

        static GameObject Model(string file) => AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelFolder}/{file}.obj");

        static VesselModelEntry Entry(VesselModelSpec spec, string centerlines, GameObject[] arteries, GameObject[] veins) => new()
        {
            Spec = spec,
            Centerlines = AssetDatabase.LoadAssetAtPath<TextAsset>($"{CenterlineFolder}/{centerlines}.json"),
            ArteryMeshes = arteries,
            VeinMeshes = veins,
        };

        static void BuildInput(GameObject rig, VesselPlaygroundController playground)
        {
            var input = playground.gameObject.AddComponent<VesselDeviceInput>();
            input.Playground = playground;
            input.LeftHandAnchor = rig.transform.Find("TrackingSpace/LeftHandAnchor");
            input.RightHandAnchor = rig.transform.Find("TrackingSpace/RightHandAnchor");
            input.LeftHand = input.LeftHandAnchor.GetComponentInChildren<OVRHand>();
            input.RightHand = input.RightHandAnchor.GetComponentInChildren<OVRHand>();
            input.LeftSkeleton = input.LeftHandAnchor.GetComponentInChildren<OVRSkeleton>();
            input.RightSkeleton = input.RightHandAnchor.GetComponentInChildren<OVRSkeleton>();
        }

        // MARK: - Status panel and buttons

        static Font UiFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static TextMesh Label(string name, Transform parent, string text, float size, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMesh>();
            label.font = UiFont;
            label.GetComponent<MeshRenderer>().sharedMaterial = UiFont.material;
            label.text = text;
            label.fontSize = 64;
            label.characterSize = size;
            label.anchor = anchor;
            label.alignment = anchor == TextAnchor.MiddleCenter ? TextAlignment.Center : TextAlignment.Left;
            label.richText = true;
            label.color = Color.white;
            return label;
        }

        static void BuildStatusPanel(VesselPlaygroundController playground)
        {
            // Floats above the far edge of the table, readable from where the user stands.
            var panel = new GameObject("Status Panel").transform;
            panel.position = new Vector3(0, TableTop + 0.32f, TableDistance + TableDepth / 2 + 0.02f);
            var backing = Box("Backing", panel, new Vector3(0, 0, 0.005f), new Vector3(0.9f, 0.22f, 0.005f),
                OpaqueMaterial("PanelBacking", new Color(0.08f, 0.1f, 0.13f), 0.3f));
            backing.name = "Backing";
            var status = panel.gameObject.AddComponent<VesselStatusPanel>();
            status.Playground = playground;
            status.Text = Label("Text", panel, "Loading…", 0.0035f, TextAnchor.MiddleLeft);
            status.Text.transform.localPosition = new Vector3(-0.43f, 0, 0);

            var help = Label("Help", panel,
                "Pinch (hands) or hold trigger (controllers) and move along the vessel to push, roll your wrist to twist. " +
                "Left = catheter, right = wire.\nSticks: push / twist.  A/B: wire / catheter tip.  X: model.  Y: centerlines.  Stick click: reset.",
                0.003f, TextAnchor.UpperCenter);
            help.transform.localPosition = new Vector3(0, -0.122f, 0);
            help.alignment = TextAlignment.Center;
        }

        static void BuildButtons(VesselPlaygroundController playground)
        {
            // A row along the user's edge of the table, left of center.
            var row = new GameObject("Buttons").transform;
            row.position = new Vector3(-0.3f, TableTop, TableDistance - TableDepth / 2 + 0.07f);
            var baseMaterial = OpaqueMaterial("ButtonBase", new Color(0.15f, 0.17f, 0.2f), 0.4f);
            var capMaterial = OpaqueMaterial("ButtonCap", new Color(0.2f, 0.25f, 0.3f), 0.5f);

            (string label, UnityAction action)[] buttons =
            {
                ("Model", playground.NextModel),
                ("Wire tip", playground.CycleWireTip),
                ("Catheter tip", playground.CycleCatheterTip),
                ("Centerlines", playground.ToggleCenterlines),
                ("Reset", playground.ResetDevices),
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = new GameObject($"Button {buttons[i].label}").transform;
                button.SetParent(row, false);
                button.localPosition = new Vector3((i - 2) * 0.1f, 0, 0);
                Box("Base", button, new Vector3(0, 0.006f, 0), new Vector3(0.065f, 0.012f, 0.065f), baseMaterial);
                var cap = Box("Cap", button, new Vector3(0, 0.016f, 0), new Vector3(0.05f, 0.008f, 0.05f), capMaterial);

                var poke = button.gameObject.AddComponent<VesselPokeButton>();
                poke.Cap = cap.GetComponent<Renderer>();
                poke.TouchHalfExtents = new Vector3(0.03f, 0.03f, 0.03f);
                UnityEventTools.AddPersistentListener(poke.OnPress, buttons[i].action);

                // Label lying just behind the button, tilted up toward the user.
                var label = Label("Label", button, buttons[i].label, 0.0026f, TextAnchor.MiddleCenter);
                label.transform.localPosition = new Vector3(0, 0.003f, 0.045f);
                label.transform.localRotation = Quaternion.Euler(60, 0, 0);
            }
        }

        // MARK: - Materials and textures (saved as assets so they can be tuned in the Inspector)

        static Material OpaqueMaterial(string name, Color color, float smoothness, float metallic = 0)
        {
            var material = NewLit(name);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            return Save(material);
        }

        static Material TranslucentMaterial(string name, Color color)
        {
            var material = NewLit(name);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.6f);
            MakeTransparent(material);
            material.SetFloat("_Cull", (float)CullMode.Off);   // see the far wall through the near one
            return Save(material);
        }

        static Material DeviceMaterial(string name, Color baseColor, Color stripe, float opacity, float metallic)
        {
            var material = NewLit(name);
            material.SetTexture("_BaseMap", StripeTexture(name + "Stripe", baseColor, stripe));
            material.SetColor("_BaseColor", new Color(1, 1, 1, opacity));
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", 0.6f);
            if (opacity < 1) MakeTransparent(material);
            return Save(material);
        }

        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1);   // transparent
            material.SetFloat("_Blend", 0);     // alpha
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        /// One stripe a quarter of the way around — rotates with twist.
        static Texture2D StripeTexture(string name, Color baseColor, Color stripe)
        {
            if (!AssetDatabase.IsValidFolder(TextureFolder)) AssetDatabase.CreateFolder("Assets/Vessels", "Textures");
            var path = $"{TextureFolder}/{name}.png";
            var texture = new Texture2D(64, 4, TextureFormat.RGBA32, false);
            for (int x = 0; x < 64; x++)
            for (int y = 0; y < 4; y++)
                texture.SetPixel(x, y, x < 16 ? stripe : baseColor);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material NewLit(string name) =>
            new(Shader.Find("Universal Render Pipeline/Lit")) { name = name };

        static Material Save(Material material)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Vessels", "Materials");
            var path = $"{MaterialFolder}/{material.name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
            existing.shader = material.shader;
            existing.CopyPropertiesFromMaterial(material);   // keep the asset GUID on rebuilds
            existing.shaderKeywords = material.shaderKeywords;
            existing.renderQueue = material.renderQueue;
            existing.SetOverrideTag("RenderType", material.GetTag("RenderType", false));
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
