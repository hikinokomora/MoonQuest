using System;
using Mehatron.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mehatron.UI.Menu
{
    /// <summary>
    /// Базовый пункт меню: подсветка при выборе, наведение мышью, клик.
    /// Навигация (вверх/вниз/влево/вправо/OK) приходит из <see cref="GameMenu"/>.
    /// </summary>
    public abstract class MenuItem : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        public GameMenu Menu;
        [TextArea] public string Description;
        public bool Selected;

        protected Image highlight, bar;
        protected Text label;
        protected float sel;

        public virtual bool Selectable => true;
        public virtual void Adjust(int dir) { }
        public virtual void Submit() { }
        /// <summary>Перечитать значение из настроек.</summary>
        public virtual void Refresh() { }

        protected virtual void Update()
        {
            sel = UIKit.Smooth(sel, Selected ? 1f : 0f, 16f);
            if (highlight != null) highlight.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, highlight.color.a * 0f + HighlightAlpha * sel + IdleAlpha);
            if (bar != null) bar.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(0f, 5f, sel), bar.rectTransform.sizeDelta.y);
            if (label != null) label.color = Color.Lerp(LabelIdle, UIKit.Ink, sel);
        }

        protected virtual float HighlightAlpha => 0.16f;
        protected virtual float IdleAlpha => 0f;
        protected virtual Color LabelIdle => UIKit.InkDim;

        public void OnPointerEnter(PointerEventData e)
        {
            if (Selectable && Menu != null) Menu.Hover(this);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (!Selectable || Menu == null) return;
            Menu.Hover(this);
            Clicked(e);
        }

        protected virtual void Clicked(PointerEventData e) => Submit();

        protected void Changed()
        {
            Settings.GameSettings.NotifyChanged();
            GameAudio.PlayUi(UiSound.Adjust);
        }

        /// <summary>Фон строки, подсветка, полоска-акцент слева и подпись.</summary>
        protected void BuildRow(string text, int fontSize, float height)
        {
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
            var le = gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            highlight = UIKit.Img("Highlight", transform, UIKit.RoundedSmall, new Color(1, 1, 1, 0));
            highlight.rectTransform.Place(0, 0, 1, 1);
            highlight.raycastTarget = true;
            bar = UIKit.Img("Bar", transform, null, UIKit.Accent);
            bar.rectTransform.anchorMin = new Vector2(0f, 0.18f);
            bar.rectTransform.anchorMax = new Vector2(0f, 0.82f);
            bar.rectTransform.pivot = new Vector2(0f, 0.5f);
            bar.rectTransform.anchoredPosition = Vector2.zero;
            bar.rectTransform.sizeDelta = new Vector2(0f, 0f);
            label = UIKit.Label("Label", transform, text, fontSize, UIKit.InkDim);
            label.rectTransform.Place(0, 0, 0.55f, 1, 26f);
        }

        protected static Text Arrow(Transform parent, string glyph, float x, Action onClick)
        {
            var t = UIKit.Label("Arrow", parent, glyph, 34, UIKit.Accent, TextAnchor.MiddleCenter, UIKit.FontBold);
            t.rectTransform.Fixed(x < 0 ? 0f : 1f, 0.5f, 0f, 0f, 44f, 48f);
            t.raycastTarget = true;
            t.gameObject.AddComponent<ClickRelay>().OnClick = onClick;
            return t;
        }
    }

    /// <summary>Клик по дочернему элементу (стрелки, вкладки) без отдельного Button.</summary>
    public class ClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public Action OnClick;
        public void OnPointerClick(PointerEventData e)
        {
            OnClick?.Invoke();
            e.Use();
        }
    }

    // ---------------------------------------------------------------- кнопка

    public class MenuButton : MenuItem
    {
        Action action;
        Text arrow, index;
        bool big;

        public static MenuButton Create(Transform parent, string text, Action action, bool big, string desc = "", string number = "")
        {
            var rt = UIKit.Node(text, parent);
            rt.sizeDelta = new Vector2(big ? 600f : 300f, big ? 84f : 62f);
            var b = rt.gameObject.AddComponent<MenuButton>();
            b.action = action;
            b.big = big;
            b.Description = desc;
            b.BuildRow(big ? UIKit.Spaced(text) : text, big ? 40 : 26, big ? 84f : 62f);
            b.label.font = UIKit.FontBold;
            if (big)
            {
                b.label.rectTransform.Place(0, 0, 1, 1, 86f);
                b.index = UIKit.Label("Index", rt, number, 20, UIKit.Accent, TextAnchor.MiddleLeft, UIKit.FontLight);
                b.index.rectTransform.Place(0, 0, 0, 1, 30f, 0, -70f);
            }
            else
            {
                b.label.alignment = TextAnchor.MiddleCenter;
                b.label.rectTransform.Place(0, 0, 1, 1);
                var frame = UIKit.Img("Frame", rt, UIKit.Outline, UIKit.Line);
                frame.rectTransform.Place(0, 0, 1, 1);
            }
            b.arrow = UIKit.Label("Arrow", rt, "›", big ? 52 : 34, UIKit.Accent, TextAnchor.MiddleCenter, UIKit.FontBold);
            b.arrow.rectTransform.Fixed(1f, 0.5f, -36f, 2f, 40f, 60f);
            return b;
        }

        protected override float HighlightAlpha => big ? 0.2f : 0.22f;
        protected override Color LabelIdle => big ? new Color(0.78f, 0.85f, 0.92f) : UIKit.InkDim;

        protected override void Update()
        {
            base.Update();
            if (arrow != null)
            {
                arrow.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, sel);
                arrow.rectTransform.anchoredPosition = new Vector2(-36f - 12f * (1f - sel), 2f);
            }
            if (big && label != null) label.rectTransform.offsetMin = new Vector2(86f + 14f * sel, 0f);
        }

        public override void Submit()
        {
            GameAudio.PlayUi(UiSound.Click);
            action?.Invoke();
        }
    }

    // ---------------------------------------------------------------- список значений

    public class MenuOption : MenuItem
    {
        string[] options;
        Func<int> get;
        Action<int> set;
        Text value;
        Image[] dots;
        bool wrap;

        public static MenuOption Create(Transform parent, string text, string[] options, Func<int> get, Action<int> set, string desc, bool wrap = false)
        {
            var rt = UIKit.Node(text, parent);
            var o = rt.gameObject.AddComponent<MenuOption>();
            o.options = options;
            o.get = get;
            o.set = set;
            o.wrap = wrap;
            o.Description = desc;
            o.BuildRow(text, 26, 60f);
            var area = UIKit.Node("Value", rt).Place(1, 0, 1, 1);
            area.offsetMin = new Vector2(-400f, 0f);
            area.offsetMax = new Vector2(-16f, 0f);
            Arrow(area, "‹", -1, () => o.Step(-1));
            Arrow(area, "›", 1, () => o.Step(1));
            o.value = UIKit.Label("Text", area, "", 26, UIKit.Ink, TextAnchor.MiddleCenter, UIKit.FontBold);
            o.value.rectTransform.Place(0, 0, 1, 1, 44f, 6f, 44f);
            int n = Mathf.Min(options.Length, 12);
            o.dots = new Image[n];
            for (int i = 0; i < n; i++)
            {
                var d = UIKit.Img("Dot", area, UIKit.Soft, UIKit.InkMuted).Tight(4f);
                d.rectTransform.Fixed(0.5f, 0f, (i - (n - 1) / 2f) * 16f, 8f, 12f, 4f);
                o.dots[i] = d;
            }
            o.Refresh();
            return o;
        }

        public override void Refresh()
        {
            int i = Mathf.Clamp(get(), 0, options.Length - 1);
            value.text = options[i];
            for (int k = 0; k < dots.Length; k++) dots[k].color = k == i ? UIKit.Accent : UIKit.InkMuted;
        }

        void Step(int dir)
        {
            Menu?.Hover(this);
            Adjust(dir);
        }

        public override void Adjust(int dir)
        {
            int i = get() + dir;
            if (wrap) i = (i + options.Length) % options.Length;
            i = Mathf.Clamp(i, 0, options.Length - 1);
            if (i == get()) return;
            set(i);
            Refresh();
            Changed();
        }

        public override void Submit() => Adjust(1);
        protected override void Clicked(PointerEventData e) { }
    }

    // ---------------------------------------------------------------- переключатель

    public class MenuToggle : MenuItem
    {
        Func<bool> get;
        Action<bool> set;
        Image pill, knob;
        Text state;
        float k;

        public static MenuToggle Create(Transform parent, string text, Func<bool> get, Action<bool> set, string desc)
        {
            var rt = UIKit.Node(text, parent);
            var t = rt.gameObject.AddComponent<MenuToggle>();
            t.get = get;
            t.set = set;
            t.Description = desc;
            t.BuildRow(text, 26, 60f);
            t.pill = UIKit.Img("Pill", rt, UIKit.Soft, UIKit.InkMuted);
            t.pill.rectTransform.Fixed(1f, 0.5f, -60f, 0f, 76f, 34f);
            t.knob = UIKit.Img("Knob", t.pill.transform, UIKit.Soft, Color.white).Tight(1.4f);
            t.knob.rectTransform.Fixed(0f, 0.5f, 4f, 0f, 30f, 26f);
            t.state = UIKit.Label("State", rt, "", 22, UIKit.InkDim, TextAnchor.MiddleRight, UIKit.FontBold);
            t.state.rectTransform.Fixed(1f, 0.5f, -150f, 0f, 120f, 40f);
            t.k = get() ? 1f : 0f;
            t.Refresh();
            return t;
        }

        public override void Refresh() => state.text = get() ? "ВКЛ" : "ВЫКЛ";

        protected override void Update()
        {
            if (get == null) return;
            base.Update();
            bool on = get();
            k = UIKit.Smooth(k, on ? 1f : 0f, 18f);
            pill.color = Color.Lerp(new Color(0.25f, 0.3f, 0.38f), UIKit.Accent, k);
            knob.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(4f, 42f, k), 0f);
            state.color = on ? UIKit.Accent : UIKit.InkMuted;
        }

        public override void Adjust(int dir) => Toggle();
        public override void Submit() => Toggle();

        void Toggle()
        {
            set(!get());
            Refresh();
            Changed();
        }
    }

    // ---------------------------------------------------------------- ползунок

    public class MenuSlider : MenuItem, IPointerDownHandler, IDragHandler
    {
        float min, max, step;
        Func<float> get;
        Action<float> set;
        Func<float, string> format;
        RectTransform track;
        Image fill, knob;
        Text value;

        public static MenuSlider Create(Transform parent, string text, float min, float max, float step,
                                        Func<float> get, Action<float> set, Func<float, string> format, string desc)
        {
            var rt = UIKit.Node(text, parent);
            var s = rt.gameObject.AddComponent<MenuSlider>();
            s.min = min; s.max = max; s.step = step;
            s.get = get; s.set = set;
            s.format = format ?? (v => v.ToString("0.00"));
            s.Description = desc;
            s.BuildRow(text, 26, 60f);
            var bg = UIKit.Img("Track", rt, UIKit.Soft, new Color(1f, 1f, 1f, 0.12f)).Tight();
            s.track = bg.rectTransform.Fixed(1f, 0.5f, -120f, 0f, 260f, 12f);
            s.fill = UIKit.Img("Fill", s.track, UIKit.Soft, UIKit.Accent).Tight();
            s.fill.rectTransform.Place(0, 0, 0, 1);
            s.knob = UIKit.Img("Knob", s.track, UIKit.Soft, Color.white).Tight();
            s.knob.rectTransform.Fixed(0f, 0.5f, 0f, 0f, 12f, 28f);
            s.value = UIKit.Label("Value", rt, "", 24, UIKit.Ink, TextAnchor.MiddleRight, UIKit.FontBold);
            s.value.rectTransform.Fixed(1f, 0.5f, -16f, 0f, 96f, 40f);
            s.Refresh();
            return s;
        }

        public override void Refresh()
        {
            float t = Mathf.InverseLerp(min, max, get());
            fill.rectTransform.anchorMax = new Vector2(t, 1f);
            fill.rectTransform.offsetMax = Vector2.zero;
            knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(t, 0.5f);
            knob.rectTransform.anchoredPosition = Vector2.zero;
            knob.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            value.text = format(get());
        }

        public override void Adjust(int dir) => SetValue(get() + dir * step);

        void SetValue(float v)
        {
            v = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
            if (Mathf.Approximately(v, get())) return;
            set(v);
            Refresh();
            Changed();
        }

        public void OnPointerDown(PointerEventData e) { Menu?.Hover(this); Drag(e); }
        public void OnDrag(PointerEventData e) => Drag(e);
        protected override void Clicked(PointerEventData e) { }

        void Drag(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(track, e.position, e.pressEventCamera, out var local)) return;
            // Мимо дорожки (по подписи строки) значение не меняем.
            if (local.y < -40f || local.y > 40f || local.x < track.rect.xMin - 20f) return;
            float t = Mathf.InverseLerp(track.rect.xMin, track.rect.xMax, local.x);
            SetValue(Mathf.Lerp(min, max, t));
        }
    }

    // ---------------------------------------------------------------- действие в строке настроек

    public class MenuAction : MenuItem
    {
        Action action;

        public static MenuAction Create(Transform parent, string text, string button, Action action, string desc)
        {
            var rt = UIKit.Node(text, parent);
            var a = rt.gameObject.AddComponent<MenuAction>();
            a.action = action;
            a.Description = desc;
            a.BuildRow(text, 26, 60f);
            var plate = UIKit.Img("Plate", rt, UIKit.Outline, UIKit.Accent);
            plate.rectTransform.Fixed(1f, 0.5f, -16f, 0f, 220f, 42f);
            var t = UIKit.Label("Button", plate.transform, button, 22, UIKit.Accent, TextAnchor.MiddleCenter, UIKit.FontBold);
            t.rectTransform.Place(0, 0, 1, 1);
            return a;
        }

        public override void Submit()
        {
            GameAudio.PlayUi(UiSound.Click);
            action?.Invoke();
        }
    }

    // ---------------------------------------------------------------- заголовок раздела

    public class MenuHeader : MenuItem
    {
        public override bool Selectable => false;

        public static MenuHeader Create(Transform parent, string text)
        {
            var rt = UIKit.Node(text, parent);
            var h = rt.gameObject.AddComponent<MenuHeader>();
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = 46f;
            var t = UIKit.Label("Text", rt, UIKit.Spaced(text.ToUpperInvariant()), 18, UIKit.Accent, TextAnchor.LowerLeft, UIKit.FontBold);
            t.rectTransform.Place(0, 0, 1, 1, 26f, 8f);
            var line = UIKit.Img("Line", rt, null, UIKit.Line);
            line.rectTransform.Place(0, 0, 1, 0, 26f, 0f, 16f);
            line.rectTransform.sizeDelta = new Vector2(line.rectTransform.sizeDelta.x, 1f);
            return h;
        }

        protected override void Update() { }
    }

    // ---------------------------------------------------------------- вкладки

    public class MenuTabs : MenuItem
    {
        string[] tabs;
        Text[] labels;
        Image underline;
        Func<int> get;
        Action<int> set;
        float ux, uw;

        public static MenuTabs Create(Transform parent, string[] tabs, Func<int> get, Action<int> set)
        {
            var rt = UIKit.Node("Tabs", parent);
            var m = rt.gameObject.AddComponent<MenuTabs>();
            m.tabs = tabs;
            m.get = get;
            m.set = set;
            m.Description = "Разделы настроек. ◀ ▶ (или Q / E, бамперы геймпада) — переключение раздела.";
            m.highlight = UIKit.Img("Highlight", rt, UIKit.RoundedSmall, new Color(1, 1, 1, 0));
            m.highlight.rectTransform.Place(0, 0, 1, 1);
            m.highlight.raycastTarget = true;
            m.labels = new Text[tabs.Length];
            float w = 1f / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                var t = UIKit.Label("Tab" + i, rt, tabs[i], 20, UIKit.InkDim, TextAnchor.MiddleCenter, UIKit.FontBold);
                t.rectTransform.Place(i * w, 0, (i + 1) * w, 1);
                t.raycastTarget = true;
                t.gameObject.AddComponent<ClickRelay>().OnClick = () => { m.Menu?.Hover(m); m.SetTab(idx); };
                m.labels[i] = t;
            }
            m.underline = UIKit.Img("Underline", rt, UIKit.Soft, UIKit.Accent).Tight(4f);
            m.underline.rectTransform.anchorMin = m.underline.rectTransform.anchorMax = new Vector2(0f, 0f);
            m.underline.rectTransform.pivot = new Vector2(0.5f, 0f);
            m.ux = -1f;
            return m;
        }

        protected override float HighlightAlpha => 0.1f;

        protected override void Update()
        {
            if (get == null) return;
            base.Update();
            int cur = get();
            float width = ((RectTransform)transform).rect.width;
            float w = width / tabs.Length;
            float tx = (cur + 0.5f) * w, tw = w * 0.6f;
            if (ux < 0f) { ux = tx; uw = tw; }
            ux = UIKit.Smooth(ux, tx, 14f);
            uw = UIKit.Smooth(uw, tw, 14f);
            underline.rectTransform.anchoredPosition = new Vector2(ux, 2f);
            underline.rectTransform.sizeDelta = new Vector2(uw, 4f);
            for (int i = 0; i < labels.Length; i++) labels[i].color = i == cur ? UIKit.Ink : UIKit.InkMuted;
        }

        public override void Adjust(int dir) => SetTab((get() + dir + tabs.Length) % tabs.Length);

        void SetTab(int i)
        {
            if (i == get()) return;
            GameAudio.PlayUi(UiSound.Adjust);
            set(i);
        }

        protected override void Clicked(PointerEventData e) { }
    }
}
