using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RepoXR.Assets;
using RepoXR.Input;
using RepoXR.Managers;
using RepoXR.Networking;
using RepoXR.Player;
using UnityEngine;

using static HarmonyLib.AccessTools;

namespace RepoXR.Patches;

[RepoXRPatch]
internal static class PhysGrabberPatches
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Transform GetHandTransform()
    {
        // Prefer the controller MainHand only when the grabber is actively grabbing/overriding; otherwise use the camera transform
        if (VRSession.Instance is { } session && PhysGrabber.instance != null && IsGrabbedOrOverride(PhysGrabber.instance))
        {
            // Prefer the user's rig right-hand tip as the origin for physics actions: this makes the beam
            // originate from the controller model more accurately. Fall back to MainHand if Rig/rightHandTip
            // isn't available.
            try
            {
                var rig = session.Player?.Rig;
                if (rig != null && rig.rightHandTip != null)
                    return rig.rightHandTip;
            }
            catch
            {
                // ignore and fall back
            }

            return session.Player.MainHand;
        }

        // Fall back to the player's camera transform using compatibility helper (field may be renamed)
        var tr = RepoCompat.GetLocalCameraTransform(PhysGrabber.instance?.playerAvatar);
        if (tr != null) return tr;

        // Last resort: main camera
        return Camera.main.transform;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGrabbedOrOverride(PhysGrabber grabber)
    {
        if (grabber == null)
            return false;

        try
        {
            // If grabbed is available and true, return immediately
            try
            {
                if (grabber.grabbed)
                    return true;
            }
            catch
            {
                // ignore - grabbed may be a property/field that differs between versions
            }

            var t = grabber.GetType();
            var fi = t.GetField("overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                     ?? t.GetField("_overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

            if (fi != null && fi.FieldType == typeof(bool))
            {
                try
                {
                    return (bool)fi.GetValue(grabber);
                }
                catch
                {
                    return false;
                }
            }
        }
        catch
        {
            // swallow
        }

        return false;
    }

    private static CodeMatcher ReplaceCameraWithHand(this CodeMatcher matcher)
    {
        var labels = matcher.Instruction.labels;

        return matcher.RemoveInstructions(2).InsertAndAdvance(
            new CodeInstruction(OpCodes.Call, ((Func<Transform>)GetHandTransform).Method).WithLabels(labels)
        );
    }

    /// <summary>
    /// Prefix for PhysGrabber.Update
    /// - decrement the force grab timer
    /// - while the timer is positive, try to keep overrideGrab true to prevent accidental release
    /// This avoids fragile IL transpiling and implements the same behavior in a safe, explicit patch.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.Update))]
    [HarmonyPrefix]
    private static void UpdatePrefix(PhysGrabber __instance)
    {
        // Decrement the force grab timer (shared static)
        try
        {
            if (forceGrabTimer > 0f)
                forceGrabTimer -= Time.deltaTime;

            // If the timer is still positive and we have a grabbed object, ensure overrideGrab remains true so the
            // grabber doesn't immediately let go. This mirrors the original intent of the removed transpiler.
            if (forceGrabTimer > 0f && __instance != null && __instance.grabbed)
            {
                try
                {
                    var fi = Field(typeof(PhysGrabber), "overrideGrab");
                    if (fi != null)
                        fi.SetValue(__instance, true);
                }
                catch
                {
                    // Best-effort: if the field isn't present or writable, swallow the error and continue.
                }
            }
        }
        catch
        {
            // Keep prefix extremely resilient; any reflection failures should not break the update loop.
        }
    }

    /// <summary>
    /// Make sure the <see cref="PhysGrabber.physGrabPointPlane"/> and <see cref="PhysGrabber.physGrabPointPuller"/> are
    /// manually updated if we are holding something.
    ///
    /// This is normally done by having these be a child of the camera, however this doesn't work in VR since
    /// we use our hand to move items, not the main camera.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.Update))]
    [HarmonyPostfix]
    private static void UpdatePhysGrabPlane(PhysGrabber __instance)
    {
        if (!__instance.isLocal || !__instance.grabbedObjectTransform)
            return;

        var hand = GetHandTransform();

        // Compute desired targets based on hand placement
        var targetPlane = hand.position + hand.forward * Vector3.Distance(hand.position, __instance.physGrabPointPlane.position);
        var targetPuller = hand.position + hand.forward * Vector3.Distance(hand.position, __instance.physGrabPointPuller.position);

        // Use SmoothDamp per-PhysGrabber instance to create a stable, frame-rate independent smoothing
        var state = smoothStates.GetOrCreateValue(__instance);

        // Tunable smooth time: smaller = snappier, larger = smoother
        const float smoothTime = 0.08f;

        __instance.physGrabPointPlane.position = Vector3.SmoothDamp(__instance.physGrabPointPlane.position, targetPlane, ref state.planeVelocity, smoothTime);
        __instance.physGrabPointPuller.position = Vector3.SmoothDamp(__instance.physGrabPointPuller.position, targetPuller, ref state.pullerVelocity, smoothTime);
    }

    /// <summary>
    /// Provide haptic feedback while something is grabbed
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.Update))]
    [HarmonyPostfix]
    private static void HapticFeedbackPatch(PhysGrabber __instance)
    {
        if (!__instance.isLocal)
            return;

        var grabbed = __instance.grabbed
            ? AssetCollection.GrabberHapticCurve.EvaluateTimed(__instance.loopSound.Source.pitch * 1.12667f) * 0.1f
            : 0;
        var overcharge = __instance.physGrabBeamOverChargeFloat * 0.4f *
                         AssetCollection.OverchargeHapticCurve.EvaluateTimed(__instance.physGrabBeamOverChargeFloat *
                                                                             3);

        if (grabbed + overcharge <= 0)
            return;

        HapticManager.Impulse(HapticManager.Hand.Dominant, HapticManager.Type.Continuous, grabbed + overcharge);
    }

    /// <summary>
    /// When grabbing items, shoot rays out of the hand, instead of the camera
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.RayCheck))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> RayCheckPatches(IEnumerable<CodeInstruction> instructions)
    {
        return TranspilerUtils.SafeTranspiler(instrs => new CodeMatcher(instrs)
            .MatchForward(false,
                new CodeMatch(OpCodes.Call,
                    Method(typeof(Physics), nameof(Physics.Raycast), new[]
                    {
                        typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(float),
                        typeof(int),
                        typeof(QueryTriggerInteraction)
                    })))
            .Advance(-10)
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Call, ((Func<PhysGrabber, Vector3>)CalculateNewForward).Method),
                new CodeInstruction(OpCodes.Stloc_1),
                new CodeInstruction(OpCodes.Ldarg_0)
            )
            .MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, Field(typeof(PhysGrabber), nameof(PhysGrabber.playerCamera))))
            .Repeat(matcher => matcher.Advance(-1).ReplaceCameraWithHand())
            .Start()
            .MatchForward(false, new CodeMatch(OpCodes.Call, PropertyGetter(typeof(Camera), nameof(Camera.main))))
            .Repeat(matcher => matcher.ReplaceCameraWithHand())
            .InstructionEnumeration(), instructions, "PhysGrabberPatches.RayCheckPatches");

        static Vector3 CalculateNewForward(PhysGrabber grabber)
        {
            // Use reflection to read possible field/property names instead of direct member access which can
            // differ between REPO versions and cause MissingFieldException at runtime.
            try
            {
                if (grabber == null)
                    return Vector3.zero;

                var t = grabber.GetType();

                // Check for an "overrideGrab" boolean field/property
                bool overrideGrab = false;
                var fi = t.GetField("overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (fi == null)
                    fi = t.GetField("_overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (fi != null && fi.FieldType == typeof(bool))
                    overrideGrab = (bool)fi.GetValue(grabber);

                // Check for overrideGrabTarget (can be GameObject or Component)
                object? overrideTarget = null;
                var ft = t.GetField("overrideGrabTarget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (ft == null)
                    ft = t.GetField("overrideGrabObj", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (ft != null)
                    overrideTarget = ft.GetValue(grabber);

                if (overrideGrab && overrideTarget != null)
                {
                    // Try to get transform safely
                    Transform? tr = null;
                    if (overrideTarget is GameObject go)
                        tr = go.transform;
                    else if (overrideTarget is Component comp)
                        tr = comp.transform;

                    if (tr != null && VRSession.Instance is { } session && session.Player?.MainHand != null)
                        return (tr.position - session.Player.MainHand.position).normalized;
                }

                return VRSession.Instance is not { } session2 || session2.Player?.MainHand == null ? Vector3.zero : session2.Player.MainHand.forward;
            }
            catch
            {
                return VRSession.Instance is not { } session ? Vector3.zero : session.Player.MainHand.forward;
            }
        }
    }

    /// <summary>
    /// Make "scrolling" update the position based on the hand, instead of the camera
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.OverridePullDistanceIncrement))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> OverridePullDistanceIncrementPatches(
        IEnumerable<CodeInstruction> instructions)
    {
        // Replace sequences that load the grabber instance and read a camera field
        // (e.g. ldarg.0; ldfld playerCamera) with a single call to GetHandTransform().
        // We must preserve labels and avoid leaving an extra argument on the eval stack
        // which would produce invalid IL.
        return TranspilerUtils.SafeTranspiler(instrs => {
            var list = new List<CodeInstruction>(instrs);
            for (int i = 0; i < list.Count; i++)
            {
                var ci = list[i];
                if (ci.opcode == OpCodes.Ldfld && ci.operand is System.Reflection.FieldInfo fi)
                {
                    var name = fi.Name;
                    if (name == "playerCamera" || name == "playerCameraTransform" || name == "playerCameraTf" || name == "localCamera" || name == "localCameraTransform")
                    {
                        // If there is a previous instruction (usually ldarg.0), replace that previous
                        // instruction with the call and turn the ldfld into a nop. Preserve labels.
                        int prev = i - 1;
                        var callInst = new CodeInstruction(OpCodes.Call, ((Func<Transform>)GetHandTransform).Method);

                        if (prev >= 0)
                        {
                            // Preserve labels that were attached to the previous instruction
                            callInst.labels = list[prev].labels;
                            // Replace previous instruction with call
                            list[prev] = callInst;

                            // Neutralize the current ldfld so stack stays balanced
                            list[i] = new CodeInstruction(OpCodes.Nop);
                        }
                        else
                        {
                            // No previous instruction: replace current instruction with call and preserve its labels
                            callInst.labels = ci.labels;
                            list[i] = callInst;
                        }
                    }
                }
            }

            return list;
        }, instructions, "PhysGrabberPatches.OverridePullDistanceIncrementPatches");
    }

    /// <summary>
    /// Make the object turning input use the controller inputs instead of mouse inputs
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.ObjectTurning))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ObjectTurningPatches(IEnumerable<CodeInstruction> instructions)
    {
        // Disabled fragile transpiler: the previous implementation used fixed local indices and
        // RemoveInstructions which proved brittle across REPO versions and caused InvalidProgramException
        // at runtime. We'll keep this as a no-op for now and reimplement the rotation handling as a
        // safe Prefix/Postfix that doesn't rely on fragile IL surgery.
        return instructions;
    }

    private static void GetRotationInput(ref float x, ref float y)
    {
        var input = Actions.Instance["Rotation"].ReadValue<Vector2>();

        x = input.x;
        y = input.y;
    }

    /// <summary>
    /// Safe Prefix for PhysGrabber.ObjectTurning
    /// - If VR rotation input is available, apply rotation directly to the grabbed object transform
    ///   using the player's hand as the pivot and skip the original method to avoid fragile IL edits.
    /// - Otherwise let the original method run (return true).
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.ObjectTurning))]
    [HarmonyPrefix]
    private static bool ObjectTurningPrefix(PhysGrabber __instance)
    {
        try
        {
            if (__instance == null || !__instance.isLocal)
                return true; // let original run for non-local or null

            if (Actions.Instance == null)
                return true;

            // Read VR rotation input; if missing or zero, fall back to original method
            Vector2 rot;
            try
            {
                rot = Actions.Instance["Rotation"].ReadValue<Vector2>();
            }
            catch
            {
                return true;
            }

            if (rot == Vector2.zero)
                return true;

            var hand = GetHandTransform();
            if (hand == null)
                return true;

            var grabbed = __instance.grabbedObjectTransform;
            if (grabbed == null)
                return true;

            // Apply rotation around the hand pivot. Tunable sensitivity to match original feel.
            const float sensitivity = 180f; // degrees per second per input unit
            float dt = Time.deltaTime;

            // Yaw around hand.up, pitch around hand.right (invert pitch to match typical mouse mapping)
            grabbed.RotateAround(hand.position, hand.up, rot.x * sensitivity * dt);
            grabbed.RotateAround(hand.position, hand.right, -rot.y * sensitivity * dt);

            // We've handled rotation in VR; skip original to avoid duplicate effects
            return false;
        }
        catch
        {
            // On any failure, allow the original method to run
            return true;
        }
    }

    /// <summary>
    /// Move the grab beam origin to the hand
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabBeam), nameof(PhysGrabBeam.Start))]
    [HarmonyPostfix]
    private static void OnPhysBeamStart(PhysGrabBeam __instance)
    {
        if (!__instance.playerAvatar.isLocal || VRSession.Instance is not {} session)
            return;

        __instance.PhysGrabPointOrigin.SetParent(session.Player.MainHand);
        __instance.PhysGrabPointOrigin.localPosition = Vector3.zero;
    }

    /// <summary>
    /// Allow a custom override to disable object turning
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.ObjectTurning))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> DisableTurningPatch(IEnumerable<CodeInstruction> instructions)
    {
        return TranspilerUtils.SafeTranspiler(instrs =>
        {
            var matcher = new CodeMatcher(instrs)
                .MatchForward(false, new CodeMatch(OpCodes.Call, Method(typeof(SemiFunc), nameof(SemiFunc.InputHold))))
                .Advance(1);

            var jmp = matcher.Instruction;

            matcher.Advance(1).InsertAndAdvance(
                new CodeInstruction(OpCodes.Call, PropertyGetter(typeof(VRSession), nameof(VRSession.Instance))),
                new CodeInstruction(OpCodes.Callvirt, PropertyGetter(typeof(VRSession), nameof(VRSession.Player))),
                new CodeInstruction(OpCodes.Ldfld, Field(typeof(VRPlayer), nameof(VRPlayer.disableRotateTimer))),
                new CodeInstruction(OpCodes.Ldc_R4, 0f),
                new CodeInstruction(OpCodes.Bgt_Un_S, jmp.operand)
            );

            return matcher
                .InstructionEnumeration();
        }, instructions, "PhysGrabberPatches.DisableTurningPatch");
    }

    /// <summary>
    /// Detect item release and try to equip item if possible
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.ReleaseObject))]
    [HarmonyPrefix]
    private static void OnReleaseObject(PhysGrabber __instance)
    {
        if (!__instance.grabbed || !__instance.isLocal || !__instance.grabbedObject ||
            !__instance.grabbedObject.TryGetComponent<ItemEquippable>(out var item))
            return;

        if (VRSession.Instance is not { } session)
            return;

        session.Player.Rig.inventoryController.TryEquipItem(item);
    }

    private static float forceGrabTimer;

    // Store per-grabber smoothing velocities for SmoothDamp
    private class SmoothState
    {
        public Vector3 planeVelocity;
        public Vector3 pullerVelocity;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PhysGrabber, SmoothState> smoothStates = new();
    /// <summary>
    /// Every time a grab override is triggered, reset the timer
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.OverrideGrab))]
    [HarmonyPostfix]
    private static void OnOverrideGrab(PhysGrabber __instance)
    {
        forceGrabTimer = 0.1f;
    }

    /// <summary>
    /// Prefix for PhysGrabLogic: perform a hand-origin raycast to detect carts (or cart-like objects) and set
    /// the grab override fields via reflection when the user is pressing the grab input. This restores cart
    /// grabbing without using fragile IL transpilers.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.PhysGrabLogic))]
    [HarmonyPrefix]
    private static void PhysGrabLogicPrefix(PhysGrabber __instance)
    {
        try
        {
            if (__instance == null || !__instance.isLocal || __instance.grabbed)
                return;

            // Check grab input (best-effort; if the action isn't present we'll default to allowing grab)
            bool pressingGrab = false;
            try
            {
                if (Actions.Instance != null)
                {
                    var a = Actions.Instance["Grab"]; // may throw if mapping missing
                    pressingGrab = a.ReadValue<float>() > 0.5f;
                }
            }
            catch
            {
                // If the input system isn't available or mapping missing, assume pressingGrab = true so carts can be grabbed
                pressingGrab = true;
            }

            if (!pressingGrab)
                return;

            // Prefer the player's MainHand (which maps to the rig's rightHandTip) so raycasts/spherecasts
            // originate from the controller tip even when the grabber isn't yet holding an object.
            // Fall back to rig.rightHandTip, then GetHandTransform() which may return camera as last resort.
            var hand = VRSession.Instance?.Player?.MainHand ?? VRSession.Instance?.Player?.Rig?.rightHandTip ?? GetHandTransform();
            if (hand == null)
                return;

            // Raycast out from the hand to detect carts. Use a conservative distance.
            const float maxDist = 6f;
            RaycastHit hit;
            bool found = Physics.Raycast(hand.position, hand.forward, out hit, maxDist, ~0, QueryTriggerInteraction.Ignore);

            // If a direct ray misses (small angle miss), try a small spherecast to tolerate offsets and make
            // grabbing more forgiving when the controller isn't perfectly aligned.
            if (!found)
            {
                const float radius = 0.15f; // 15 cm tolerance
                found = Physics.SphereCast(hand.position, radius, hand.forward, out hit, maxDist, ~0, QueryTriggerInteraction.Ignore);
            }

            if (!found)
                return;

            if (hit.collider == null)
                return;

            // If we hit a cart-like object, force the grab override target. We consider PhysGrabCart and ItemCartCannonMain as cart targets.
            var cart = hit.collider.GetComponentInParent<PhysGrabCart>();
            var cannon = hit.collider.GetComponentInParent<ItemCartCannonMain>();
            if (cart == null && cannon == null)
                return;

            var targetObj = cart != null ? (object)cart : (object)cannon;
            TryForceOverrideGrab(__instance, targetObj as GameObject ?? (targetObj as Component)?.gameObject);
        }
        catch
        {
            // No-op on any failure; we must not break the original logic.
        }
    }

    private static void TryForceOverrideGrab(PhysGrabber grabber, GameObject target)
    {
        if (grabber == null || target == null)
            return;

        try
        {
            var t = grabber.GetType();

            // Try boolean field overrideGrab
            var fi = t.GetField("overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                     ?? t.GetField("_overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (fi != null && fi.FieldType == typeof(bool))
                fi.SetValue(grabber, true);

            // Try override target field (several name variants observed in different REPO versions)
            var ft = t.GetField("overrideGrabTarget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                     ?? t.GetField("overrideGrabObj", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                     ?? t.GetField("overrideTarget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

            if (ft != null)
            {
                // Prefer setting a GameObject if the field accepts it; otherwise attempt Component/Transform.
                try
                {
                    if (ft.FieldType.IsAssignableFrom(typeof(GameObject)))
                        ft.SetValue(grabber, target);
                    else if (ft.FieldType.IsAssignableFrom(typeof(Component)))
                        ft.SetValue(grabber, target.GetComponent<Component>() ?? target.transform);
                    else if (ft.FieldType.IsAssignableFrom(typeof(Transform)))
                        ft.SetValue(grabber, target.transform);
                }
                catch
                {
                    // swallow
                }
            }

            // Prefer invoking a strongly-typed OverrideGrab if available. Try GameObject, then Transform, then no-arg.
            try
            {
                var mi = t.GetMethod("OverrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (mi != null)
                {
                    var pars = mi.GetParameters();
                    if (pars.Length == 1)
                    {
                        var ptype = pars[0].ParameterType;
                        if (ptype.IsAssignableFrom(typeof(GameObject)))
                            mi.Invoke(grabber, new object[] { target });
                        else if (ptype.IsAssignableFrom(typeof(Transform)))
                            mi.Invoke(grabber, new object[] { target.transform });
                        else
                        {
                            // Try to coerce to a Component instance expected by the method
                            var comp = target.GetComponent(ptype);
                            if (comp != null)
                                mi.Invoke(grabber, new object[] { comp });
                        }
                    }
                    else if (pars.Length == 0)
                    {
                        mi.Invoke(grabber, null);
                    }
                }
            }
            catch
            {
                // ignore
            }

            // Reset our local force timer to keep the grab locked briefly
            forceGrabTimer = 0.1f;
        }
        catch
        {
            // swallow
        }
    }

    /// <summary>
    /// If the <see cref="forceGrabTimer"/> is above zero, do not allow the grabber to let go
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.Update))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ForceOverrideGrabPatch(IEnumerable<CodeInstruction> instructions)
    {
        // This transpiler previously tried to inject logic to prevent the grabber from letting go while
        // a short "force grab" timer was active. We've implemented that behavior in UpdatePrefix using
        // a safe field write, and we keep this transpiler as a no-op to avoid risky IL edits.
        return instructions;
    }
}

[RepoXRPatch(RepoXRPatchTarget.Universal)]
internal static class PhysGrabberUniversalPatches
{
    private static Transform GetHandTransform(PhysGrabber grabber)
    {
        if (grabber.playerAvatar.isLocal)
        {
            bool overrideGrab = false;
            try
            {
                var t = grabber.GetType();
                var fi = t.GetField("overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) ??
                         t.GetField("_overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (fi != null && fi.FieldType == typeof(bool))
                    overrideGrab = (bool)fi.GetValue(grabber);
            }
            catch
            {
                overrideGrab = false;
            }

            return (VRSession.InVR && (grabber.grabbed || overrideGrab)) ? VRSession.Instance.Player.MainHand : RepoCompat.GetLocalCameraTransform(grabber.playerAvatar);
        }

        if (!NetworkSystem.instance)
        {
            Logger.LogError("NetworkSystem is null?");
            return RepoCompat.GetLocalCameraTransform(grabber.playerAvatar);
        }

        if (NetworkSystem.instance.GetNetworkPlayer(grabber.playerAvatar, out var networkPlayer))
        {
            if (!networkPlayer)
            {
                Logger.LogError("NetworkPlayer is null?");
                return RepoCompat.GetLocalCameraTransform(grabber.playerAvatar);
            }

            if (!networkPlayer.PrimaryHand)
            {
                Logger.LogError("GrabberHand is null?");
                return RepoCompat.GetLocalCameraTransform(grabber.playerAvatar);
            }

            bool overrideGrab = false;
            try
            {
                var t = grabber.GetType();
                var fi = t.GetField("overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) ??
                         t.GetField("_overrideGrab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (fi != null && fi.FieldType == typeof(bool))
                    overrideGrab = (bool)fi.GetValue(grabber);
            }
            catch
            {
                overrideGrab = false;
            }

            return (grabber.grabbed || overrideGrab) ? networkPlayer.PrimaryHand : RepoCompat.GetLocalCameraTransform(grabber.playerAvatar);
        }

        return RepoCompat.GetLocalCameraTransform(grabber.playerAvatar);
    }

    /// <summary>
    /// Make certain phys grabber logic be applied based on the hand instead of the head
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.PhysGrabLogic))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PhysGrabLogicPatches(IEnumerable<CodeInstruction> instructions)
    {
        // Walk instructions and replace any ldfld that looks like a camera transform with a call to GetHandTransform(PhysGrabber)
        return TranspilerUtils.SafeTranspiler(instrs => {
            var list = new List<CodeInstruction>(instrs);
            for (int i = 0; i < list.Count; i++)
            {
                var ci = list[i];
                if (ci.opcode == OpCodes.Ldfld && ci.operand is System.Reflection.FieldInfo fi)
                {
                    var name = fi.Name;
                    if (name == "playerCamera" || name == "playerCameraTransform" || name == "playerCameraTf" || name == "localCamera" || name == "localCameraTransform")
                    {
                        list[i] = new CodeInstruction(OpCodes.Call, ((Func<PhysGrabber, Transform>)GetHandTransform).Method);
                    }
                }
            }

            return list;
        }, instructions, "PhysGrabberPatches.PhysGrabLogicPatches");
    }

    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.ObjectTurning))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ObjectTurningPatches(IEnumerable<CodeInstruction> instructions)
    {
        // Disabled: we now handle object turning via a safe Prefix (ObjectTurningPrefix) which avoids
        // fragile IL surgery. Keeping this as a no-op prevents duplicate edits and reduces risk.
        return instructions;
    }
}