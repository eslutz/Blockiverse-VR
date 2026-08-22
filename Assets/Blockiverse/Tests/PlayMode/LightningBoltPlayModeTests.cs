using System.Collections;
using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    // The bolt only exists at runtime -- it is built from code, not from a prefab -- so these are
    // the only tests that can see it draw. The billboard case is the important one: a bolt that
    // tilts with the head reads instantly as a flat card, which is the exact failure that ruled
    // out a sprite in the first place.
    public sealed class LightningBoltPlayModeTests
    {
        GameObject boltObject;
        GameObject cameraObject;

        [TearDown]
        public void TearDown()
        {
            if (boltObject != null)
                Object.Destroy(boltObject);
            if (cameraObject != null)
                Object.Destroy(cameraObject);
        }

        LightningBoltView CreateBolt()
        {
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(0.0f, 1.7f, -20.0f);

            boltObject = new GameObject("Lightning Bolt Under Test");
            LightningBoltView bolt = boltObject.AddComponent<LightningBoltView>();

            // Explicit rather than via Camera.main: a Boot scene left loaded by an earlier test
            // also carries a MainCamera, and the bolt would billboard toward whichever one the
            // engine happened to return first.
            bolt.Configure(cameraObject.transform);
            return bolt;
        }

        [UnityTest]
        public IEnumerator BoltShowsOnStrikeAndHidesAfterItsLifetime()
        {
            LightningBoltView bolt = CreateBolt();
            yield return null;

            Assert.That(bolt.IsStriking, Is.False, "A bolt must be invisible until something strikes.");

            bolt.Strike(new Vector3(0.0f, 4.0f, 0.0f), seed: 12345, distance: 20.0f, reducedParticles: false);

            Assert.That(bolt.IsStriking, Is.True);
            Assert.That(bolt.Renderer, Is.Not.Null);
            Assert.That(bolt.Renderer.enabled, Is.True);

            float deadline = Time.time + LightningBoltView.LifetimeSeconds + 0.3f;
            while (Time.time < deadline)
                yield return null;

            Assert.That(bolt.IsStriking, Is.False);
            Assert.That(bolt.Renderer.enabled, Is.False, "A bolt left enabled would hang in the sky forever.");
        }

        [UnityTest]
        public IEnumerator BoltBillboardsOnYawOnly()
        {
            LightningBoltView bolt = CreateBolt();
            yield return null;

            // Four cardinal head offsets plus one from directly above, which is where a full
            // LookRotation would visibly tip the bolt over.
            Vector3[] headOffsets =
            {
                new(0.0f, 1.7f, -20.0f),
                new(20.0f, 1.7f, 0.0f),
                new(0.0f, 1.7f, 20.0f),
                new(-20.0f, 1.7f, 0.0f),
                new(6.0f, 60.0f, 6.0f)
            };

            foreach (Vector3 offset in headOffsets)
            {
                cameraObject.transform.position = offset;
                bolt.Strike(Vector3.zero, seed: 7, distance: offset.magnitude, reducedParticles: false);
                yield return null;

                Assert.That(Mathf.Abs(bolt.transform.forward.y), Is.LessThan(1e-3f),
                    $"The bolt tilted when the head was at {offset}.");

                // It still has to turn toward the head, or the ribbon is edge-on and invisible.
                Vector3 flatToHead = new Vector3(offset.x, 0.0f, offset.z).normalized;
                if (flatToHead.sqrMagnitude > 0.5f)
                {
                    Assert.That(Vector3.Dot(bolt.transform.forward, flatToHead), Is.GreaterThan(0.99f),
                        $"The bolt did not face the head at {offset}.");
                }
            }
        }

        [UnityTest]
        public IEnumerator RestrikingReusesTheSameMeshAndMaterial()
        {
            // One instance, restarted -- not a pool and not a fresh allocation per strike, so a
            // long storm produces no steady-state garbage.
            LightningBoltView bolt = CreateBolt();
            yield return null;

            bolt.Strike(Vector3.zero, seed: 1, distance: 30.0f, reducedParticles: false);
            yield return null;

            Mesh firstMesh = boltObject.GetComponent<MeshFilter>().sharedMesh;
            Material firstMaterial = bolt.Renderer.sharedMaterial;

            Assert.That(firstMesh, Is.Not.Null);
            Assert.That(firstMesh.vertexCount, Is.GreaterThan(0));

            bolt.Strike(new Vector3(5.0f, 0.0f, 5.0f), seed: 2, distance: 60.0f, reducedParticles: false);
            yield return null;

            Assert.That(boltObject.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(firstMesh));
            Assert.That(bolt.Renderer.sharedMaterial, Is.SameAs(firstMaterial));
            Assert.That(bolt.transform.position, Is.EqualTo(new Vector3(5.0f, 0.0f, 5.0f)));
        }

    }
}
