using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RepoXR.Managers;
using UnityEngine;

using static HarmonyLib.AccessTools;

namespace RepoXR.Patches;

/// <summary>
/// The idea of all these patches are to reduce and nullify some of the camera effects as they can be nauseating in VR
/// </summary>
[RepoXRPatch]
internal static class CameraEffectsPatches
{
    /// <summary>
    /// Disable camera shake in the main menu
    /// </summary>
    [HarmonyPatch(typeof(CameraShake), nameof(CameraShake.Shake))]
    [HarmonyPrefix]
    private static bool DisableShakeMenu()
    {
        return !SemiFunc.MenuLevel();
    }
    
    /// <summary>
    /// Camera noise is the idle sway, which is a nono in VR
    /// </summary>
    [HarmonyPatch(typeof(CameraNoise), nameof(CameraNoise.Awake))]
    [HarmonyPostfix]
    private static void DisableCameraNoise(CameraNoise __instance)
    {
        __instance.AnimNoise.enabled = false;
        __instance.transform.localPosition = Vector3.zero;
        __instance.transform.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// No noise please (even when crouched)
    /// </summary>
    [HarmonyPatch(typeof(CameraCrouchNoise), nameof(CameraCrouchNoise.Start))]
    [HarmonyPostfix]
    private static void DisableCameraCrouchNoise(CameraCrouchNoise __instance)
    {
        __instance.enabled = false;
        __instance.transform.localPosition = Vector3.zero;
        __instance.transform.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Reduce camera bobbing by 75%
    /// </summary>
    [HarmonyPatch(typeof(CameraBob), nameof(CameraBob.Update))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ReduceCameraBob(IEnumerable<CodeInstruction> instructions)
    {
        return TranspilerUtils.SafeTranspiler(instrs => new CodeMatcher(instrs)
            .MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, Field(typeof(GameplayManager), nameof(GameplayManager.cameraAnimation))))
            .Repeat(matcher => matcher.Advance(1).InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldc_R4, 0.25f),
                new CodeInstruction(OpCodes.Mul)
            ))
            .InstructionEnumeration(), instructions, "CameraEffectsPatches.ReduceCameraBob");
    }

    /// <summary>
    /// Reduce camera jump effects by 75%
    /// </summary>
    [HarmonyPatch(typeof(CameraJump), nameof(CameraJump.Update))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ReduceCameraJump(IEnumerable<CodeInstruction> instructions)
    {
        return TranspilerUtils.SafeTranspiler(instrs => new CodeMatcher(instrs)
            .MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, Field(typeof(GameplayManager), nameof(GameplayManager.cameraAnimation))))
            .Repeat(matcher => matcher.Advance(1).InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldc_R4, 0.25f),
                new CodeInstruction(OpCodes.Mul)
            ))
            .InstructionEnumeration(), instructions, "CameraEffectsPatches.ReduceCameraJump");
    }

    /// <summary>
    /// Reduce camera shake by 75%
    /// </summary>
    [HarmonyPatch(typeof(CameraShake), nameof(CameraShake.ShakeMultiplier))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ReduceCameraShake(IEnumerable<CodeInstruction> instructions)
    {
        return TranspilerUtils.SafeTranspiler(instrs => new CodeMatcher(instrs)
            .MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, Field(typeof(GameplayManager), nameof(GameplayManager.cameraShake))))
            .Advance(1)
            .Insert(
                new CodeInstruction(OpCodes.Ldc_R4, 0.25f),
                new CodeInstruction(OpCodes.Mul)
            )
            .InstructionEnumeration(), instructions, "CameraEffectsPatches.ReduceCameraShake");
    }

    /// <summary>
    /// Reduce tilt caused by fast rotation by 75%
    /// </summary>
    [HarmonyPatch(typeof(CameraTilt), nameof(CameraTilt.Update))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ReduceRotationTilt(IEnumerable<CodeInstruction> instructions)
    {
        return TranspilerUtils.SafeTranspiler(instrs => new CodeMatcher(instrs)
            .MatchForward(false, new CodeMatch(OpCodes.Stfld, Field(typeof(CameraTilt), nameof(CameraTilt.tiltZresult))))
            .Advance(-10)
            .Insert(
                new CodeInstruction(OpCodes.Ldc_R4, 0.25f),
                new CodeInstruction(OpCodes.Mul)
            )
            .InstructionEnumeration(), instructions, "CameraEffectsPatches.ReduceRotationTilt");
    }

    [HarmonyPatch(typeof(CameraGlitch), nameof(CameraGlitch.PlayLong))]
    [HarmonyPostfix]
    private static void CameraGlitchLongFeedback()
    {
        HapticManager.Impulse(HapticManager.Hand.Both, HapticManager.Type.Impulse, 0.2f, 0.3f);
    }

    [HarmonyPatch(typeof(CameraGlitch), nameof(CameraGlitch.PlayShort))]
    [HarmonyPostfix]
    private static void CameraGlitchShortFeedback()
    {
        HapticManager.Impulse(HapticManager.Hand.Both, HapticManager.Type.Impulse, 0.2f);
    }

    [HarmonyPatch(typeof(CameraGlitch), nameof(CameraGlitch.PlayTiny))]
    [HarmonyPostfix]
    private static void CameraGlitchTinyFeedback()
    {
        HapticManager.Impulse(HapticManager.Hand.Both, HapticManager.Type.Impulse, 0.1f, 0.05f);
    }

    [HarmonyPatch(typeof(CameraGlitch), nameof(CameraGlitch.PlayLongHeal))]
    [HarmonyPostfix]
    private static void CameraGlitchLongHealFeedback()
    {
        HapticManager.Impulse(HapticManager.Hand.Both, HapticManager.Type.Impulse, 0.25f, 0.2f);
    }

    [HarmonyPatch(typeof(CameraGlitch), nameof(CameraGlitch.PlayShortHeal))]
    [HarmonyPostfix]
    private static void CameraGlitchShortHealFeedback()
    {
        HapticManager.Impulse(HapticManager.Hand.Both, HapticManager.Type.Impulse, 0.15f, 0.2f);
    }

    [HarmonyPatch(typeof(CameraGlitch), nameof(CameraGlitch.PlayUpgrade))]
    [HarmonyPostfix]
    private static void CameraGlitchUpgradeFeedback()
    {
        HapticManager.Impulse(HapticManager.Hand.Both, HapticManager.Type.Impulse, 0.2f, 0.5f);
    }
}