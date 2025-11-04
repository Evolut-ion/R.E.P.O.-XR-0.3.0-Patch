using System;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RepoXR
{
    internal static class RepoCompat
    {
        /// <summary>
        /// Safely call PhysGrabber.ReleaseObject on a runtime PhysGrabber instance.
        /// Tries common overloads (float, parameterless) via reflection so the mod
        /// works across REPO versions with differing signatures.
        /// </summary>
        public static void ReleaseObjectSafe(object? physGrabber)
        {
            if (physGrabber == null) return;

            var t = physGrabber.GetType();

            try
            {
                // Try float overload first
                var mFloat = t.GetMethod("ReleaseObject", new[] { typeof(float) });
                if (mFloat != null)
                {
                    mFloat.Invoke(physGrabber, new object[] { 0f });
                    return;
                }

                // Fallback to parameterless
                var mParamless = t.GetMethod("ReleaseObject", Type.EmptyTypes);
                if (mParamless != null)
                {
                    mParamless.Invoke(physGrabber, null);
                    return;
                }

                // As a last resort, try any method named ReleaseObject with 1..3 parameters and attempt to pass sensible zeros
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "ReleaseObject") continue;

                    var pars = m.GetParameters();
                    try
                    {
                        if (pars.Length == 1 && pars[0].ParameterType.IsPrimitive)
                        {
                            object arg = Convert.ChangeType(0, pars[0].ParameterType);
                            m.Invoke(physGrabber, new[] { arg });
                            return;
                        }

                        if (pars.Length == 2)
                        {
                            var a0 = pars[0].ParameterType.IsPrimitive ? Convert.ChangeType(0, pars[0].ParameterType) : null;
                            var a1 = pars[1].ParameterType.IsPrimitive ? Convert.ChangeType(0, pars[1].ParameterType) : null;
                            m.Invoke(physGrabber, new object?[] { a0, a1 });
                            return;
                        }

                        if (pars.Length == 3)
                        {
                            var a0 = pars[0].ParameterType.IsPrimitive ? Convert.ChangeType(0, pars[0].ParameterType) : null;
                            var a1 = pars[1].ParameterType.IsPrimitive ? Convert.ChangeType(0, pars[1].ParameterType) : null;
                            var a2 = pars[2].ParameterType.IsPrimitive ? Convert.ChangeType(0, pars[2].ParameterType) : null;
                            m.Invoke(physGrabber, new object?[] { a0, a1, a2 });
                            return;
                        }
                    }
                    catch
                    {
                        // ignore invocation errors and try next overload
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"RepoCompat: Failed to call ReleaseObject on {t.FullName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to add the XR Toolkit TrackedDeviceGraphicRaycaster if it exists at runtime, otherwise
        /// fall back to the standard GraphicRaycaster so UI still receives raycasts.
        /// Returns the added or existing raycaster component.
        /// </summary>
        public static Component AddTrackedDeviceRaycaster(GameObject go)
        {
            if (go == null) return null;

            // Try to find the XR Toolkit type in any loaded assembly (robust against missing package at runtime)
            Type? trackedType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                trackedType = asm.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster");
                if (trackedType != null)
                    break;
            }

            if (trackedType != null)
            {
                try
                {
                    var existing = go.GetComponent(trackedType);
                    if (existing != null)
                        return existing;

                    return go.AddComponent(trackedType) as Component;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"RepoCompat: failed to add TrackedDeviceGraphicRaycaster: {ex.Message}");
                }
            }

            // Fallback to the built-in GraphicRaycaster
            var gr = go.GetComponent<GraphicRaycaster>();
            if (gr != null)
                return gr;

            return go.AddComponent<GraphicRaycaster>();
        }

        /// <summary>
        /// Get a Transform representing the player's local camera in a runtime-version-safe way.
        /// Tries several possible field/property names that have changed between REPO versions.
        /// </summary>
        private static Transform? fallbackTransform;

        public static Transform GetLocalCameraTransform(object? playerAvatar)
        {
            if (playerAvatar == null)
                return GetOrCreateFallbackTransform();

            var t = playerAvatar.GetType();

            // Try direct field 'localCameraTransform' (old versions)
            var f = t.GetField("localCameraTransform");
            if (f != null)
            {
                var v = f.GetValue(playerAvatar) as Transform;
                if (v != null) return v;
            }

            // Try 'localCamera' field which is a PlayerLocalCamera component in newer versions
            var f2 = t.GetField("localCamera");
            if (f2 != null)
            {
                var localCam = f2.GetValue(playerAvatar);
                if (localCam is Component comp)
                    return comp.transform;

                // Try to get a 'transform' property via reflection if it's not a Component
                var prop = localCam?.GetType().GetProperty("transform");
                if (prop != null)
                {
                    var tr = prop.GetValue(localCam) as Transform;
                    if (tr != null) return tr;
                }
            }

            // Try direct properties for position/rotation container
            var propTransform = t.GetProperty("localCameraTransform");
            if (propTransform != null)
            {
                var v = propTransform.GetValue(playerAvatar) as Transform;
                if (v != null) return v;
            }

            // Fallback to main camera or the avatar's transform
            var cam = Camera.main?.transform;
            if (cam != null) return cam;

            // Last resort: if the avatar is a Component, return its transform
            if (playerAvatar is Component c)
                return c.transform;

            return GetOrCreateFallbackTransform();
        }

        public static Vector3 GetLocalCameraPosition(object? playerAvatar)
        {
            var tr = GetLocalCameraTransform(playerAvatar);
            return tr != null ? tr.position : Vector3.zero;
        }

        public static Quaternion GetLocalCameraRotation(object? playerAvatar)
        {
            var tr = GetLocalCameraTransform(playerAvatar);
            return tr != null ? tr.rotation : Quaternion.identity;
        }

        private static Transform GetOrCreateFallbackTransform()
        {
            if (fallbackTransform != null)
                return fallbackTransform;

            // Try main camera
            var cam = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                fallbackTransform = cam.transform;
                return fallbackTransform;
            }

            // Create a lightweight fallback object to return a valid Transform reference
            var go = new GameObject("RepoXR_FallbackCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            fallbackTransform = go.transform;
            return fallbackTransform;
        }
    }
}
