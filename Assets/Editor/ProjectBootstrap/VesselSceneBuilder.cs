using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectBootstrap
{
    // Builds Assets/Scenes/VesselPlayground.unity: Meta camera rig with passthrough and tracked
    // hands, a procedure table, and the vessel models lying flat on it (Tube shown, the others
    // inactive under "Vessels"). Mirrors the visionOS playground's table layout.
    //   Menu: Tools > Blood Vessels > Rebuild Vessel Playground Scene
    //   Headless: Unity -batchmode -buildTarget Android -projectPath <repo>
    //             -executeMethod ProjectBootstrap.VesselSceneBuilder.BuildHeadless -quit
    public static class VesselSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/VesselPlayground.unity";
        const string MaterialFolder = "Assets/Vessels/Materials";
        const string ModelFolder = "Assets/Vessels/Models";
        const string RigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string HandPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";

        // Table (metres), matching the visionOS VesselTable.
        const float TableTop = 0.9f, TableDistance = 0.7f;
        const float TableWidth = 1.2f, TableDepth = 0.7f, TableThickness = 0.03f;
        const float LegInset = 0.06f, LegSize = 0.04f, Clearance = 0.005f;

        struct ModelSpec
        {
            public string Name;
            public (string File, bool Vein)[] Meshes;
            public float DisplayScale;
        }

        // Same catalog and display scales as VesselModelCatalog.swift.
        static readonly ModelSpec[] Models =
        {
            new() { Name = "Tube", Meshes = new[] { ("VesselTube", false) }, DisplayScale = 3f },
            new() { Name = "AVM", Meshes = new[] { ("avm_vessel_simple", false) }, DisplayScale = 4f },
            new() { Name = "Artery + Vein", Meshes = new[] { ("artery", false), ("vein", true) }, DisplayScale = 3.5f },
        };

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

            BuildRig();
            BuildTable();
            BuildVessels();

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[VesselSceneBuilder] Saved {ScenePath}.");
        }

        // MARK: - Camera rig, passthrough, hands

        static void BuildRig()
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
        }

        static void AddHand(Transform anchor, bool right)
        {
            var hand = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefab), anchor);
            hand.name = right ? "OVRHandPrefab (Right)" : "OVRHandPrefab (Left)";
            if (!right) return;   // the prefab defaults to the left hand
            SetEnum(hand.GetComponent<OVRHand>(), "HandType", 1);
            SetEnum(hand.GetComponent<OVRSkeleton>(), "_skeletonType", 1);
            SetEnum(hand.GetComponent<OVRMesh>(), "_meshType", 1);
        }

        static void SetEnum(Object target, string property, int value)
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

        static void Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
        }

        // MARK: - Vessels

        static void BuildVessels()
        {
            var artery = TranslucentMaterial("VesselArtery", new Color(0.85f, 0.25f, 0.25f, 0.35f));
            var vein = TranslucentMaterial("VesselVein", new Color(0.25f, 0.4f, 0.9f, 0.35f));
            var root = new GameObject("Vessels").transform;

            for (int i = 0; i < Models.Length; i++)
            {
                var spec = Models[i];
                var model = new GameObject(spec.Name).transform;
                model.SetParent(root, false);
                foreach (var (file, isVein) in spec.Meshes)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelFolder}/{file}.obj");
                    var mesh = (GameObject)PrefabUtility.InstantiatePrefab(asset, model);
                    foreach (var r in mesh.GetComponentsInChildren<Renderer>())
                        r.sharedMaterials = Enumerable.Repeat(isVein ? vein : artery, r.sharedMaterials.Length).ToArray();
                }
                model.localScale = Vector3.one * spec.DisplayScale;
                LayFlatOnTable(model);
                model.gameObject.SetActive(i == 0);   // Tube shown; toggle the others in the Hierarchy
            }
        }

        /// Turns the model so its thinnest extent is vertical and its longest runs left–right,
        /// then centers it over the table with its lowest point just above the top.
        /// (The visionOS app also turns the access site to the right using the centerlines;
        /// that comes with the VesselLayout port.)
        static void LayFlatOnTable(Transform model)
        {
            model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var size = WorldBounds(model).size;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            var order = Enumerable.Range(0, 3).OrderBy(i => size[i]).ToArray();
            var thinnest = axes[order[0]];
            var longest = axes[order[2]];

            // Rotation taking (longest, thinnest) onto (world right, world up).
            var fromModel = Quaternion.LookRotation(longest, thinnest);
            var toWorld = Quaternion.LookRotation(Vector3.right, Vector3.up);
            model.rotation = toWorld * Quaternion.Inverse(fromModel);

            var bounds = WorldBounds(model);
            var target = new Vector3(0, TableTop + Clearance, TableDistance);
            model.position += new Vector3(target.x - bounds.center.x, target.y - bounds.min.y, target.z - bounds.center.z);
        }

        static Bounds WorldBounds(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        // MARK: - Materials (URP Lit, saved as assets so they can be tuned in the Inspector)

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
            return Save(material);
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
            existing.CopyPropertiesFromMaterial(material);   // keep the asset GUID on rebuilds
            existing.shaderKeywords = material.shaderKeywords;
            existing.renderQueue = material.renderQueue;
            existing.SetOverrideTag("RenderType", material.GetTag("RenderType", false));
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
