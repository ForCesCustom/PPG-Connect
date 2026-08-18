using UnityEngine;

namespace ConnectWorkshopCompanion
{
    [SkipSerialisation]
    public sealed class ConnectWorkshopInstallGuard : MonoBehaviour
    {
        // Maintained by the release publisher with every public Connect update.
        private const string PublishedReleaseUrl = "https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.26.zip";
        private const string BepInExGuideUrl = "https://docs.bepinex.dev/master/articles/user_guide/installation/unity_mono.html?tabs=tabid-win";
        private const string RuntimeMarkerName = "Connect.RuntimeMarker";

        private static ConnectWorkshopInstallGuard instance;
        private bool notified;
        private float nextCheckAt;

        private void Awake()
        {
            instance = this;
            nextCheckAt = Time.unscaledTime + 1.5f;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCheckAt) return;
            nextCheckAt = Time.unscaledTime + 2f;
            if (GameObject.Find(RuntimeMarkerName) != null || notified) return;
            notified = true;
            string message = "[Connect] Workshop Companion: external Connect runtime was not detected. Close the game and extract the complete Connect release ZIP into the folder containing People Playground.exe; it already includes BepInEx 5 x64 and the Connect plugin. Guide: " + BepInExGuideUrl;
            if (!string.IsNullOrEmpty(PublishedReleaseUrl)) message += " Connect release: " + PublishedReleaseUrl;
            ModAPI.Notify(message);
            Debug.Log(message);
        }

        internal static void Release()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }
    }
}
