using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARMurals.EditorTools
{
    /// <summary>
    /// One click to put the project into the state the conventions contract requires.
    /// Safe to run more than once.
    /// </summary>
    public static class MuralProjectSetup
    {
        [MenuItem("AR Murals/1. Configure Project", priority = 0)]
        public static void Configure()
        {
            // --- Version control friendliness (this is what makes four branches mergeable)
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";

            // --- Identity
            PlayerSettings.companyName = "ALU";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.alu.armurals");

            // --- Android / ARCore requirements
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel26)
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            // OpenGLES3 only. Vulkan plus ARCore still produces random black-camera builds.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

            AssetDatabase.SaveAssets();

            Debug.Log("[AR Murals] Project configured: Force Text serialisation, visible meta files, " +
                      "com.alu.armurals, ARM64 + IL2CPP, OpenGLES3 only. " +
                      "Now switch platform to Android in Build Profiles if you have not already.");
        }
    }
}
