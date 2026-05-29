using System;
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
        const string DefaultBuildOutputPath = "Builds/Android/BlockiverseVR-development.apk";
        const string DefaultReleaseBuildOutputPath = "Builds/Android/BlockiverseVR-release.apk";

        public static void BuildDevelopmentAndroid()
        {
            string outputPath = GetArgumentValue(BuildOutputArgument) ?? DefaultBuildOutputPath;
            string outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            BlockiverseProjectBootstrapper.Run();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { BlockiverseProject.BootScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.CompressWithLz4
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Android development build failed with {summary.result}. Errors: {summary.totalErrors}");
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
            ConfigureReleaseVersion();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { BlockiverseProject.BootScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Android release build failed with {summary.result}. Errors: {summary.totalErrors}");
            }
        }

        static void ConfigureReleaseSigning()
        {
            string keystorePath = RequireEnvironmentVariable("ANDROID_KEYSTORE_PATH");
            string keystorePassword = RequireEnvironmentVariable("ANDROID_KEYSTORE_PASSWORD");
            string keyAlias = RequireEnvironmentVariable("ANDROID_KEY_ALIAS");
            string keyPassword = RequireEnvironmentVariable("ANDROID_KEY_PASSWORD");

            if (!File.Exists(keystorePath))
                throw new FileNotFoundException($"Android keystore was not found: {keystorePath}", keystorePath);

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePassword;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = keyPassword;
        }

        static void ConfigureReleaseVersion()
        {
            string versionName = GetArgumentValue(BuildVersionNameArgument)
                ?? Environment.GetEnvironmentVariable("UNITY_ANDROID_VERSION_NAME");
            string versionCode = GetArgumentValue(BuildVersionCodeArgument)
                ?? Environment.GetEnvironmentVariable("UNITY_ANDROID_VERSION_CODE");

            if (!string.IsNullOrWhiteSpace(versionName))
                PlayerSettings.bundleVersion = versionName.Trim();

            if (!string.IsNullOrWhiteSpace(versionCode))
            {
                if (!int.TryParse(versionCode.Trim(), out int parsedVersionCode) || parsedVersionCode < 1)
                    throw new InvalidOperationException($"UNITY_ANDROID_VERSION_CODE must be a positive integer: {versionCode}");

                PlayerSettings.Android.bundleVersionCode = parsedVersionCode;
            }
        }

        static string RequireEnvironmentVariable(string variableName)
        {
            string value = Environment.GetEnvironmentVariable(variableName);

            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{variableName} must be set for Android release signing.");

            return value;
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
    }
}
