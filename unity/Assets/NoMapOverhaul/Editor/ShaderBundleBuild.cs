using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NoMapOverhaul.Editor
{
    /// <summary>
    /// Builds the mod's shader bundle in batch mode, from build/Build-Shaders.ps1:
    /// Unity -batchmode -quit -projectPath unity -executeMethod NoMapOverhaul.Editor.ShaderBundleBuild.Build
    /// </summary>
    public static class ShaderBundleBuild
    {
        private const string ShaderPath = "Assets/NoMapOverhaul/WorldRing.shader";
        private const string BundleName = "nomapoverhaul-shaders.bundle";
        private const string Staging = "Temp/ShaderBundle";
        private const string Output = "Bundles";

        public static void Build()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                throw new InvalidOperationException(ShaderPath + " didn't import.");
            if (ShaderUtil.ShaderHasError(shader))
            {
                var messages = ShaderUtil.GetShaderMessages(shader).Select(message => message.message + " (line " + message.line + ")");
                throw new InvalidOperationException(ShaderPath + " doesn't compile: " + string.Join("; ", messages));
            }

            // The game runs on Direct3D 11 unless started with -force-d3d12 or -force-vulkan.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Vulkan });

            Directory.CreateDirectory(Staging);
            var bundle = new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { ShaderPath } };
            var manifest = BuildPipeline.BuildAssetBundles(Staging, new[] { bundle },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
                throw new InvalidOperationException("The shader bundle build failed.");

            Directory.CreateDirectory(Output);
            var built = Path.GetFullPath(Path.Combine(Output, BundleName));
            File.Copy(Path.Combine(Staging, BundleName), built, true);
            Debug.Log("[NoMapOverhaul] Shader bundle built: " + built);
        }
    }
}
