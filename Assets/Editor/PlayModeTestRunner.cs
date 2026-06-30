using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

[InitializeOnLoad]
internal static class PlayModeTestRunner
{
    private static TestRunnerApi api;

    static PlayModeTestRunner()
    {
        if (SessionState.GetString("RunPlayModeTests", "") != "true")
            return;

        EditorApplication.delayCall += Run;
    }

    private static void Run()
    {
        SessionState.SetString("RunPlayModeTests", "running");
        api = ScriptableObject.CreateInstance<TestRunnerApi>();
        var callback = ScriptableObject.CreateInstance<MyCallback>();
        api.RegisterCallbacks(callback);
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.PlayMode,
            groupNames = new[] { "Blockiverse.Tests.PlayMode.MetaAvatarPlayModeTests" }
        }));
    }

    private class MyCallback : ScriptableObject, ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) {}

        public void RunFinished(ITestResultAdaptor result)
        {
            Debug.Log($"[TestRunner] PlayMode test run finished: {result.PassCount} passed, {result.FailCount} failed.");
            SessionState.SetString("PlayModeTestResults", $"PASSED:{result.PassCount},FAILED:{result.FailCount}");
            SessionState.SetString("RunPlayModeTests", "done");
            
            EditorApplication.delayCall += () => {
                AssetDatabase.DeleteAsset("Assets/Editor/PlayModeTestRunner.cs");
                AssetDatabase.Refresh();
            };
        }

        public void TestStarted(ITestAdaptor test) {}
        public void TestFinished(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
            {
                Debug.Log($"[TestRunner] Test {result.Test.Name}: {result.TestStatus}");
                if (result.TestStatus == TestStatus.Failed)
                {
                    Debug.LogError($"[TestRunner] Failure Message: {result.Message}");
                }
            }
        }
    }
}
