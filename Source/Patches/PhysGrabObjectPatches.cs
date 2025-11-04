using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RepoXR.Managers;
using RepoXR.Networking;
using UnityEngine;

using static HarmonyLib.AccessTools;

namespace RepoXR.Patches;

[RepoXRPatch(RepoXRPatchTarget.Universal)]
internal static class PhysGrabObjectPatches
{
    private static Transform GetTargetTransform(PlayerAvatar player)
    {
        if (player.isLocal)
            return VRSession.Instance is { } session ? session.Player.MainHand : RepoCompat.GetLocalCameraTransform(player);

        return NetworkSystem.instance.GetNetworkPlayer(player, out var networkPlayer)
            ? networkPlayer.PrimaryHand
            : RepoCompat.GetLocalCameraTransform(player);
    }

    private static Quaternion GetTargetRotation(PlayerAvatar player)
    {
        if (player.isLocal)
            return VRSession.Instance is { } session ? session.Player.MainHand.rotation : RepoCompat.GetLocalCameraRotation(player);

        return NetworkSystem.instance.GetNetworkPlayer(player, out var networkPlayer)
            ? networkPlayer.PrimaryHand.rotation
            : RepoCompat.GetLocalCameraRotation(player);
    }

    private static Vector3 GetTargetPosition(PlayerAvatar player)
    {
        if (player.isLocal)
            return VRSession.Instance is { } session ? session.Player.MainHand.position : RepoCompat.GetLocalCameraPosition(player);

        return NetworkSystem.instance.GetNetworkPlayer(player, out var networkPlayer)
            ? networkPlayer.PrimaryHand.position
            : RepoCompat.GetLocalCameraPosition(player);
    }

    private static Transform GetCartSteerTransform(PhysGrabber grabber)
    { 
        if (grabber.playerAvatar.isLocal)
            return VRSession.Instance is { } session ? session.Player.MainHand : grabber.transform;

        return NetworkSystem.instance.GetNetworkPlayer(grabber.playerAvatar, out var networkPlayer)
            ? networkPlayer.PrimaryHand
            : grabber.transform;
    }

    /// <summary>
    /// Apply object rotation based on hand rotation instead of camera rotation
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabObject), nameof(PhysGrabObject.PhysicsGrabbingManipulation))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> HandRelativeMovementPatch(IEnumerable<CodeInstruction> instructions)
    {
        // Disabled: instruction-level replacements caused InvalidProgram at runtime on some REPO builds.
        // Keep as a no-op transpiler for now; we'll implement VR-hand behavior using postfix/prefix methods.
        return instructions;
    }

    /// <summary>
    /// Apply cart steering rotation based on hand rotation instead of camera rotation
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabCart), nameof(PhysGrabCart.CartSteer))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> HandRelativeCartPatch(IEnumerable<CodeInstruction> instructions)
    {
        // Disabled: keep original IL to avoid runtime IL issues. We'll use safer hooks instead.
        return instructions;
    }

    /// <summary>
    /// Apply cart cannon rotations based on the hand position and rotation
    /// </summary>
    [HarmonyPatch(typeof(ItemCartCannonMain), nameof(ItemCartCannonMain.GrabLogic))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> HandRelativeCartCannonPatch(IEnumerable<CodeInstruction> instructions)
    {
        // Disabled: this transpiler introduced invalid IL on some REPO versions. Reverting to no-op.
        return instructions;
    }

    /// <summary>
    /// Update the rotation target based on the hand rotation
    /// </summary>
    [HarmonyPatch(typeof(ItemCartCannonMain), nameof(ItemCartCannonMain.CorrectorAndLightLogic))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> RotationTargetHandRelative(IEnumerable<CodeInstruction> instructions)
    {
        // Disabled: keep original IL to avoid runtime IL errors; we'll provide hand-based behavior elsewhere.
        return instructions;
    }
}