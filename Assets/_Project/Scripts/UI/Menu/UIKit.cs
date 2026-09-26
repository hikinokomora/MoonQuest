using UnityEngine;
using UnityEngine.UI;

namespace Mehatron.UI.Menu
{
    /// <summary>Стиль меню: палитра, шрифт, процедурные спрайты (скруглённые плашки, свечение, градиенты) и фабрика UI-объектов.</summary>
    public static class UIKit
    {
        public static readonly Color Accent = new Color(0.36f, 0.86f, 1f);
        public static readonly Color Accent2 = new Color(1f, 0.6f, 0.16f);
        public static readonly Color Ink = new Color(0.92f, 0.96f, 1f);
        public static readonly Color InkDim = new Color(0.6f, 0.7f, 0.8f);
        public static readonly Color InkMuted = new Color(0.42f, 0.5f, 0.6f);
        public static readonly Color Panel = new Color(0.035f, 0.06f, 0.1f, 0.88f);
        public static readonly Color PanelLight = new Color(0.08f, 0.13f, 0.2f, 0.9f);
        public static readonly Color Line = new Color(0.36f, 0.86f, 1f, 0.22f);
        public static readonly Color Good = new Color(0.4f, 1f, 0.55f);
        public static readonly Color Bad = new Color(1f, 0.35f, 0.3f);

        static Font font, fontBold, fontLight;
        static Sprite rounded, roundedSmall, soft, outline, glow, gradH, gradV, circle;

        public static Font Font => font ??= LoadFont("Bahnschrift", "Segoe UI");
        public static Font FontBold => fontBold ??= LoadFont("Bahnschrift SemiBold", "Segoe UI Semibold");
        public static Font FontLight => fontLight ??= LoadFont("Bahnschrift Light", "Segoe UI Light");

        /// <summary>Системный шрифт Windows (Bahnschrift — с кириллицей), иначе встроенный.</summary>
        static Font LoadFont(params string[] names)
        {
            try
            {
                var f = Font.CreateDynamicFontFromOSFont(names, 48);
                if (f != null) return f;
            }
            catch { /* нет такого шрифта в системе */ }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public static Sprite Rounded => rounded ??= MakeRounded(64, 18, 0f);
        public static Sprite RoundedSmall => roundedSmall ??= MakeRounded(32, 8, 0f);
        /// <summary>Прямоугольник со скруглением 6 px — тумблеры, ползунки, индикаторы.
        /// Для тонких элементов радиус уменьшается через <c>Image.pixelsPerUnitMultiplier</c> (см. <see cref="Tight"/>).</summary>
        public static Sprite Soft => soft ??= MakeRounded(24, 6, 0f);

        /// <summary>Уменьшить радиус скругления sliced-картинки в k раз (для тонких полос, где 6 px дали бы овал).</summary>
        public static Image Tight(this Image img, float k = 2f)
        {
            img.pixelsPerUnitMultiplier = k;
            return img;
        }
        public static Sprite Outline => outline ??= MakeRounded(64, 18, 2.5f);
        public static Sprite Circle => circle ??= MakeRounded(64, 32, 0f);
        public static Sprite Glow => glow ??= MakeGlow(128);
        /// <summary>Слева непрозрачный, справа прозрачный.</summary>
        public static Sprite GradientH => gradH ??= MakeGradient(256, true);
        /// <summary>Снизу непрозрачный, сверху прозрачный.</summary>
        public static Sprite GradientV => gradV ??= MakeGradient(256, false);

        static Sprite MakeRounded(int size, float radius, float stroke)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Расстояние до скруглённого прямоугольника (SDF), сглаживание в 1 пиксель.
                    float cx = Mathf.Max(Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - radius), 0f);
                    float cy = Mathf.Max(Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - radius), 0f);
                    float d = Mathf.Sqrt(cx * cx + cy * cy) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (stroke > 0f) a *= Mathf.Clamp01(d + stroke + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            float b = radius + 1f;
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        static Sprite MakeGlow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * a));
                }
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeGradient(int size, bool horizontal)
        {
            var tex = new Texture2D(horizontal ? size : 4, horizontal ? 4 : size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < size; i++)
            {
                float a = 1f - i / (float)(size - 1);
                a = a * a * (3f - 2f * a);
                for (int j = 0; j < 4; j++)
                    if (horizontal) tex.SetPixel(i, j, new Color(1f, 1f, 1f, a));
                    else tex.SetPixel(j, i, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        // ---------------------------------------------------------------- фабрика

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Якоря и отступы: anchors (minX, minY, maxX, maxY), затем смещения краёв в пикселях.</summary>
        public static RectTransform Place(this RectTransform rt, float minX, float minY, float maxX, float maxY,
                                          float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Полоса фиксированной высоты от низа родителя: по X растянута между якорями minX..maxX с отступами.</summary>
        public static RectTransform Strip(this RectTransform rt, float minX, float maxX, float left, float right, float bottom, float height)
        {
            rt.anchorMin = new Vector2(minX, 0f);
            rt.anchorMax = new Vector2(maxX, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, bottom + height);
            return rt;
        }

        /// <summary>Фиксированный размер с якорем в точке (ax, ay) и pivot там же.</summary>
        public static RectTransform Fixed(this RectTransform rt, float ax, float ay, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(ax, ay);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image Img(string name, Transform parent, Sprite sprite, Color color, bool sliced = true)
        {
            var img = Node(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced && sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color,
                                 TextAnchor align = TextAnchor.MiddleLeft, Font f = null)
        {
            var t = Node(name, parent).gameObject.AddComponent<Text>();
            t.font = f ?? Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        /// <summary>Разрядка букв для заголовков (у legacy Text нет letter spacing).</summary>
        public static string Spaced(string s, int spaces = 1)
        {
            var pad = new string(' ', spaces);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(s[i]);
                if (i < s.Length - 1 && s[i] != '\n' && s[i + 1] != '\n') sb.Append(pad);
            }
            return sb.ToString();
        }

        public static float Smooth(float current, float target, float speed) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
    }
}
