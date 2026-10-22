using System;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// The WorldRing shader, which draws the 3D view's rings on the GPU, from the asset bundle beside the plugin. It's
/// built from <c>unity/</c> by <c>build/Build-Shaders.ps1</c>. Without it the rings are built on the CPU, as
/// <see cref="StrokeGraphic"/> meshes, and the log says which way they're drawn.
/// </summary>
internal static class RingShader
{
    public const string BundleFile = "nomapoverhaul-shaders.bundle";

    private static ManualLogSource? _log;

    /// <summary>The shader, or null while the rings are drawn on the CPU.</summary>
    public static Shader? Shader { get; private set; }

    public static void Load(ManualLogSource log)
    {
        _log = log;
        var path = Path.Combine(Path.GetDirectoryName(typeof(RingShader).Assembly.Location) ?? "", BundleFile);
        string problem;
        try
        {
            Shader = TryLoad(path, out problem);
        }
        catch (Exception exception)
        {
            problem = $"{BundleFile} didn't load. {exception.Message}";
        }

        if (Shader != null)
        {
            log.LogInfo($"3D rings are drawn on the GPU, by the shader in {BundleFile}.");
        }
        else
        {
            log.LogWarning($"3D rings are drawn on the CPU: {problem}");
        }
    }

    /// <summary>Sends the rings back to the CPU for the rest of the session, when the HUD is one the shader can't draw on.</summary>
    public static void FallBack(string reason)
    {
        if (Shader == null)
        {
            return;
        }

        Shader = null;
        _log?.LogWarning($"3D rings are drawn on the CPU from now on: {reason}");
    }

    private static Shader? TryLoad(string path, out string problem)
    {
        if (!File.Exists(path))
        {
            problem = $"{BundleFile} isn't beside the plugin.";
            return null;
        }

        var bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null)
        {
            problem = $"{BundleFile} didn't load.";
            return null;
        }

        try
        {
            var shader = bundle.LoadAsset<Shader>("WorldRing");
            if (shader == null)
            {
                problem = $"{BundleFile} has no WorldRing shader.";
                return null;
            }

            if (!shader.isSupported)
            {
                problem = $"this graphics API ({SystemInfo.graphicsDeviceType}) can't run the WorldRing shader.";
                return null;
            }

            // Kept when the game unloads unused assets between missions, since the bundle is gone by then.
            shader.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            problem = "";
            return shader;
        }
        finally
        {
            bundle.Unload(false);
        }
    }
}
