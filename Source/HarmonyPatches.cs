using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PPGTogether.BepInEx
{
    [HarmonyPatch(typeof(CameraControlBehaviour), "ZoomCamera")]
    internal static class ConnectMenuZoomPatch
    {
        private static bool Prefix() { var p = PPGTogetherPlugin.Instance; return p == null || !p.ConnectMenuContainsCursor; }
    }

    [HarmonyPatch(typeof(ToolControllerBehaviour), "HandleTools")]
    internal static class ClientWorldInputPatch
    {
        private static bool Prefix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            if (plugin != null) plugin.HandleClientBlockedToolInput();
            return plugin == null || (!plugin.ConnectMenuBlocksTools && !plugin.ShouldBlockVanillaWorldInput);
        }
    }

    // The base-game Tab catalog remains each player's own UI.  A non-host
    // client never creates its own local authoritative object; it asks the
    // host to resolve the selected spawnable instead.
    [HarmonyPatch(typeof(CatalogBehaviour), "Spawn", new[] { typeof(SpawnableAsset), typeof(bool) })]
    internal static class ClientCatalogSpawnPatch
    {
        private static bool Prefix(SpawnableAsset e, bool flipped)
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            if (plugin == null) return true;
            if (plugin.ShouldRouteVanillaCatalogSpawn)
            {
                plugin.RequestCatalogSpawn(e, flipped);
                return false;
            }
            // The item-spawn callback in current PPG can expose a temporary
            // local ordering key after CatalogBehaviour.Spawn has begun. Keep
            // the key captured at the public catalog boundary for the host's
            // subsequent authoritative broadcast.
            plugin.BeginHostCatalogSpawn(e);
            return true;
        }

        private static void Postfix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            if (plugin != null) plugin.EndHostCatalogSpawn();
        }
    }

    // HandleTools is intentionally suppressed on clients to stop local physics
    // authority.  Keep right-click useful by selecting only the hovered object
    // for the local context menu; the menu actions themselves are routed below.
    [HarmonyPatch(typeof(ToolControllerBehaviour), "HandleContextMenu")]
    internal static class ClientContextMenuSelectionPatch
    {
        private static void Prefix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            if (plugin != null) plugin.PrepareClientContextMenu();
        }
    }

    [HarmonyPatch(typeof(ContextMenuBehaviour), "ActivateAction")]
    internal static class ClientContextActivatePatch
    {
        private static bool Prefix(ContextMenuBehaviour __instance)
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            if (plugin == null || !plugin.ShouldBlockVanillaWorldInput) return true;
            plugin.RequestClientContextActivate();
            if (__instance != null) __instance.Hide();
            return false;
        }
    }

    [HarmonyPatch(typeof(ContextMenuBehaviour), "DeleteAction")]
    internal static class ClientContextDeletePatch
    {
        private static bool Prefix(ContextMenuBehaviour __instance)
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            if (plugin == null || !plugin.ShouldBlockVanillaWorldInput) return true;
            plugin.RequestClientContextDelete();
            if (__instance != null) __instance.Hide();
            return false;
        }
    }

    // Activate and Delete are explicitly routed to host validation above. The
    // rest of the native Context menu changes only the local scene (Paste can
    // even instantiate new GameObjects), so a connected guest must not execute
    // those actions until matching protocol support exists.
    [HarmonyPatch]
    internal static class ClientUnsupportedContextActionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo[] methods = typeof(ContextMenuBehaviour).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.GetParameters().Length != 0 || !method.Name.EndsWith("Action", StringComparison.Ordinal) ||
                    method.Name == "ActivateAction" || method.Name == "DeleteAction")
                    continue;
                yield return method;
            }
        }

        private static bool Prefix(MethodBase __originalMethod)
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            if (plugin == null || !plugin.ShouldBlockVanillaWorldInput) return true;
            string name = __originalMethod == null ? "Context action" : __originalMethod.Name;
            if (name == "FollowAction" || name == "CopyAction" || name == "SaveAction") return true;
            if (!plugin.RouteSharedContextAction(name)) plugin.NotifyUnsupportedClientContextAction(name);
            return false;
        }
    }

    [HarmonyPatch(typeof(ClearButtonBehaviour), "ClearEverything")]
    internal static class ClientClearEverythingPatch
    {
        private static bool Prefix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            return plugin == null || plugin.RouteWorldCommand(0);
        }
    }

    [HarmonyPatch(typeof(ClearLivingBehaviour), "Clear")]
    internal static class ClientClearLivingPatch
    {
        private static bool Prefix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            return plugin == null || plugin.RouteWorldCommand(1);
        }
    }

    [HarmonyPatch(typeof(ClearDebrisBehaviour), "Clear")]
    internal static class ClientClearDebrisPatch
    {
        private static bool Prefix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            return plugin == null || plugin.RouteWorldCommand(2);
        }
    }

    [HarmonyPatch(typeof(Global), "TogglePaused")]
    internal static class ClientPausePatch
    {
        private static bool Prefix() { var p = PPGTogetherPlugin.Instance; return p == null || p.RouteWorldCommand(3); }
    }

    [HarmonyPatch(typeof(Global), "ToggleSlowmotion")]
    internal static class ClientSlowmotionPatch
    {
        private static bool Prefix() { var p = PPGTogetherPlugin.Instance; return p == null || p.RouteWorldCommand(4); }
    }

    [HarmonyPatch(typeof(UndoControllerBehaviour), "Undo")]
    internal static class ClientUndoPatch
    {
        private static bool Prefix() { var p = PPGTogetherPlugin.Instance; return p == null || p.RouteWorldCommand(5); }
    }

    [HarmonyPatch(typeof(EnvironmentSettingsController), "SetValue")]
    internal static class ClientEnvironmentPatch
    {
        private static bool Prefix(FieldInfo member, object value)
        { var p = PPGTogetherPlugin.Instance; return p == null || p.RouteEnvironmentChange(member, value); }
    }

    [HarmonyPatch(typeof(ContextMenuBehaviour), "CreateDynamicButtons")]
    internal static class ClientDynamicActionsPatch
    {
        // Mod-supplied arbitrary closures are not a safe network protocol.
        private static bool Prefix() { var p = PPGTogetherPlugin.Instance; return p == null || !p.ShouldBlockVanillaWorldInput; }
    }

    [HarmonyPatch(typeof(ToolControllerBehaviour), "HandleIndirectInteraction")]
    internal static class ClientDirectActivationPatch
    {
        private static bool Prefix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            return plugin == null || !plugin.HandleClientDirectActivation();
        }
    }

    // People Playground already owns map instantiation.  Observing the public
    // completion point lets the host relay only the selected map identity and
    // lets every client load its own installed copy through the same loader.
    [HarmonyPatch(typeof(MapLoaderBehaviour), "Load")]
    internal static class ConnectMapLoadPatch
    {
        private static void Postfix(MapLoaderBehaviour __instance)
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            if (plugin != null) plugin.OnLocalMapLoaded(__instance);
        }
    }

    // A normal map tile first stores CurrentMap and then starts a scene
    // transition.  Block that tile for guests before it can alter their local
    // selection; the host is the only player who may choose the shared map.
    [HarmonyPatch(typeof(MapViewBehaviour), "Select")]
    internal static class ConnectClientMapViewPatch
    {
        private static bool Prefix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            return plugin == null || plugin.AllowClientMapViewSelection();
        }
    }

    // Also cover the Enter/Play button's direct scene switch. Connect marks
    // its own host-authorised switch for the duration of the exact call.
    [HarmonyPatch(typeof(SceneSwitchBehaviour), "Switch")]
    internal static class ConnectClientSceneSwitchPatch
    {
        private static bool Prefix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            return plugin == null || plugin.AllowClientSceneSwitch();
        }
    }
}
