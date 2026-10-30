using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace ProjectBootstrap
{
    // Installs the Quest 3 XR packages via the PackageManager Client API, headless-safe.
    // Run: Unity -batchmode -projectPath <repo> -executeMethod ProjectBootstrap.PackageInstaller.Install (no -quit)
    public static class PackageInstaller
    {
        static readonly string[] PackagesToAdd =
        {
            "com.unity.xr.openxr",               // XR runtime (Meta's recommended provider)
            "com.meta.xr.sdk.core",              // Meta XR Core: camera rig, passthrough, hands
            "com.meta.xr.sdk.interaction.ovr",   // Interaction SDK: grab, poke, ray, hand poses
            "com.meta.xr.mrutilitykit",          // MR Utility Kit: room/scene understanding
        };

        static readonly string[] PackagesToRemove = { };

        const double TimeoutSeconds = 900;

        static AddAndRemoveRequest _request;
        static double _deadline;

        public static void Install()
        {
            Debug.Log($"[PackageInstaller] Adding: {string.Join(", ", PackagesToAdd)}");
            _request = Client.AddAndRemove(packagesToAdd: PackagesToAdd, packagesToRemove: PackagesToRemove);
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (_request == null) return;
            if (!_request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    EditorApplication.update -= Poll;
                    Debug.LogError("[PackageInstaller] Timed out waiting for UPM.");
                    EditorApplication.Exit(2);
                }
                return;
            }
            EditorApplication.update -= Poll;
            if (_request.Status == StatusCode.Success)
            {
                Debug.Log($"[PackageInstaller] Resolved: {string.Join(", ", _request.Result.Select(p => $"{p.name}@{p.version}"))}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[PackageInstaller] Failed: {_request.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
    }
}
