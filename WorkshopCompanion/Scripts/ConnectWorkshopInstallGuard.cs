using UnityEngine;
using UnityEngine.Events;

namespace ConnectWorkshopCompanion
{
    [SkipSerialisation]
    public sealed class ConnectWorkshopInstallGuard : MonoBehaviour
    {
        // Maintained by the release publisher with every public Connect update.
        private const string PublishedReleaseUrl = "https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.30.zip";
        private const string BepInExGuideUrl = "https://docs.bepinex.dev/master/articles/user_guide/installation/unity_mono.html?tabs=tabid-win";
        private const string RuntimeMarkerName = "Connect.RuntimeMarker";

        private static ConnectWorkshopInstallGuard instance;
        private bool notified;
        private bool recoveryDialogShown;
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
            if (GameObject.Find(RuntimeMarkerName) != null)
            {
                return;
            }
            if (!notified)
            {
                notified = true;
                string message = "[Connect] Workshop Companion: full Connect runtime was not detected. Install the complete release: " + PublishedReleaseUrl + " | BepInEx guide: " + BepInExGuideUrl;
                ModAPI.Notify(message);
                Debug.Log(message);
            }

            // The standard PPG script compiler does not reference UnityEngine.IMGUIModule,
            // so this uses the game's own native dialog rather than OnGUI/GUIStyle.
            if (recoveryDialogShown || DialogBoxManager.Main == null) return;
            recoveryDialogShown = true;
            try
            {
                DialogBoxManager.Dialog(
                    "CONNECT RUNTIME IS MISSING",
                    new[]
                    {
                        new DialogButton("OPEN CONNECT ON GITHUB", true, new UnityAction[] { OpenPublishedRelease }),
                        new DialogButton("CLOSE", true, new UnityAction[0])
                    });
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Connect] Could not show the install dialog: " + exception.Message);
            }
        }

        private static void OpenPublishedRelease()
        {
            Application.OpenURL(PublishedReleaseUrl);
        }

        internal static void Release()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }
    }
}
