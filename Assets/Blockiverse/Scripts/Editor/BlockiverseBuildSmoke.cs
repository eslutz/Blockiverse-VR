using System;
using System.Collections.Generic;
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
        const string AndroidPackageNameArgument = "-blockiverseAndroidPackageName";
        const string SigningConfigPathArgument = "-blockiverseSigningConfigPath";
        const string KeystorePathArgument = "-blockiverseKeystorePath";
        const string KeystorePasswordArgument = "-blockiverseKeystorePassword";
        const string KeyAliasArgument = "-blockiverseKeyAlias";
        const string KeyPasswordArgument = "-blockiverseKeyPassword";
        const string DefaultBuildOutputPath = "Builds/Android/BlockiverseVR-development.apk";
        const string DefaultReleaseBuildOutputPath = "Builds/Android/BlockiverseVR-release.apk";

        public static void BuildDevelopmentAndroid()
        {
            string outputPath = GetArgumentValue(BuildOutputArgument) ?? DefaultBuildOutputPath;
            string outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            BlockiverseProjectBootstrapper.Run();
            ConfigureAndroidPackageName();
            ConfigureAndroidVersion();

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
            ConfigureAndroidPackageName();
            ConfigureReleaseSigning();
            ConfigureAndroidVersion();

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
            Dictionary<string, string> signingConfig = ReadSigningConfig();
            string keystorePath = RequireValue(signingConfig, KeystorePathArgument, "ANDROID_KEYSTORE_PATH");
            string keystorePassword = RequireValue(
                signingConfig,
                KeystorePasswordArgument,
                "ANDROID_KEYSTORE_PASS",
                "ANDROID_KEYSTORE_PASSWORD");
            string keyAlias = RequireValue(
                signingConfig,
                KeyAliasArgument,
                "ANDROID_KEYALIAS_NAME",
                "ANDROID_KEY_ALIAS");
            string keyPassword = RequireValue(
                signingConfig,
                KeyPasswordArgument,
                "ANDROID_KEYALIAS_PASS",
                "ANDROID_KEY_PASSWORD");

            if (!File.Exists(keystorePath))
                throw new FileNotFoundException($"Android keystore was not found: {keystorePath}", keystorePath);

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePassword;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = keyPassword;
        }

        static void ConfigureAndroidPackageName()
        {
            string packageName = GetArgumentValue(AndroidPackageNameArgument)
                ?? Environment.GetEnvironmentVariable("ANDROID_PACKAGE_NAME");

            if (string.IsNullOrWhiteSpace(packageName))
                return;

            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, packageName.Trim());
        }

        static void ConfigureAndroidVersion()
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

        static string RequireValue(
            Dictionary<string, string> configValues,
            string argumentName,
            params string[] environmentVariableNames)
        {
            string value = GetArgumentValue(argumentName);

            if (!string.IsNullOrWhiteSpace(value))
                return value;

            foreach (string variableName in environmentVariableNames)
            {
                if (configValues.TryGetValue(variableName, out value) && !string.IsNullOrWhiteSpace(value))
                    return value;

                value = Environment.GetEnvironmentVariable(variableName);

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            throw new InvalidOperationException(
                $"{environmentVariableNames[0]} must be set for Android release signing.");
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
