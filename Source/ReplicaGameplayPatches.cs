using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    internal static class ReplicaGameplayAuthority
    {
        internal static bool Suppress(Component component)
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            return plugin != null && plugin.ShouldBlockVanillaWorldInput && ReplicatedObjectState.IsReplicaComponent(component);
        }

        internal static MethodBase Required(Type type, string name)
        {
            MethodInfo method = AccessTools.DeclaredMethod(type, name, Type.EmptyTypes);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }

    [HarmonyPatch]
    internal static class ReplicaBiologySimulationPatch
    {
        // Verified against the installed 1.27.17 game assembly. Update as well
        // as FixedUpdate mutates health/blood/burn values in this version.
        // Keep Start/Awake, render callbacks and teardown fully native.
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return ReplicaGameplayAuthority.Required(typeof(PhysicalBehaviour), "ManagedFixedUpdate");
            yield return ReplicaGameplayAuthority.Required(typeof(LimbBehaviour), "ManagedFixedUpdate");
            yield return ReplicaGameplayAuthority.Required(typeof(LimbBehaviour), "ManagedUpdate");
            yield return ReplicaGameplayAuthority.Required(typeof(CirculationBehaviour), "FixedUpdate");
            // Inherited Update invokes liquid effects/mixing independently of
            // Circulation.FixedUpdate, so it must also remain host-owned.
            yield return ReplicaGameplayAuthority.Required(typeof(BloodContainer), "Update");
            yield return ReplicaGameplayAuthority.Required(typeof(PersonBehaviour), "FixedUpdate");
            yield return ReplicaGameplayAuthority.Required(typeof(PersonBehaviour), "Update");
            yield return ReplicaGameplayAuthority.Required(typeof(PersonBehaviour), "LateUpdate");
            // Update regenerates/adds wounds, LateUpdate progresses rot;
            // OnWillRenderObject still updates the native material rendering.
            yield return ReplicaGameplayAuthority.Required(typeof(SkinMaterialHandler), "Update");
            yield return ReplicaGameplayAuthority.Required(typeof(SkinMaterialHandler), "LateUpdate");
        }

        private static bool Prefix(Component __instance)
        {
            return !ReplicaGameplayAuthority.Suppress(__instance);
        }
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "ManagedUpdate")]
    internal static class ReplicaPhysicalVisualUpdatePatch
    {
        // Native ManagedUpdate mixes particle rendering with burn simulation,
        // fire propagation, and out-of-bounds destruction. Calling its verified
        // visual helper preserves fire/charge/smoke without advancing the world.
        // This is a fixed local method, never selected by remote packet data.
        private static readonly Action<PhysicalBehaviour> updateParticles =
            (Action<PhysicalBehaviour>)Delegate.CreateDelegate(typeof(Action<PhysicalBehaviour>),
                (MethodInfo)ReplicaGameplayAuthority.Required(typeof(PhysicalBehaviour), "SetParticleEmission"));
        private static bool reportedVisualError;

        private static bool Prefix(PhysicalBehaviour __instance)
        {
            if (!ReplicaGameplayAuthority.Suppress(__instance)) return true;
            try { updateParticles(__instance); }
            catch (Exception error)
            {
                if (!reportedVisualError)
                {
                    reportedVisualError = true;
                    Debug.LogWarning("[Connect][Sync] Replica particle update unavailable: " + error.GetType().Name + ": " + error.Message);
                }
            }
            return false;
        }
    }
}
