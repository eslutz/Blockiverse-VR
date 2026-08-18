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

public class LaserGun : MonoBehaviour
{
    [Header("Projectile")]
    public GameObject projectilePrefab;
    public Transform muzzlePoint;
    public float fireRate = 0.15f;

    [Header("Grab")]
    // After grabbing, the muzzle won't perfectly align with the controller aim pose — calibrate the rotational offset before aiming.
    public float grabRadius = 0.15f;

    private float nextFireTime;
    private bool isGrabbed;
    private OVRInput.Controller grabbedByController;
    private Transform originalParent;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        originalParent = transform.parent;
    }

    void Update()
    {
        if (!isGrabbed)
        {
            TryGrab(OVRInput.Controller.RTouch);
            TryGrab(OVRInput.Controller.LTouch);
        }
        else
        {
            // Check for release (grip button up)
            float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, grabbedByController);
            if (grip < 0.3f)
            {
                Release();
                return;
            }

            // Check for fire (index trigger)
            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, grabbedByController);
            if (trigger > 0.7f && Time.time >= nextFireTime)
            {
                Fire();
                nextFireTime = Time.time + fireRate;
            }
        }
    }

    void TryGrab(OVRInput.Controller controller)
    {
        float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller);
        if (grip < 0.7f) return;

        Vector3 handPos = OVRInput.GetLocalControllerPosition(controller);
        // Convert from local tracking space to world space
        var cameraRig = FindAnyObjectByType<OVRCameraRig>();
        if (cameraRig != null)
        {
            handPos = cameraRig.trackingSpace.TransformPoint(handPos);
        }

        float dist = Vector3.Distance(handPos, transform.position);
        if (dist < grabRadius)
        {
            Grab(controller, cameraRig);
        }
    }

    void Grab(OVRInput.Controller controller, OVRCameraRig cameraRig)
    {
        isGrabbed = true;
        grabbedByController = controller;

        if (rb != null)
        {
            rb.isKinematic = true;
        }

        // Parent to the controller anchor
        Transform anchor = null;
        if (cameraRig != null)
        {
            anchor = controller == OVRInput.Controller.RTouch
                ? cameraRig.rightControllerAnchor
                : cameraRig.leftControllerAnchor;
        }

        if (anchor != null)
        {
            transform.SetParent(anchor);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // Align muzzle forward with controller forward
            if (muzzlePoint != null)
            {
                Vector3 muzzleForward = muzzlePoint.forward;
                Vector3 anchorForward = anchor.forward;
                Quaternion correction = Quaternion.FromToRotation(muzzleForward, anchorForward);
                transform.rotation = correction * transform.rotation;

                // Recenter grip on controller after rotation correction
                var col = GetComponent<BoxCollider>();
                if (col != null)
                {
                    Vector3 gripWorld = transform.TransformPoint(col.center);
                    transform.position -= (gripWorld - anchor.position);
                }
            }
        }
    }

    void Release()
    {
        isGrabbed = false;

        transform.SetParent(originalParent);

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = OVRInput.GetLocalControllerVelocity(grabbedByController);
            rb.angularVelocity = OVRInput.GetLocalControllerAngularVelocityCw(grabbedByController);
        }
    }

    void Fire()
    {
        if (projectilePrefab == null || muzzlePoint == null) return;

        GameObject proj = Instantiate(projectilePrefab, muzzlePoint.position, muzzlePoint.rotation);
        // Ignore collision between projectile and gun
        var projCollider = proj.GetComponent<Collider>();
        var gunCollider = GetComponent<Collider>();
        if (projCollider != null && gunCollider != null)
        {
            Physics.IgnoreCollision(projCollider, gunCollider);
        }
    }
}
