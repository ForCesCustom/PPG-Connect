using UnityEngine;

namespace PPGTogether.BepInEx
{
    internal sealed class RoundedUiTheme
    {
        internal readonly GUIStyle Title;
        internal readonly GUIStyle Subtitle;
        internal readonly GUIStyle Label;
        internal readonly GUIStyle Small;
        internal readonly GUIStyle ButtonText;
        internal readonly GUIStyle ButtonSmall;
        internal readonly GUIStyle Input;
        internal readonly GUIStyle Center;

        private readonly Texture2D roundedLarge;
        private readonly Texture2D roundedMedium;
        private readonly Texture2D roundedSmall;
        private readonly Texture2D cursorRing;
        private readonly Texture2D cursorDot;
        private readonly GUIStyle panelShape;
        private readonly GUIStyle cardShape;
        private readonly GUIStyle pillShape;

        internal RoundedUiTheme()
        {
            roundedLarge = CreateRoundedTexture(64, 20);
            roundedMedium = CreateRoundedTexture(48, 14);
            roundedSmall = CreateRoundedTexture(32, 9);
            cursorRing = CreateCursorRingTexture(40, 14f, 8f);
            cursorDot = CreateCursorRingTexture(16, 8f, 0f);
            panelShape = Shape(roundedLarge, 20);
            cardShape = Shape(roundedMedium, 14);
            pillShape = Shape(roundedSmall, 9);

            Title = Text(24, FontStyle.Bold, new Color(0.76f, 0.97f, 1f, 1f), TextAnchor.MiddleLeft);
            Subtitle = Text(11, FontStyle.Bold, new Color(0.40f, 0.75f, 0.84f, 1f), TextAnchor.MiddleLeft);
            Label = Text(13, FontStyle.Normal, new Color(0.90f, 0.95f, 0.98f, 1f), TextAnchor.MiddleLeft);
            Small = Text(11, FontStyle.Normal, new Color(0.58f, 0.67f, 0.72f, 1f), TextAnchor.MiddleLeft);
            ButtonText = Text(13, FontStyle.Bold, new Color(0.05f, 0.10f, 0.13f, 1f), TextAnchor.MiddleCenter);
            ButtonSmall = Text(11, FontStyle.Bold, new Color(0.83f, 0.92f, 0.95f, 1f), TextAnchor.MiddleCenter);
            Input = Text(12, FontStyle.Normal, new Color(0.92f, 0.96f, 0.98f, 1f), TextAnchor.MiddleLeft);
            Input.padding = new RectOffset(10, 10, 7, 7);
            Input.normal.background = roundedSmall;
            Input.focused.background = roundedSmall;
            Input.hover.background = roundedSmall;
            Input.active.background = roundedSmall;
            Center = Text(12, FontStyle.Bold, new Color(0.91f, 0.97f, 1f, 1f), TextAnchor.MiddleCenter);
        }

        internal void Panel(Rect rect, Color color) { Draw(rect, color, panelShape); }
        internal void Card(Rect rect, Color color) { Draw(rect, color, cardShape); }
        internal void Pill(Rect rect, Color color) { Draw(rect, color, pillShape); }

        internal void CursorRing(Rect rect, Color color) { DrawTexture(rect, color, cursorRing); }
        internal void CursorDot(Rect rect, Color color) { DrawTexture(rect, color, cursorDot); }

        private static GUIStyle Text(int size, FontStyle font, Color color, TextAnchor anchor)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = size;
            style.fontStyle = font;
            style.alignment = anchor;
            style.wordWrap = false;
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            return style;
        }

        private static GUIStyle Shape(Texture2D texture, int border)
        {
            GUIStyle style = new GUIStyle();
            style.normal.background = texture;
            style.border = new RectOffset(border, border, border, border);
            return style;
        }

        private static void Draw(Rect rect, Color color, GUIStyle style)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Box(rect, GUIContent.none, style);
            GUI.color = previous;
        }

        private static void DrawTexture(Rect rect, Color color, Texture2D texture)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        private static Texture2D CreateRoundedTexture(int size, int radius)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Connect_RoundedUI";
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color clear = new Color(1f, 1f, 1f, 0f);
            Color solid = new Color(1f, 1f, 1f, 1f);
            float feather = 1.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x, x - (size - radius - 1), 0f);
                    float dy = Mathf.Max(radius - y, y - (size - radius - 1), 0f);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01((radius + feather - distance) / feather);
                    texture.SetPixel(x, y, Color.Lerp(clear, solid, alpha));
                }
            }
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateCursorRingTexture(int size, float outerRadius, float innerRadius)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Connect_CursorMarker";
            texture.hideFlags = HideFlags.HideAndDontSave;
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float outer = Mathf.Clamp01((outerRadius + 1.2f - distance) / 1.2f);
                    float inner = innerRadius <= 0f ? 1f : Mathf.Clamp01((distance - innerRadius + 1.2f) / 1.2f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, outer * inner));
                }
            }
            texture.Apply(false, true);
            return texture;
        }

        internal void Dispose()
        {
            if (roundedLarge != null) Object.Destroy(roundedLarge);
            if (roundedMedium != null) Object.Destroy(roundedMedium);
            if (roundedSmall != null) Object.Destroy(roundedSmall);
            if (cursorRing != null) Object.Destroy(cursorRing);
            if (cursorDot != null) Object.Destroy(cursorDot);
        }
    }
}
