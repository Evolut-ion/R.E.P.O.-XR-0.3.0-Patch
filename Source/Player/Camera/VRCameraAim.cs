using HarmonyLib;
using RepoXR.Input;
using RepoXR.Patches;
using UnityEngine;

namespace RepoXR.Player.Camera;

// KNOWN ISSUE: When the player is not near their play area center, the VR aim script will pivot around a far away point
//              instead of at the camera itself. This is a known issue, no clue how to fix it yet.

public class VRCameraAim : MonoBehaviour
{
    public static VRCameraAim instance;
    
    private CameraAim cameraAim;
    private Transform mainCamera;
    
    private Quaternion rotation;
    private float yawOffset;
    
    // Aim fields
    private bool aimTargetActive;
    private GameObject? aimTargetObject;
    private Vector3 aimTargetPosition;
    private float aimTargetTimer;
    private float aimTargetSpeed;
    private int aimTargetPriority = 999;
    private bool aimTargetLowImpact;

    private float aimTargetLerp;
    
    // Soft aim fields
    private GameObject? aimTargetSoftObject;
    private Vector3 aimTargetSoftPosition;
    private float aimTargetSoftTimer;
    private float aimTargetSoftStrength;
    private float aimTargetSoftStrengthNoAim;
    private int aimTargetSoftPriority = 999;
    private bool aimTargetSoftLowImpact;
    
    private float aimTargetSoftStrengthCurrent;

    private Quaternion lastCameraRotation;
    private float playerAimingTimer;
    // Track the last rotation we pushed to the game's CameraAim to avoid repeated tiny resets (can cause flicker)
    private Quaternion lastSetPlayerAim = Quaternion.identity;
    private float lastSetPlayerAimTime = 0f;

    // smoothing velocity for local position
    private Vector3 localPosVelocity;

    public bool IsActive => aimTargetActive;
    
    private void Awake()
    {
        instance = this;
        
        cameraAim = GetComponent<CameraAim>();
        mainCamera = GetComponentInChildren<UnityEngine.Camera>().transform;
    }

    private void Update()
    {
        // Detect head movement
        
        if (lastCameraRotation == Quaternion.identity)
            lastCameraRotation = mainCamera.localRotation;

        var delta = Quaternion.Angle(lastCameraRotation, mainCamera.localRotation);
        if (delta > 1)
            playerAimingTimer = 0.1f;

        lastCameraRotation = mainCamera.localRotation;

        // Perform forced rotations
        
        if (playerAimingTimer > 0)
            playerAimingTimer -= Time.deltaTime;

        if (aimTargetTimer > 0)
        {
            aimTargetTimer -= Time.deltaTime;
            aimTargetLerp += Time.deltaTime * aimTargetSpeed;
            aimTargetLerp = Mathf.Clamp01(aimTargetLerp);
        } else if (aimTargetLerp > 0)
        {
            // End aim target lerp without forcing a reset of the game's CameraAim. For VR we want the
            // headset + input to remain authoritative so do not call SetPlayerAim/ResetPlayerAim here.
            aimTargetLerp = 0;
            aimTargetPriority = 999;
            aimTargetActive = false;
        }

        var targetRotation = GetLookRotation(aimTargetPosition);

        if (aimTargetLowImpact)
            targetRotation = Quaternion.Euler(0, targetRotation.eulerAngles.y, 0);
        
        rotation = Quaternion.LerpUnclamped(rotation, targetRotation, cameraAim.AimTargetCurve.Evaluate(aimTargetLerp));
        
        if (aimTargetSoftTimer > 0 && aimTargetTimer <= 0)
        {
            var targetStrength = playerAimingTimer <= 0 ? aimTargetSoftStrengthNoAim : aimTargetSoftStrength;

            aimTargetSoftStrengthCurrent = Mathf.Lerp(aimTargetSoftStrengthCurrent, targetStrength, 10 * Time.deltaTime);

            targetRotation = GetLookRotation(aimTargetSoftPosition);

            if (aimTargetSoftLowImpact)
                targetRotation = Quaternion.Euler(0, targetRotation.eulerAngles.y, 0);
            
            rotation = Quaternion.Lerp(rotation, targetRotation, aimTargetSoftStrengthCurrent * Time.deltaTime);

            aimTargetSoftTimer -= Time.deltaTime;

            if (aimTargetSoftTimer <= 0)
            {
                aimTargetSoftObject = null;
                aimTargetSoftPriority = 999;
            }
        }

        if (!aimTargetActive && aimTargetSoftTimer <= 0)
            rotation = Quaternion.LerpUnclamped(rotation, Quaternion.Euler(0, rotation.eulerAngles.y, 0), 5 * Time.deltaTime);

    // For VR, follow the XR camera rotation and smooth small movements to reduce jitter.
    // Use Slerp for rotation and SmoothDamp for position to keep things stable. Lower the smoothing
    // rate to make motion smoother and reduce random spinning caused by abrupt resets.
    const float rotSmoothSpeed = 6f; // lower = smoother
    const float posSmoothTime = 0.12f; // larger = smoother

    // Target rotation is the headset rotation plus any yaw offset applied by player turning
    var targetLocal = mainCamera.localRotation * Quaternion.Euler(0f, yawOffset, 0f);
    transform.localRotation = Quaternion.Slerp(transform.localRotation, targetLocal, rotSmoothSpeed * Time.deltaTime);
    // Smooth towards zero local offset (camera anchor point)
    transform.localPosition = Vector3.SmoothDamp(transform.localPosition, Vector3.zero, ref localPosVelocity, posSmoothTime);
    }

    private Quaternion GetLookRotation(Vector3 position)
    {
        var desired = Quaternion.LookRotation(position - mainCamera.transform.position, Vector3.up);
        var camDelta = desired * Quaternion.Inverse(mainCamera.transform.rotation);

        return camDelta * transform.rotation;
    }

    /// <summary>
    /// Instantly append a set amount of degrees to the current aim on the Y axis
    /// </summary>
    public void TurnAimNow(float degrees)
    {
        // Accumulate a yaw offset instead of directly setting localRotation. This keeps
        // headset rotation authoritative while allowing smooth/snap turning to rotate
        // the player's aim around the Y axis.
        yawOffset += degrees;
        // Normalize to [-180,180]
        if (yawOffset > 180f) yawOffset -= 360f;
        if (yawOffset < -180f) yawOffset += 360f;
        // Apply immediately so snap turns feel instant
        transform.localRotation = mainCamera.localRotation * Quaternion.Euler(0f, yawOffset, 0f);
        rotation = transform.localRotation;
    }

    /// <summary>
    /// Instantly change the aim rotation without any interpolation or smoothing
    /// </summary>
    public void ForceSetRotation(Vector3 newAngles)
    {
        var rot = Quaternion.Euler(newAngles);

        transform.localRotation = rot;
        rotation = rot;
        // Update yawOffset relative to the current main camera local rotation
        if (mainCamera != null)
        {
            var mainYaw = mainCamera.localRotation.eulerAngles.y;
            var rotYaw = rot.eulerAngles.y;
            yawOffset = Mathf.DeltaAngle(mainYaw, rotYaw);
        }
    }

    /// <summary>
    /// Set spawn rotation, which takes into account the current Y rotation of the headset
    /// </summary>
    public void SetSpawnRotation(float yRot)
    {
        if (CameraNoPlayerTarget.instance)
            yRot = CameraNoPlayerTarget.instance.transform.eulerAngles.y;
        
        var angle = new Vector3(0, yRot - TrackingInput.instance.HeadTransform.localEulerAngles.y, 0);
        
        ForceSetRotation(angle);
    }

    public void SetAimTarget(Vector3 position, float time, float speed, GameObject obj, int priority, bool lowImpact = false)
    {
        if (priority > aimTargetPriority)
            return;

        if (obj != aimTargetObject && aimTargetLerp != 0)
            return;

        aimTargetActive = true;
        aimTargetObject = obj;
        aimTargetPosition = position;
        aimTargetTimer = time;
        aimTargetSpeed = speed;
        aimTargetPriority = priority;
        aimTargetLowImpact = lowImpact;
    }

    public void SetAimTargetSoft(Vector3 position, float time, float strength, float strengthNoAim, GameObject obj,
        int priority, bool lowImpact = false)
    {
        if (priority > aimTargetSoftPriority)
            return;

        if (aimTargetSoftObject && obj != aimTargetSoftObject)
            return;        
        
        if (obj != aimTargetSoftObject)
            playerAimingTimer = 0;

        aimTargetSoftPosition = position;
        aimTargetSoftTimer = time;
        aimTargetSoftStrength = strength;
        aimTargetSoftStrengthNoAim = strengthNoAim;
        aimTargetSoftObject = obj;
        aimTargetSoftPriority = priority;
        aimTargetSoftLowImpact = lowImpact;
    }
}

[RepoXRPatch]
internal static class CameraAimPatches
{
    /// <summary>
    /// Attach a <see cref="VRCameraAim"/> script to all <see cref="CameraAim"/> objects
    /// </summary>
    [HarmonyPatch(typeof(CameraAim), nameof(CameraAim.Awake))]
    [HarmonyPostfix]
    private static void OnCameraAimAwake(CameraAim __instance)
    {
        var comp = __instance.gameObject.AddComponent<VRCameraAim>();

        // Try to set a sensible spawn rotation immediately after the VRCameraAim is attached.
        // Prefer the game's CameraNoPlayerTarget if present, otherwise fall back to the main camera's Y rotation.
        float yRot = 0f;
        if (CameraNoPlayerTarget.instance)
        {
            yRot = CameraNoPlayerTarget.instance.transform.eulerAngles.y;
        }
        else if (UnityEngine.Camera.main)
        {
            yRot = UnityEngine.Camera.main.transform.eulerAngles.y;
        }

        comp.SetSpawnRotation(yRot);
    }
    
    /// <summary>
    /// Disable the game's built in <see cref="CameraAim"/> functionality, as we'll implement that manually in VR 
    /// </summary>
    [HarmonyPatch(typeof(CameraAim), nameof(CameraAim.Update))]
    [HarmonyPrefix]
    private static bool DisableCameraAim(CameraAim __instance)
    {
        return false;
    }
}
