using HarmonyLib;

namespace PPGTogether.BepInEx
{
    [HarmonyPatch(typeof(ToolControllerBehaviour), "HandleTools")]
    internal static class ClientWorldInputPatch
    {
        private static bool Prefix()
        {
            PPGTogetherPlugin plugin = PPGTogetherPlugin.Instance;
            if (plugin != null) plugin.HandleClientBlockedToolInput();
            return plugin == null || !plugin.ShouldBlockVanillaWorldInput;
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
            if (plugin == null || !plugin.ShouldRouteVanillaCatalogSpawn) return true;
            plugin.RequestCatalogSpawn(e, flipped);
            return false;
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
}
