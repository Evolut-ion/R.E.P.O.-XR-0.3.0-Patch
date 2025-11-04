using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using UnityEngine;
using HarmonyLib;
using RepoXR.Assets;
using RepoXR.Managers;
using RepoXR.Player.Camera;
using static HarmonyLib.AccessTools;

namespace RepoXR.Patches.Enemy;

[RepoXRPatch]
internal static class EnemyCeilingEyePatches
{
    /// <summary>
    /// Replace <see cref="CameraAim.AimTargetSoftSet"/> with <see cref="VRCameraAim.SetAimTargetSoft"/>
    /// </summary>
    [HarmonyPatch(typeof(EnemyCeilingEye), nameof(EnemyCeilingEye.Update))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> SetCameraSoftRotationPatch(IEnumerable<CodeInstruction> instructions)
    {
        // Disabled: per-version IL differences made this transpiler fragile. We intercept CameraAim calls directly
        // via prefixes instead; keep this transpiler as a no-op.
        return instructions;
    }

    /// <summary>
    /// Replace <see cref="CameraAim.AimTargetSet"/> with <see cref="VRCameraAim.SetAimTarget"/>
    /// </summary>
    [HarmonyPatch(typeof(EnemyCeilingEye), nameof(EnemyCeilingEye.UpdateStateRPC))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> SetCameraRotationPatch(IEnumerable<CodeInstruction> instructions)
    {
        // Disabled: per-version IL differences made this transpiler fragile. We intercept CameraAim calls directly
        // via prefixes instead; keep this transpiler as a no-op.
        return instructions;
    }

    [HarmonyPatch(typeof(CameraAim), nameof(CameraAim.AimTargetSet))]
    [HarmonyPrefix]
    private static bool CameraAim_AimTargetSet_Prefix(CameraAim __instance, Vector3 position, float time, float speed, GameObject obj, int priority)
    {
        if (VRCameraAim.instance != null)
        {
            // VRCameraAim accepts a lowImpact flag; CameraAim doesn't — pass false
            VRCameraAim.instance.SetAimTarget(position, time, speed, obj, priority, false);
            return false; // skip original
        }

        return true; // run original
    }

    [HarmonyPatch(typeof(CameraAim), "AimTargetSoftSet")]
    [HarmonyPrefix]
    private static bool CameraAim_AimTargetSoftSet_Prefix(CameraAim __instance, Vector3 position, float time, float strength, float strengthNoAim, GameObject obj, int priority)
    {
        if (VRCameraAim.instance != null)
        {
            VRCameraAim.instance.SetAimTargetSoft(position, time, strength, strengthNoAim, obj, priority, false);
            return false;
        }

        return true;
    }

    // The transpilers above attempted to mutate calls to CameraAim.AimTargetSet/AimTargetSoftSet. These
    // transpilers were brittle across REPO versions (index/offset differences). Instead, we intercept the
    // game's CameraAim methods globally and delegate to our VRCameraAim when available. This is simpler
    // and avoids per-call IL surgery.
    // NOTE: Some REPO versions don't have the optional "lowImpact" parameter on CameraAim methods.
    // We already provide prefixes that match the common signature (without lowImpact) above. Removing
    // the duplicated overloads that declare the extra parameter prevents Harmony from trying to map
    // a nonexistent parameter and failing with "Parameter \"lowImpact\" not found".

    /// <summary>
    /// Provide haptic feedback while attached to the ceiling eye
    /// </summary>
    [HarmonyPatch(typeof(EnemyCeilingEye), nameof(EnemyCeilingEye.Update))]
    [HarmonyPostfix]
    private static void EyeAttachHapticFeedback(EnemyCeilingEye __instance)
    {
        if (__instance.currentState != EnemyCeilingEye.State.HasTarget)
            return;
        
        if (!__instance.targetPlayer || !__instance.targetPlayer.isLocal)
            return;

        // Want a higher priority than damage, so we use 11
        HapticManager.Impulse(HapticManager.Hand.Both, HapticManager.Type.Continuous,
            0.2f * AssetCollection.EyeAttachHapticCurve.EvaluateTimed(0.25f), priority: 11);
    }
}