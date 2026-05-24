using System;
using System.Collections;
using Blockiverse.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BootScenePlayModeTests
    {
        const string BootSceneName = "Boot";

        [UnityTest]
        public IEnumerator BootSceneLoadsWithXrRigAndCamera()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(BootSceneName, LoadSceneMode.Single);

            Assert.That(operation, Is.Not.Null);

            while (!operation.isDone)
                yield return null;

            GameObject rig = GameObject.Find(BlockiverseProject.XrRigRootName);
            Assert.That(rig, Is.Not.Null);
            Assert.That(Camera.main, Is.Not.Null);

            Type markerType = Type.GetType("Blockiverse.VR.BlockiverseXRRigMarker, Blockiverse.VR");
            Assert.That(markerType, Is.Not.Null);
            Assert.That(rig.GetComponent(markerType), Is.Not.Null);
        }
    }
}
