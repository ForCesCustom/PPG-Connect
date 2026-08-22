using UnityEngine;

namespace ConnectWorkshopCompanion
{
    [SkipSerialisation]
    public sealed class ConnectWorkshopInstallGuard : MonoBehaviour
    {
        // Maintained by the release publisher with every public Connect update.
        private const string PublishedReleaseUrl = "https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.42.zip";
        private const string BepInExGuideUrl = "https://docs.bepinex.dev/master/articles/user_guide/installation/unity_mono.html?tabs=tabid-win";
        private const string RuntimeMarkerName = "Connect.RuntimeMarker";
        private const string ExpectedRuntimeMarkerName = "Connect.RuntimeVersion.0.1.42";

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
            bool runtimeFound = GameObject.Find(RuntimeMarkerName) != null;
            if (GameObject.Find(ExpectedRuntimeMarkerName) != null) return;
            bool updateRequired = runtimeFound;
            if (!notified)
            {
                notified = true;
                string message = updateRequired
                    ? "[Connect] Workshop Companion and Connect runtime versions do not match. Install the complete matching release: " + PublishedReleaseUrl
                    : "[Connect] Workshop Companion: full Connect runtime was not detected. Install the complete release: " + PublishedReleaseUrl + " | BepInEx guide: " + BepInExGuideUrl;
                ModAPI.Notify(message);
                Debug.Log(message);
            }

        }

        internal static void Release()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }
    }
}
