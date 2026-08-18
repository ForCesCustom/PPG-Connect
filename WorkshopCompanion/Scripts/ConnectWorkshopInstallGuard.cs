using UnityEngine;

namespace ConnectWorkshopCompanion
{
    [SkipSerialisation]
    public sealed class ConnectWorkshopInstallGuard : MonoBehaviour
    {
        // Maintained by the release publisher with every public Connect update.
        private const string PublishedReleaseUrl = "https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.28.zip";
        private const string BepInExGuideUrl = "https://docs.bepinex.dev/master/articles/user_guide/installation/unity_mono.html?tabs=tabid-win";
        private const string RuntimeMarkerName = "Connect.RuntimeMarker";

        private static ConnectWorkshopInstallGuard instance;
        private bool notified;
        private bool recoveryVisible;
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
                recoveryVisible = false;
                return;
            }
            recoveryVisible = true;
            if (notified) return;
            notified = true;
            string message = "[Connect] Workshop Companion: external Connect runtime was not detected. Close the game and extract the complete Connect release ZIP into the folder containing People Playground.exe; it already includes BepInEx 5 x64 and the Connect plugin. Guide: " + BepInExGuideUrl;
            if (!string.IsNullOrEmpty(PublishedReleaseUrl)) message += " Connect release: " + PublishedReleaseUrl;
            ModAPI.Notify(message);
            Debug.Log(message);
        }

        private void OnGUI()
        {
            if (!recoveryVisible) return;

            float width = Mathf.Min(570f, Screen.width - 36f);
            float height = 238f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, Mathf.Max(30f, Screen.height * 0.28f), width, height);
            GUI.Box(panel, GUIContent.none);

            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.fontSize = 19;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = new Color(1f, 0.42f, 0.62f, 1f);

            GUIStyle body = new GUIStyle(GUI.skin.label);
            body.fontSize = 13;
            body.wordWrap = true;
            body.normal.textColor = new Color(0.91f, 0.95f, 0.98f, 1f);

            GUI.Label(new Rect(panel.x + 22f, panel.y + 18f, panel.width - 44f, 28f), "CONNECT RUNTIME IS MISSING", title);
            GUI.Label(new Rect(panel.x + 22f, panel.y + 55f, panel.width - 44f, 79f),
                "You installed the Workshop Companion, but the full Connect runtime is not installed. " +
                "Close People Playground, download the complete Connect ZIP, and extract it into the folder containing People Playground.exe.", body);
            GUI.Label(new Rect(panel.x + 22f, panel.y + 140f, panel.width - 44f, 22f),
                "Required: BepInEx\\plugins\\Connect\\Connect.BepInEx.dll", body);

            if (GUI.Button(new Rect(panel.x + 22f, panel.y + height - 48f, 248f, 30f), "OPEN CONNECT ON GITHUB"))
                Application.OpenURL(PublishedReleaseUrl);
            if (GUI.Button(new Rect(panel.x + 278f, panel.y + height - 48f, 118f, 30f), "COPY LINK"))
                GUIUtility.systemCopyBuffer = PublishedReleaseUrl;
            if (GUI.Button(new Rect(panel.x + panel.width - 126f, panel.y + height - 48f, 104f, 30f), "CLOSE"))
                recoveryVisible = false;
        }

        internal static void Release()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }
    }
}
