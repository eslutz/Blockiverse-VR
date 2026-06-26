using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Android;

namespace Blockiverse.Editor
{
    public sealed class BlockiverseAndroidGradlePostprocessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            BlockiverseAndroidGradleFixups.Apply(path);
        }
    }

    public static class BlockiverseAndroidGradleFixups
    {
        const string OculusIntegrationPackage = "com.oculus.Integration";

        public static void Apply(string unityLibraryPath)
        {
            if (string.IsNullOrWhiteSpace(unityLibraryPath))
                throw new ArgumentException("Unity library Gradle path must be provided.", nameof(unityLibraryPath));

            RewriteAarManifestPackage(
                Path.Combine(unityLibraryPath, "libs", "InteractionSdk.aar"),
                "com.oculus.integration.interactionsdk");
            RewriteAarManifestPackage(
                Path.Combine(unityLibraryPath, "libs", "OVRPlugin.aar"),
                "com.oculus.integration.ovrplugin");
            ModernizeGeneratedGradleFiles(unityLibraryPath);
        }

        static void RewriteAarManifestPackage(string aarPath, string replacementPackage)
        {
            if (!File.Exists(aarPath))
                return;

            string temporaryPath = aarPath + ".tmp";
            bool changed = false;

            using (var sourceStream = File.OpenRead(aarPath))
            using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read))
            using (var destinationStream = File.Create(temporaryPath))
            using (var destinationArchive = new ZipArchive(destinationStream, ZipArchiveMode.Create))
            {
                foreach (ZipArchiveEntry sourceEntry in sourceArchive.Entries)
                {
                    ZipArchiveEntry destinationEntry = destinationArchive.CreateEntry(
                        sourceEntry.FullName,
                        sourceEntry.FullName == "AndroidManifest.xml"
                            ? CompressionLevel.Optimal
                            : CompressionLevel.NoCompression);
                    destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;

                    using Stream input = sourceEntry.Open();
                    using Stream output = destinationEntry.Open();

                    if (sourceEntry.FullName == "AndroidManifest.xml")
                    {
                        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                        string manifest = reader.ReadToEnd();
                        string updatedManifest = manifest.Replace(
                            $"package=\"{OculusIntegrationPackage}\"",
                            $"package=\"{replacementPackage}\"",
                            StringComparison.Ordinal);

                        changed |= !string.Equals(manifest, updatedManifest, StringComparison.Ordinal);

                        byte[] manifestBytes = Encoding.UTF8.GetBytes(updatedManifest);
                        output.Write(manifestBytes, 0, manifestBytes.Length);
                    }
                    else
                    {
                        input.CopyTo(output);
                    }
                }
            }

            if (changed)
            {
                File.Delete(aarPath);
                File.Move(temporaryPath, aarPath);
            }
            else
            {
                File.Delete(temporaryPath);
            }
        }

        static void ModernizeGeneratedGradleFiles(string unityLibraryPath)
        {
            string gradleRoot = Directory.GetParent(unityLibraryPath)?.FullName;
            if (string.IsNullOrWhiteSpace(gradleRoot) || !Directory.Exists(gradleRoot))
                return;

            foreach (string gradlePath in Directory.GetFiles(gradleRoot, "*.gradle", SearchOption.AllDirectories))
                ModernizeGeneratedGradle(gradlePath);
        }

        static void ModernizeGeneratedGradle(string gradlePath)
        {
            if (!File.Exists(gradlePath))
                return;

            string gradle = File.ReadAllText(gradlePath);
            string updatedGradle = gradle
                .Replace("implementation(name: 'InteractionSdk', ext:'aar')", "implementation ':InteractionSdk@aar'", StringComparison.Ordinal)
                .Replace("implementation(name: 'OVRPlugin', ext:'aar')", "implementation ':OVRPlugin@aar'", StringComparison.Ordinal);
            updatedGradle = Regex.Replace(
                updatedGradle,
                @"^(\s*)(namespace|ndkPath|ndkVersion|debugSymbolLevel|version)\s+(['""].+)$",
                "$1$2 = $3",
                RegexOptions.Multiline);
            updatedGradle = Regex.Replace(
                updatedGradle,
                @"^(\s*)(abortOnError|useLegacyPackaging|prefab)\s+(true|false)\s*$",
                "$1$2 = $3",
                RegexOptions.Multiline);
            updatedGradle = Regex.Replace(
                updatedGradle,
                @"^(\s*)signingConfig\s+(signingConfigs\.\w+)\s*$",
                "$1signingConfig = $2",
                RegexOptions.Multiline);

            if (!string.Equals(gradle, updatedGradle, StringComparison.Ordinal))
                File.WriteAllText(gradlePath, updatedGradle);
        }
    }
}
