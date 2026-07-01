using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Blockiverse.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Blockiverse.Editor
{
    public static class BlockiverseBuildSmoke
    {
        const string BuildOutputArgument = "-blockiverseBuildOutput";
        const string BuildVersionNameArgument = "-blockiverseBuildVersionName";
        const string BuildVersionCodeArgument = "-blockiverseBuildVersionCode";
        const string SigningConfigPathArgument = "-blockiverseSigningConfigPath";
        const string DefaultBuildOutputPath = "Builds/Android/BlockiverseVR-development.apk";
        const string DefaultReleaseBuildOutputPath = "Builds/Android/BlockiverseVR-release.apk";
        const string BaseVersionFilePath = "ProjectSettings/BlockiverseVersion.txt";
        const string MetaAvatarSamplePresetDirectory = "Assets/Oculus/Avatar2_SampleAssets/SampleAssets/SampleAssets";
        const string MetaAvatarSamplePresetMarkerFile = ".blockiverse-no-sample-presets";
        static readonly DateTime AndroidVersionCodeEpochUtc =
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [MenuItem("Blockiverse/Build/Build and Install Android APK")]
        public static void BuildAndInstallAndroid()
        {
            string outputPath = GetArgumentValue(BuildOutputArgument) ?? DefaultBuildOutputPath;
            string outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            BlockiverseProjectBootstrapper.Run();
            ConfigureAndroidVersion(allowLocalDevelopmentDefaults: true, requireExplicitVersion: false);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { BlockiverseProject.BootScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.CompressWithLz4 | BuildOptions.AutoRunPlayer
            };

            using (PrepareOptionalMetaAvatarSamplePresets())
            {
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Android development build and install failed with {summary.result}. Errors: {summary.totalErrors}");
                }
            }
        }

        [MenuItem("Blockiverse/Build/Development Android APK")]
        public static void BuildDevelopmentAndroid()
        {
            string outputPath = GetArgumentValue(BuildOutputArgument) ?? DefaultBuildOutputPath;
            string outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            BlockiverseProjectBootstrapper.Run();
            ConfigureAndroidVersion(allowLocalDevelopmentDefaults: true, requireExplicitVersion: false);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { BlockiverseProject.BootScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.CompressWithLz4
            };

            using (PrepareOptionalMetaAvatarSamplePresets())
            {
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Android development build failed with {summary.result}. Errors: {summary.totalErrors}");
                }
            }
        }

        public static void BuildReleaseAndroid()
        {
            string outputPath = GetArgumentValue(BuildOutputArgument) ?? DefaultReleaseBuildOutputPath;
            string outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            BlockiverseProjectBootstrapper.Run();
            ConfigureReleaseSigning();
            ConfigureAndroidVersion(allowLocalDevelopmentDefaults: false, requireExplicitVersion: true);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { BlockiverseProject.BootScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            using (PrepareOptionalMetaAvatarSamplePresets())
            {
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Android release build failed with {summary.result}. Errors: {summary.totalErrors}");
                }
            }
        }

        static IDisposable PrepareOptionalMetaAvatarSamplePresets()
        {
            if (DirectoryHasFiles(MetaAvatarSamplePresetDirectory))
                return DisposableAction.None;

            if (ProjectEnablesMetaAvatarFallbackPresets())
            {
                throw new InvalidOperationException(
                    "Meta Avatar fallback presets are enabled, but packaged sample preset assets are missing. " +
                    "Either disable loadFallbackPreset on Blockiverse avatar prefabs or intentionally add the packaged Quest preset assets.");
            }

            Directory.CreateDirectory(MetaAvatarSamplePresetDirectory);
            string markerPath = Path.Combine(MetaAvatarSamplePresetDirectory, MetaAvatarSamplePresetMarkerFile);
            File.WriteAllText(markerPath,
                "Blockiverse does not ship Meta Avatars sample preset zips. " +
                "This marker only prevents the Meta SDK sample-assets build hook from requiring unused local sample assets.\n");

            return new DisposableAction(() => CleanupOptionalMetaAvatarSamplePresetMarker(markerPath));
        }

        static bool ProjectEnablesMetaAvatarFallbackPresets()
        {
            foreach (string assetPath in EnumerateUnityTextAssets("Assets/Blockiverse"))
            {
                foreach (string line in File.ReadLines(assetPath))
                {
                    if (line.Trim() == "loadFallbackPreset: 1")
                        return true;
                }
            }

            return false;
        }

        static IEnumerable<string> EnumerateUnityTextAssets(string directory)
        {
            if (!Directory.Exists(directory))
                yield break;

            foreach (string path in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(path);
                if (extension == ".asset" || extension == ".prefab" || extension == ".unity")
                    yield return path;
            }
        }

        static bool DirectoryHasFiles(string directory)
        {
            if (!Directory.Exists(directory))
                return false;

            using (IEnumerator<string> enumerator = Directory.EnumerateFiles(directory).GetEnumerator())
                return enumerator.MoveNext();
        }

        static void CleanupOptionalMetaAvatarSamplePresetMarker(string markerPath)
        {
            DeleteFileIfExists(markerPath);
            DeleteFileIfExists(markerPath + ".meta");
            CleanupEmptyDirectoryTree(MetaAvatarSamplePresetDirectory, "Assets");
        }

        static void CleanupEmptyDirectoryTree(string directory, string stopDirectory)
        {
            string currentDirectory = Path.GetFullPath(directory);
            string fullStopDirectory = Path.GetFullPath(stopDirectory);

            while (!string.Equals(currentDirectory, fullStopDirectory, StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(currentDirectory) || !DirectoryIsEmpty(currentDirectory))
                    return;

                Directory.Delete(currentDirectory);
                DeleteFileIfExists(currentDirectory + ".meta");

                string parentDirectory = Path.GetDirectoryName(currentDirectory);
                if (string.IsNullOrEmpty(parentDirectory))
                    return;

                currentDirectory = parentDirectory;
            }
        }

        static bool DirectoryIsEmpty(string directory)
        {
            using (IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator())
                return !enumerator.MoveNext();
        }

        static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        static void ConfigureReleaseSigning()
        {
            Dictionary<string, string> signingConfig = ReadSigningConfig();
            string keystorePath = RequireValue(signingConfig, "ANDROID_KEYSTORE_PATH");
            string keystorePassword = RequireValue(signingConfig, "ANDROID_KEYSTORE_PASSWORD");
            string keyAlias = RequireValue(signingConfig, "ANDROID_KEY_ALIAS");
            string keyPassword = RequireValue(signingConfig, "ANDROID_KEY_PASSWORD");

            if (!File.Exists(keystorePath))
                throw new FileNotFoundException($"Android keystore was not found: {keystorePath}", keystorePath);

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePassword;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = keyPassword;
        }

        public static void ConfigureLocalDevelopmentAndroidVersion()
        {
            DateTime utcNow = DateTime.UtcNow;
            ApplyAndroidVersion(
                CreateLocalDevelopmentVersionName(utcNow),
                CreateAndroidVersionCode(utcNow).ToString(CultureInfo.InvariantCulture));
        }

        public static string CreateLocalDevelopmentVersionName(DateTime utcNow)
        {
            string buildStamp = utcNow
                .ToUniversalTime()
                .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

            return $"{ReadBaseVersion()}-dev.local.{buildStamp}";
        }

        public static int CreateAndroidVersionCode(DateTime utcNow)
        {
            double totalSeconds = (utcNow.ToUniversalTime() - AndroidVersionCodeEpochUtc).TotalSeconds;
            if (totalSeconds < 1 || totalSeconds > int.MaxValue)
                throw new InvalidOperationException($"Android versionCode timestamp is out of range: {utcNow:o}");

            return (int)Math.Floor(totalSeconds);
        }

        static void ConfigureAndroidVersion(bool allowLocalDevelopmentDefaults, bool requireExplicitVersion)
        {
            string versionName = GetArgumentValue(BuildVersionNameArgument)
                ?? Environment.GetEnvironmentVariable("UNITY_ANDROID_VERSION_NAME");
            string versionCode = GetArgumentValue(BuildVersionCodeArgument)
                ?? Environment.GetEnvironmentVariable("UNITY_ANDROID_VERSION_CODE");

            if (allowLocalDevelopmentDefaults && (string.IsNullOrWhiteSpace(versionName) || string.IsNullOrWhiteSpace(versionCode)))
            {
                DateTime utcNow = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(versionName))
                    versionName = CreateLocalDevelopmentVersionName(utcNow);
                if (string.IsNullOrWhiteSpace(versionCode))
                    versionCode = CreateAndroidVersionCode(utcNow).ToString(CultureInfo.InvariantCulture);
            }

            if (requireExplicitVersion && (string.IsNullOrWhiteSpace(versionName) || string.IsNullOrWhiteSpace(versionCode)))
            {
                throw new InvalidOperationException(
                    "Android release builds must pass -blockiverseBuildVersionName and -blockiverseBuildVersionCode " +
                    "or set UNITY_ANDROID_VERSION_NAME and UNITY_ANDROID_VERSION_CODE.");
            }

            ApplyAndroidVersion(versionName, versionCode);
        }

        static void ApplyAndroidVersion(string versionName, string versionCode)
        {
            if (!string.IsNullOrWhiteSpace(versionName))
                PlayerSettings.bundleVersion = versionName.Trim();

            if (string.IsNullOrWhiteSpace(versionCode))
                return;

            if (!int.TryParse(versionCode.Trim(), out int parsedVersionCode) || parsedVersionCode < 1)
                throw new InvalidOperationException($"Android versionCode must be a positive integer: {versionCode}");

            PlayerSettings.Android.bundleVersionCode = parsedVersionCode;
        }

        static string ReadBaseVersion()
        {
            if (!File.Exists(BaseVersionFilePath))
                throw new FileNotFoundException($"Blockiverse base version file was not found: {BaseVersionFilePath}", BaseVersionFilePath);

            string baseVersion = File.ReadAllText(BaseVersionFilePath).Trim();
            if (!IsBaseSemVer(baseVersion))
                throw new InvalidOperationException($"{BaseVersionFilePath} must contain MAJOR.MINOR.PATCH without a leading v.");

            return baseVersion;
        }

        static bool IsBaseSemVer(string value)
        {
            string[] parts = value.Split('.');
            if (parts.Length != 3)
                return false;

            foreach (string part in parts)
            {
                if (part.Length == 0 || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return false;
            }

            return true;
        }

        static Dictionary<string, string> ReadSigningConfig()
        {
            string configPath = GetArgumentValue(SigningConfigPathArgument);

            if (string.IsNullOrWhiteSpace(configPath))
                return new Dictionary<string, string>(StringComparer.Ordinal);

            if (!File.Exists(configPath))
                throw new FileNotFoundException($"Android signing config was not found: {configPath}", configPath);

            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string line in File.ReadAllLines(configPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex < 1)
                    throw new InvalidOperationException($"Invalid Android signing config line: {line}");

                values[line.Substring(0, separatorIndex)] = line.Substring(separatorIndex + 1);
            }

            return values;
        }

        static string RequireValue(Dictionary<string, string> configValues, string environmentVariableName)
        {
            if (configValues.TryGetValue(environmentVariableName, out string value) && !string.IsNullOrWhiteSpace(value))
                return value;

            value = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            throw new InvalidOperationException(
                $"{environmentVariableName} must be set for Android release signing.");
        }

        static string GetArgumentValue(string argumentName)
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == argumentName)
                    return args[i + 1];
            }

            return null;
        }

        sealed class DisposableAction : IDisposable
        {
            public static readonly IDisposable None = new DisposableAction(null);

            readonly Action action;
            bool disposed;

            public DisposableAction(Action action)
            {
                this.action = action;
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                action?.Invoke();
            }
        }
    }
}
