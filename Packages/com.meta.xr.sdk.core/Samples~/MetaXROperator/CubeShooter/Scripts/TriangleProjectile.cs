/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using UnityEngine;

public class TriangleProjectile : MonoBehaviour
{
    public float speed = 80f;
    public float lifetime = 5f;

    private Rigidbody rb;
    private bool hitTarget;
    private Transform rigTransform;
    private float maxRange;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.linearVelocity = transform.forward * speed;
        }

        var spawner = FindAnyObjectByType<TargetSpawner>();
        if (spawner != null)
            maxRange = spawner.maxDistFromRig + 5f;
        else
            maxRange = 25f;

        var rig = FindAnyObjectByType<OVRCameraRig>();
        if (rig != null)
            rigTransform = rig.transform;

        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (hitTarget) return;

        Vector3 rigPos = rigTransform != null ? rigTransform.position : Vector3.zero;
        float dist = Vector3.Distance(transform.position, rigPos);
        if (dist > maxRange)
            Destroy(gameObject);
    }

    void OnCollisionEnter(Collision collision)
    {
        HandleHit(collision.gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        HandleHit(other.gameObject);
    }

    void HandleHit(GameObject hitObject)
    {
        ShootableTarget target = hitObject.GetComponent<ShootableTarget>();
        if (target != null)
        {
            hitTarget = true;
            Debug.Log($"Target destroyed: {hitObject.name}");
            ScoreManager.RegisterHit(target.pointValue);
            var spawner = FindAnyObjectByType<TargetSpawner>();
            if (spawner != null)
                spawner.OnTargetDestroyed(hitObject);
            Destroy(hitObject);
        }
        Destroy(gameObject);
    }

}
