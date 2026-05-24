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
        const string DefaultBuildOutputPath = "Builds/Android/BlockiverseVR-development.apk";

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
