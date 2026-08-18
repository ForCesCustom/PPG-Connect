using UnityEngine;

namespace ConnectWorkshopCompanion
{
    public sealed class Mod
    {
        private static bool registered;

        public static void OnLoad()
        {
            Debug.Log("[Connect][Workshop] Workshop Companion loaded.");
        }

        public static void Main()
        {
            if (registered) return;
            registered = true;
            ModAPI.Register<ConnectWorkshopInstallGuard>();
        }

        public static void OnUnload()
        {
            ConnectWorkshopInstallGuard.Release();
            registered = false;
        }
    }
}
