using System.Threading.Tasks;
using Meta.XR;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace ProjectBootstrap
{
    // One-time Quest 3 project configuration, headless-safe. Run with the Android target:
    //   Unity -batchmode -buildTarget Android -projectPath <repo>
    //         -executeMethod ProjectBootstrap.QuestSetup.Configure   (no -quit)
    // Also available from the menu: Tools > Blood Vessels > Configure for Quest 3.
    public static class QuestSetup
    {
        const BuildTargetGroup Android = BuildTargetGroup.Android;
        const string XRSettingsFolder = "Assets/XR";

        static Task _fixTask;
        static double _deadline;

        [MenuItem("Tools/Blood Vessels/Configure for Quest 3")]
        public static void ConfigureFromMenu() => ApplySettings();

        public static void Configure()
        {
            ApplySettings();
            // Meta's Project Setup Tool fixes the rest of its recommended settings.
            _fixTask = OVRProjectSetup.FixAllAsync(Android);
            _deadline = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update += PollFixes;
        }

        static void PollFixes()
        {
            if (!_fixTask.IsCompleted && EditorApplication.timeSinceStartup < _deadline) return;
            EditorApplication.update -= PollFixes;
            AssetDatabase.SaveAssets();
            Debug.Log(_fixTask.IsCompleted
                ? $"[QuestSetup] Meta project setup fixes done ({_fixTask.Status})."
                : "[QuestSetup] Meta project setup fixes timed out; run them from Meta > Project Setup Tool.");
            EditorApplication.Exit(_fixTask.IsCompleted ? 0 : 2);
        }

        /// Settings only (no Meta setup-tool pass); safe to call repeatedly.
        public static void ApplySettings()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(Android, BuildTarget.Android);

            // Player settings Quest 3 requires: 64-bit IL2CPP, Vulkan, linear color, Android 12L+.
            PlayerSettings.companyName = "willyci";
            PlayerSettings.productName = "Blood Vessels";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.willyci.bloodvessels");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

            EnableOpenXRLoader();
            EnableMetaFeatures();

            // Hand tracking (alongside controllers) and passthrough for mixed reality.
            var config = OVRProjectConfig.CachedProjectConfig;
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            OVRProjectConfig.CommitProjectConfig(config);

            AssetDatabase.SaveAssets();
            Debug.Log("[QuestSetup] Android, OpenXR, Meta features, hands and passthrough configured.");
        }

        static void EnableOpenXRLoader()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                if (!AssetDatabase.IsValidFolder(XRSettingsFolder)) AssetDatabase.CreateFolder("Assets", "XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, $"{XRSettingsFolder}/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(Android)) perTarget.CreateDefaultSettingsForBuildTarget(Android);
            if (!perTarget.HasManagerSettingsForBuildTarget(Android)) perTarget.CreateDefaultManagerSettingsForBuildTarget(Android);

            var settings = perTarget.SettingsForBuildTarget(Android);
            settings.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(settings.AssignedSettings, typeof(OpenXRLoader).FullName, Android);
            EditorUtility.SetDirty(perTarget);
        }

        static void EnableMetaFeatures()
        {
            FeatureHelpers.RefreshFeatures(Android);
            var openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(Android);
            if (openXR == null)
            {
                Debug.LogWarning("[QuestSetup] OpenXR settings not created yet; run Configure again.");
                return;
            }
            foreach (var feature in new OpenXRFeature[]
                     {
                         openXR.GetFeature<MetaQuestFeature>(),   // Quest runtime + manifest
                         openXR.GetFeature<MetaXRFeature>(),      // Meta XR Core SDK on OpenXR
                         openXR.GetFeature<MetaQuestTouchPlusControllerProfile>(),   // Quest 3 controllers
                     })
            {
                if (feature == null) continue;
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }
            EditorUtility.SetDirty(openXR);
        }
    }
}
