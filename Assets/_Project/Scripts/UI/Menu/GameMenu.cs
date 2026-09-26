using System;
using System.Collections.Generic;
using Mehatron.Audio;
using Mehatron.Gameplay;
using Mehatron.Haptics;
using Mehatron.Motion;
using Mehatron.Settings;
using Mehatron.Vehicle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Mehatron.UI.Menu
{
    /// <summary>
    /// Игровое меню: главное (Играть / Настройки / Выход), настройки из 7 разделов, пауза, итоги миссии, подтверждение выхода.
    /// Без шлема — экранный оверлей поверх кинематографичной камеры над базой; в VR — панель в пространстве перед кабиной.
    /// Управление: мышь, клавиатура, геймпад, руль/педали стенда (см. <see cref="MenuInput"/>).
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        [SerializeField] GameFlow flow;
        [SerializeField] RoverController rover;
        [SerializeField] VRSeatRig seatRig;
        [SerializeField] Camera cockpitCamera;
        [SerializeField] MenuCamera menuCamera;
        [SerializeField] SettingsApplier applier;
        [Tooltip("Расстояние до панели меню в VR, м.")]
        [SerializeField] float vrDistance = 2.4f;
        [SerializeField] float vrScale = 0.0015f;

        public static GameMenu Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.current != Page.None;
        /// <summary>Отладка без шлема: показать меню так, как оно висит в VR (панель перед кабиной).</summary>
        public static bool DebugVrLayout;

        enum Page { None, Main, Settings, Pause, Result, ConfirmExit }

        class View
        {
            public RectTransform rt;
            public CanvasGroup group;
            public readonly List<MenuItem> items = new List<MenuItem>();
            public int sel;
            public float shown;
        }

        static readonly string[] Tabs = { "ГРАФИКА", "ИЗОБРАЖЕНИЕ", "ЭКРАН", "ЗВУК", "УПРАВЛЕНИЕ", "ПЛАТФОРМА", "ИГРА" };

        Canvas canvas;
        CanvasScaler scaler;
        RectTransform root;
        Image backdrop, sideShade, bottomShade, vrPlate, cutFade;
        readonly Dictionary<Page, View> views = new Dictionary<Page, View>();
        Page current = Page.None, settingsReturn = Page.Main;
        MenuInput input;
        bool xr, modeSet;

        // Настройки
        int tab;
        RectTransform settingsList;
        ScrollRect scroll;
        Text descTitle, descText;
        MenuTabs tabsItem;
        readonly List<MenuItem> footer = new List<MenuItem>();

        // Главная
        Text statusText, resultTitle, resultStats, pauseInfo;
        float statusTimer;
        Image titleLine, pulseDot;

        static GameSettings S => GameSettings.Current;

        void Awake()
        {
            Instance = this;
            if (flow != null) flow.MenuDriven = true;
            input = new MenuInput(rover != null ? rover.Input : null);
            EnsureEventSystem();
            BuildCanvas();
            BuildMain();
            BuildSettings();
            BuildPause();
            BuildResult();
            BuildConfirm();
            foreach (var v in views.Values) { v.group.alpha = 0f; v.rt.gameObject.SetActive(false); }
            GameEvents.StateChanged += OnState;
            GameSettings.Changed += RefreshAll;
        }

        void OnDestroy()
        {
            GameEvents.StateChanged -= OnState;
            GameSettings.Changed -= RefreshAll;
            input?.Dispose();
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (menuCamera != null && applier != null) applier.AddCamera(menuCamera.Camera);
            OnState(flow != null ? flow.State : GameState.Menu);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ================================================================ построение

        void BuildCanvas()
        {
            var go = new GameObject("GameMenuCanvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.sortingOrder = 50;
            scaler = go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            root = (RectTransform)go.transform;

            vrPlate = UIKit.Img("VrPlate", root, UIKit.Rounded, new Color(0.02f, 0.04f, 0.07f, 0.82f));
            vrPlate.rectTransform.Place(0, 0, 1, 1, -30f, -30f, -30f, -30f);
            backdrop = UIKit.Img("Backdrop", root, null, new Color(0.01f, 0.02f, 0.04f, 0.6f));
            backdrop.rectTransform.Place(0, 0, 1, 1, -4000f, -4000f, -4000f, -4000f);
            sideShade = UIKit.Img("SideShade", root, UIKit.GradientH, new Color(0.01f, 0.03f, 0.06f, 0.92f), false);
            sideShade.rectTransform.Place(0, 0, 0.62f, 1);
            bottomShade = UIKit.Img("BottomShade", root, UIKit.GradientV, new Color(0f, 0.01f, 0.03f, 0.75f), false);
            bottomShade.rectTransform.Place(0, 0, 1, 0.25f);
            cutFade = UIKit.Img("CutFade", root, null, new Color(0f, 0f, 0f, 0f));
            cutFade.rectTransform.Place(0, 0, 1, 1, -4000f, -4000f, -4000f, -4000f);
        }

        View NewView(Page p, string name)
        {
            var rt = UIKit.Node(name, root).Place(0, 0, 1, 1);
            var v = new View { rt = rt, group = rt.gameObject.AddComponent<CanvasGroup>() };
            views[p] = v;
            return v;
        }

        T Add<T>(View v, T item) where T : MenuItem
        {
            item.Menu = this;
            v.items.Add(item);
            return item;
        }

        RectTransform Panel(Transform parent, float w, float h, float y = 0f)
        {
            var plate = UIKit.Img("Panel", parent, UIKit.Rounded, UIKit.Panel);
            plate.rectTransform.Fixed(0.5f, 0.5f, 0f, y, w, h);
            var frame = UIKit.Img("Frame", plate.transform, UIKit.Outline, UIKit.Line);
            frame.rectTransform.Place(0, 0, 1, 1);
            var glow = UIKit.Img("TopGlow", plate.transform, UIKit.Glow, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.12f), false);
            glow.rectTransform.Fixed(0.5f, 1f, 0f, -10f, w * 0.9f, 160f);
            return plate.rectTransform;
        }

        static VerticalLayoutGroup Column(RectTransform rt, float spacing)
        {
            var l = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            l.spacing = spacing;
            l.childControlHeight = true;
            l.childControlWidth = true;
            l.childForceExpandHeight = false;
            l.childForceExpandWidth = true;
            return l;
        }

        static HorizontalLayoutGroup Row(RectTransform rt, float spacing)
        {
            var l = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            l.spacing = spacing;
            l.childAlignment = TextAnchor.MiddleCenter;
            l.childControlHeight = true;
            l.childControlWidth = true;
            l.childForceExpandHeight = true;
            l.childForceExpandWidth = true;
            return l;
        }

        void BuildMain()
        {
            var v = NewView(Page.Main, "Main");

            var col = UIKit.Node("Column", v.rt).Fixed(0f, 0.5f, 140f, 20f, 700f, 820f);
            var kicker = UIKit.Label("Kicker", col, UIKit.Spaced("МЕХАТРОННЫЙ VR  ·  4DOF  ·  bHAPTICS"), 18, UIKit.Accent, TextAnchor.UpperLeft, UIKit.FontBold);
            kicker.rectTransform.Place(0, 1, 1, 1, 6f, -40f, 0f, 0f);
            pulseDot = UIKit.Img("Dot", col, UIKit.Circle, UIKit.Accent2);
            pulseDot.rectTransform.Fixed(0f, 1f, -18f, -10f, 10f, 10f);

            var title = UIKit.Label("Title", col, "ЛУННАЯ\nЭКСПЕДИЦИЯ", 92, UIKit.Ink, TextAnchor.UpperLeft, UIKit.FontBold);
            title.rectTransform.Place(0, 1, 1, 1, 0f, -260f, 0f, 50f);
            title.lineSpacing = 0.9f;
            var shadow = title.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0.4f, 0.6f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -4f);
            titleLine = UIKit.Img("TitleLine", col, null, UIKit.Accent);
            titleLine.rectTransform.anchorMin = titleLine.rectTransform.anchorMax = titleLine.rectTransform.pivot = new Vector2(0f, 1f);
            titleLine.rectTransform.anchoredPosition = new Vector2(6f, -278f);
            titleLine.rectTransform.sizeDelta = new Vector2(0f, 4f);
            var sub = UIKit.Label("Subtitle", col, "Соберите образцы, разверните связь, переживите бурю\nи вернитесь на базу. 7 этапов, 10–15 минут.", 24, UIKit.InkDim, TextAnchor.UpperLeft, UIKit.FontLight);
            sub.rectTransform.Place(0, 1, 1, 1, 6f, -380f, 0f, 296f);

            var buttons = UIKit.Node("Buttons", col).Strip(0, 1, 0f, 100f, 60f, 300f);
            Column(buttons, 8f);
            Add(v, MenuButton.Create(buttons, "ИГРАТЬ", () => flow.StartMission(), true, "Начать экспедицию.", "01"));
            Add(v, MenuButton.Create(buttons, "НАСТРОЙКИ", () => OpenSettings(Page.Main), true, "", "02"));
            Add(v, MenuButton.Create(buttons, "ВЫХОД", () => Show(Page.ConfirmExit), true, "", "03"));

            // Карточка состояния стенда
            var card = UIKit.Img("Status", v.rt, UIKit.Rounded, new Color(0.03f, 0.06f, 0.1f, 0.7f));
            card.rectTransform.Fixed(1f, 0f, -80f, 90f, 460f, 250f);
            UIKit.Img("Frame", card.transform, UIKit.Outline, UIKit.Line).rectTransform.Place(0, 0, 1, 1);
            var head = UIKit.Label("Head", card.transform, UIKit.Spaced("СОСТОЯНИЕ СТЕНДА"), 16, UIKit.Accent, TextAnchor.UpperLeft, UIKit.FontBold);
            head.rectTransform.Place(0, 1, 1, 1, 26f, -44f, 20f, 20f);
            statusText = UIKit.Label("Text", card.transform, "", 21, UIKit.Ink, TextAnchor.UpperLeft);
            statusText.rectTransform.Place(0, 0, 1, 1, 26f, 16f, 20f, 58f);
            statusText.lineSpacing = 1.25f;

            var hint = UIKit.Label("Hint", v.rt, "▲▼ выбор    ◀▶ изменить    Enter / A / курок руля — OK    Esc / B — назад    мышь работает тоже", 18, UIKit.InkMuted, TextAnchor.LowerLeft);
            hint.rectTransform.Fixed(0f, 0f, 146f, 40f, 1500f, 30f);
        }

        void BuildSettings()
        {
            var v = NewView(Page.Settings, "Settings");
            var panel = Panel(v.rt, 1620f, 900f);

            var title = UIKit.Label("Title", panel, UIKit.Spaced("НАСТРОЙКИ"), 40, UIKit.Ink, TextAnchor.MiddleLeft, UIKit.FontBold);
            title.rectTransform.Place(0, 1, 1, 1, 48f, -78f, 0f, 22f);
            var crumb = UIKit.Label("Crumb", panel, "Изменения применяются сразу · сохраняются кнопкой «Готово»", 18, UIKit.InkMuted, TextAnchor.MiddleRight);
            crumb.rectTransform.Place(0, 1, 1, 1, 0f, -78f, 48f, 22f);

            var tabsRt = UIKit.Node("TabsHost", panel).Place(0, 1, 1, 1, 36f, -148f, 36f, 92f);
            tabsItem = MenuTabs.Create(tabsRt, Tabs, () => tab, SetTab);
            ((RectTransform)tabsItem.transform).Place(0, 0, 1, 1);
            tabsItem.Menu = this;

            // Список строк с прокруткой
            var viewport = UIKit.Node("Viewport", panel).Place(0, 0, 0, 1, 36f, 110f, -1036f, 166f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0);
            settingsList = UIKit.Node("Content", viewport);
            settingsList.anchorMin = new Vector2(0f, 1f);
            settingsList.anchorMax = new Vector2(1f, 1f);
            settingsList.pivot = new Vector2(0.5f, 1f);
            settingsList.offsetMin = settingsList.offsetMax = Vector2.zero;
            var layout = Column(settingsList, 4f);
            layout.padding = new RectOffset(0, 18, 4, 12);
            settingsList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = settingsList;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            // Описание выбранного пункта
            var desc = UIKit.Img("Desc", panel, UIKit.Rounded, new Color(0.06f, 0.1f, 0.16f, 0.85f));
            desc.rectTransform.Place(1, 0, 1, 1, -560f, 110f, 36f, 166f);
            UIKit.Img("Frame", desc.transform, UIKit.Outline, UIKit.Line).rectTransform.Place(0, 0, 1, 1);
            var dk = UIKit.Label("Kicker", desc.transform, UIKit.Spaced("ОПИСАНИЕ"), 16, UIKit.Accent, TextAnchor.UpperLeft, UIKit.FontBold);
            dk.rectTransform.Place(0, 1, 1, 1, 30f, -50f, 30f, 26f);
            descTitle = UIKit.Label("Title", desc.transform, "", 30, UIKit.Ink, TextAnchor.UpperLeft, UIKit.FontBold);
            descTitle.rectTransform.Place(0, 1, 1, 1, 30f, -110f, 30f, 60f);
            descTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            descText = UIKit.Label("Text", desc.transform, "", 22, UIKit.InkDim, TextAnchor.UpperLeft, UIKit.Font);
            descText.rectTransform.Place(0, 0, 1, 1, 30f, 30f, 30f, 130f);
            descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descText.lineSpacing = 1.15f;

            // Нижняя панель
            var foot = UIKit.Node("Footer", panel).Strip(1, 1, -1010f, 36f, 28f, 62f);
            Row(foot, 16f);
            footer.Add(MenuButton.Create(foot, "ОТМЕНИТЬ", RevertSettings, false, "Вернуть настройки, сохранённые в прошлый раз (отменить правки этого сеанса)."));
            footer.Add(MenuButton.Create(foot, "ПО УМОЛЧАНИЮ", () => { GameSettings.ResetToDefaults(); }, false, "Сбросить ВСЕ настройки к заводским значениям."));
            footer.Add(MenuButton.Create(foot, "ГОТОВО", CloseSettings, false, "Сохранить настройки и вернуться."));
            foreach (var f in footer) f.Menu = this;
            var hint = UIKit.Label("Hint", panel, "Q / E — разделы    ◀ ▶ — значение    Esc — готово", 18, UIKit.InkMuted, TextAnchor.MiddleLeft);
            hint.rectTransform.Fixed(0f, 0f, 48f, 28f, 560f, 62f);

            BuildTab();
        }

        void BuildPause()
        {
            var v = NewView(Page.Pause, "Pause");
            var p = Panel(v.rt, 760f, 640f);
            var t = UIKit.Label("Title", p, UIKit.Spaced("ПАУЗА"), 52, UIKit.Ink, TextAnchor.MiddleCenter, UIKit.FontBold);
            t.rectTransform.Place(0, 1, 1, 1, 0f, -110f, 0f, 30f);
            pauseInfo = UIKit.Label("Info", p, "", 22, UIKit.InkDim, TextAnchor.MiddleCenter);
            pauseInfo.rectTransform.Place(0, 1, 1, 1, 0f, -180f, 0f, 110f);
            var list = UIKit.Node("Buttons", p).Strip(0, 1, 80f, 80f, 50f, 370f);
            Column(list, 8f);
            Add(v, MenuButton.Create(list, "ПРОДОЛЖИТЬ", () => flow.TogglePause(), true, "", "▶"));
            Add(v, MenuButton.Create(list, "НАСТРОЙКИ", () => OpenSettings(Page.Pause), true, "", "⚙"));
            Add(v, MenuButton.Create(list, "ЗАНОВО", () => flow.StartMission(), true, "", "↺"));
            Add(v, MenuButton.Create(list, "В МЕНЮ", () => flow.GoToMenu(), true, "", "⌂"));
        }

        void BuildResult()
        {
            var v = NewView(Page.Result, "Result");
            var p = Panel(v.rt, 860f, 700f);
            resultTitle = UIKit.Label("Title", p, "", 44, UIKit.Good, TextAnchor.MiddleCenter, UIKit.FontBold);
            resultTitle.rectTransform.Place(0, 1, 1, 1, 40f, -130f, 40f, 30f);
            resultTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            resultTitle.resizeTextForBestFit = true;
            resultTitle.resizeTextMinSize = 22;
            resultTitle.resizeTextMaxSize = 44;
            resultStats = UIKit.Label("Stats", p, "", 26, UIKit.Ink, TextAnchor.UpperCenter);
            resultStats.rectTransform.Place(0, 0, 1, 1, 60f, 230f, 60f, 150f);
            resultStats.lineSpacing = 1.3f;
            var list = UIKit.Node("Buttons", p).Strip(0, 1, 120f, 120f, 50f, 180f);
            Column(list, 8f);
            Add(v, MenuButton.Create(list, "ЕЩЁ РАЗ", () => flow.StartMission(), true, "", "↺"));
            Add(v, MenuButton.Create(list, "В МЕНЮ", () => flow.GoToMenu(), true, "", "⌂"));
        }

        void BuildConfirm()
        {
            var v = NewView(Page.ConfirmExit, "ConfirmExit");
            var p = Panel(v.rt, 720f, 360f);
            var t = UIKit.Label("Title", p, "Выйти из игры?", 44, UIKit.Ink, TextAnchor.MiddleCenter, UIKit.FontBold);
            t.rectTransform.Place(0, 1, 1, 1, 0f, -130f, 0f, 40f);
            var sub = UIKit.Label("Sub", p, "Платформа плавно опустится в парковочное положение.", 20, UIKit.InkDim, TextAnchor.MiddleCenter);
            sub.rectTransform.Place(0, 1, 1, 1, 0f, -180f, 0f, 130f);
            var row = UIKit.Node("Row", p).Strip(0, 1, 80f, 80f, 50f, 66f);
            Row(row, 24f);
            Add(v, MenuButton.Create(row, "ОСТАТЬСЯ", () => Show(Page.Main), false));
            Add(v, MenuButton.Create(row, "ВЫЙТИ", Quit, false));
        }

        // ================================================================ разделы настроек

        void SetTab(int i)
        {
            tab = i;
            BuildTab();
        }

        string[] ResolutionNames(out int current)
        {
            var res = SettingsApplier.Resolutions();
            var names = new string[res.Length];
            current = res.Length - 1;
            for (int i = 0; i < res.Length; i++)
            {
                names[i] = $"{res[i].width} × {res[i].height}  ·  {res[i].refreshRateRatio.value:0} Гц";
                if (res[i].width == Screen.width && res[i].height == Screen.height) current = i;
            }
            return names;
        }

        static string Pct(float v) => $"{v * 100f:0}%";

        void BuildTab()
        {
            var v = views.TryGetValue(Page.Settings, out var sv) ? sv : null;
            if (v == null) return;
            for (int i = settingsList.childCount - 1; i >= 0; i--) Destroy(settingsList.GetChild(i).gameObject);
            v.items.Clear();
            v.items.Add(tabsItem);
            Transform c = settingsList;

            MenuItem Item(MenuItem m) { m.Menu = this; v.items.Add(m); return m; }
            void Head(string t) => MenuHeader.Create(c, t);
            void Opt(string n, string[] o, Func<int> g, Action<int> s, string d) => Item(MenuOption.Create(c, n, o, g, s, d));
            void Tog(string n, Func<bool> g, Action<bool> s, string d) => Item(MenuToggle.Create(c, n, g, s, d));
            void Sld(string n, float a, float b, float st, Func<float> g, Action<float> s, Func<float, string> f, string d) => Item(MenuSlider.Create(c, n, a, b, st, g, s, f, d));
            void Act(string n, string btn, Action a, string d) => Item(MenuAction.Create(c, n, btn, a, d));
            // Ручная правка графики переводит профиль в «Своё».
            void Custom() => S.qualityPreset = 4;

            switch (tab)
            {
                case 0: // ГРАФИКА
                    Head("Качество");
                    Opt("Профиль качества", new[] { "Низкое", "Среднее", "Высокое", "Ультра", "Своё" }, () => S.qualityPreset,
                        i => { if (i < 4) S.ApplyQualityPreset(i); else S.qualityPreset = 4; },
                        "Готовый набор графических параметров.\n\nНизкое — для слабых ПК и стабильных 72+ FPS в шлеме.\nВысокое — рекомендуется для RTX 3060 и выше.\nУльтра — суперсэмплинг 120% и MSAA 8×.\n\nЛюбая ручная правка ниже переключит профиль в «Своё».");
                    Sld("Масштаб рендера", 0.5f, 1.5f, 0.05f, () => S.renderScale, x => { S.renderScale = x; Custom(); }, Pct,
                        "Внутреннее разрешение кадра относительно экрана/шлема.\n\nМеньше 100% — выше FPS, картинка мягче.\nБольше 100% — суперсэмплинг: чётче, но тяжелее.\n\nВ VR это самый сильный рычаг производительности.");
                    Opt("Масштабирование", new[] { "Билинейное", "AMD FSR 1.0", "STP (временное)" }, () => S.upscaler, x => { S.upscaler = x; Custom(); },
                        "Как растягивать кадр, если масштаб рендера меньше 100%.\n\nFSR 1.0 — пространственный апскейлер с повышением резкости.\nSTP — временной апскейлер Unity, лучше качество, нужен запас по GPU.");
                    Opt("Сглаживание MSAA", new[] { "Выкл", "2×", "4×", "8×" }, () => S.msaa switch { 2 => 1, 4 => 2, 8 => 3, _ => 0 },
                        x => { S.msaa = x switch { 1 => 2, 2 => 4, 3 => 8, _ => 1 }; Custom(); },
                        "Аппаратное сглаживание лесенок по краям объектов.\n\nДля VR рекомендуется 4× — без него мерцают тонкие детали (антенны, рамы).");
                    Tog("HDR-рендеринг", () => S.hdr, x => { S.hdr = x; Custom(); },
                        "Расширенный динамический диапазон: яркое солнце, свечение кристаллов и фар без пересвета. Нужен для красивого свечения (Bloom).");
                    Head("Тени и освещение");
                    Opt("Качество теней", new[] { "Выкл", "Низкое", "Среднее", "Высокое", "Ультра" }, () => S.shadows, x => { S.shadows = x; Custom(); },
                        "Разрешение карты теней от Солнца (512 – 4096) и число каскадов.\n\nНа Луне тени — главный источник объёма, выключать не рекомендуется.");
                    Sld("Дальность теней", 30f, 300f, 10f, () => S.shadowDistance, x => { S.shadowDistance = x; Custom(); }, x => $"{x:0} м",
                        "На каком расстоянии от камеры ещё рисуются тени. Дальше — красивее горизонт, но тени вблизи становятся менее чёткими.");
                    Tog("Мягкие тени", () => S.softShadows, x => { S.softShadows = x; Custom(); }, "Сглаженные края теней вместо резких пиксельных.");
                    Tog("Затенение SSAO", () => S.ssao, x => { S.ssao = x; Custom(); },
                        "Ambient Occlusion: затемнение в щелях, под камнями и в кратерах. Заметно добавляет объёма, стоит ~10% FPS.");
                    Head("Детализация");
                    Opt("Качество текстур", new[] { "Четверть", "Половина", "Полное" }, () => S.textureQuality, x => { S.textureQuality = x; Custom(); },
                        "Разрешение текстур. Понижайте, если не хватает видеопамяти.");
                    Opt("Анизотропная фильтрация", new[] { "Выкл", "Вкл", "Принудительно" }, () => S.anisotropic, x => { S.anisotropic = x; Custom(); },
                        "Чёткость текстур грунта под острым углом — то, что вы видите впереди ровера. Почти бесплатно.");
                    Opt("Детализация рельефа", new[] { "Низкая", "Средняя", "Высокая" }, () => S.terrainDetail, x => { S.terrainDetail = x; Custom(); },
                        "Точность сетки террейна и дальность полноценных текстур грунта.");
                    Sld("Дальность детализации", 0.5f, 3f, 0.1f, () => S.lodBias, x => { S.lodBias = x; Custom(); }, x => $"×{x:0.0}",
                        "LOD bias: на каком расстоянии модели переключаются на упрощённые версии.");
                    break;

                case 1: // ИЗОБРАЖЕНИЕ
                    Head("Постобработка");
                    Tog("Постобработка", () => S.postProcessing, x => S.postProcessing = x,
                        "Общий выключатель эффектов ниже. Без неё картинка плоская, но быстрее.");
                    Tog("Свечение (Bloom)", () => S.bloom, x => S.bloom = x, "Ореолы вокруг ярких источников: фары, кристаллы, огни базы, вспышки метеоритов.");
                    Sld("Сила свечения", 0f, 2f, 0.05f, () => S.bloomIntensity, x => S.bloomIntensity = x, x => $"{x:0.00}", "Насколько сильно светятся яркие объекты.");
                    Opt("Тонмаппинг", new[] { "Нет", "ACES (кино)", "Нейтральный" }, () => S.tonemapping, x => S.tonemapping = x,
                        "Как HDR-яркость сводится к экрану.\n\nACES — контрастно и «кинематографично».\nНейтральный — точнее цвета.");
                    Head("Цвет");
                    Sld("Яркость", -2f, 2f, 0.1f, () => S.brightness, x => S.brightness = x, x => $"{x:+0.0;-0.0;0.0} EV",
                        "Экспозиция кадра. Поднимите, если в кратерах слишком темно.");
                    Sld("Контраст", -50f, 50f, 5f, () => S.contrast, x => S.contrast = x, x => $"{x:+0;-0;0}", "Разница между светом и тенью.");
                    Sld("Насыщенность", -50f, 50f, 5f, () => S.saturation, x => S.saturation = x, x => $"{x:+0;-0;0}", "Насыщенность цветов. Лунный реголит почти серый — Земля и огни базы выиграют от +10.");
                    Head("Эффекты");
                    Tog("Виньетка", () => S.vignette, x => S.vignette = x, "Лёгкое затемнение по краям кадра — помогает от укачивания в VR.");
                    Tog("Зерно плёнки", () => S.filmGrain, x => S.filmGrain = x, "Шум как у видеокамер «Аполлона». Атмосферно, но в шлеме может мешать.");
                    break;

                case 2: // ЭКРАН
                {
                    Head("Окно (без шлема)");
                    var names = ResolutionNames(out int cur);
                    Opt("Разрешение", names, () => S.resolutionIndex >= 0 && S.resolutionIndex < names.Length ? S.resolutionIndex : cur, x => S.resolutionIndex = x,
                        "Разрешение окна на мониторе. В шлеме разрешение задаёт Meta Quest Link, а здесь — «Масштаб рендера».\n\nВ редакторе Unity не применяется.");
                    Opt("Режим окна", new[] { "Полный экран", "Окно без рамки", "Окно" }, () => S.windowMode, x => S.windowMode = x,
                        "Для стенда удобнее «Окно»: на мониторе оператора видно и игру, и панель F1.");
                    Tog("Вертикальная синхронизация", () => S.vsync, x => S.vsync = x, "Убирает разрывы кадра на мониторе. В шлеме синхронизацию делает Oculus Runtime.");
                    Opt("Ограничение FPS", new[] { "Без ограничения", "60", "72", "90", "120", "144" },
                        () => Mathf.Max(0, Array.IndexOf(SettingsApplier.FpsLimits, S.fpsLimit)), x => S.fpsLimit = SettingsApplier.FpsLimits[x],
                        "Верхний предел частоты кадров (экономит GPU и нагрев). Для VR выставляйте частоту шлема: 72 / 90 / 120.");
                    Sld("Поле зрения", 50f, 100f, 1f, () => S.fieldOfView, x => S.fieldOfView = x, x => $"{x:0}°", "Угол обзора камеры на мониторе. В шлеме определяется линзами и не меняется.");
                    Tog("Счётчик FPS", () => S.showFps, x => S.showFps = x, "Показывать частоту кадров в углу экрана.");
                    Head("Виртуальная реальность");
                    Act("Центрировать шлем", "ЦЕНТРОВАТЬ", () => seatRig?.Recenter(),
                        "Сядьте ровно, смотрите вперёд и нажмите — голова встанет в кресло водителя. То же делает кнопка 3 руля или F5.");
                    Sld("Высота сиденья", -0.25f, 0.25f, 0.01f, () => S.seatHeight, x => S.seatHeight = x, x => $"{x * 100f:+0;-0;0} см",
                        "Поднять или опустить точку глаз в кабине, если вы выше или ниже «среднего» пилота.");
                    break;
                }

                case 3: // ЗВУК
                    Head("Громкость");
                    Sld("Общая громкость", 0f, 1f, 0.05f, () => S.masterVolume, x => S.masterVolume = x, Pct, "Главный регулятор всего звука игры.");
                    Sld("Музыка", 0f, 1f, 0.05f, () => S.musicVolume, x => S.musicVolume = x, Pct, "Эмбиент-музыка в меню и на паузе.");
                    Sld("Эффекты", 0f, 1f, 0.05f, () => S.sfxVolume, x => S.sfxVolume = x, Pct, "Удары, взрывы метеоритов, разряды статики, сигналы задач и тревоги.");
                    Sld("Двигатель ровера", 0f, 1f, 0.05f, () => S.engineVolume, x => S.engineVolume = x, Pct, "Гул электромоторов колёс; тон растёт со скоростью.");
                    Sld("Окружение", 0f, 1f, 0.05f, () => S.ambienceVolume, x => S.ambienceVolume = x, Pct, "Системы жизнеобеспечения кабины и ветер пылевой бури.");
                    Sld("Интерфейс", 0f, 1f, 0.05f, () => S.uiVolume, x => S.uiVolume = x, Pct, "Щелчки и сигналы меню.");
                    Head("Поведение");
                    Tog("Музыка в меню", () => S.menuMusic, x => S.menuMusic = x, "Играть ли фоновую музыку в главном меню и на паузе.");
                    Tog("Без звука в фоне", () => S.muteInBackground, x => S.muteInBackground = x, "Глушить игру, когда окно не в фокусе (в редакторе не действует).");
                    Act("Проверка звука", "ПРОСЛУШАТЬ", GameAudio.PlayTest, "Короткий сигнал через группу «Эффекты» — проверьте наушники шлема.");
                    break;

                case 4: // УПРАВЛЕНИЕ
                    Head("Руль и педали");
                    Sld("Чувствительность руля", 0.5f, 4f, 0.1f, () => S.steerSensitivity, x => S.steerSensitivity = x, x => $"×{x:0.0}",
                        "Сколько оборотов руля нужно до полного поворота колёс. Больше — резче. Для руля 900° рекомендуется ×2.");
                    Sld("Мёртвая зона руля", 0f, 0.2f, 0.01f, () => S.steerDeadzone, x => S.steerDeadzone = x, Pct, "Люфт в центре, который игнорируется.");
                    Tog("Инверсия руля", () => S.invertSteer, x => S.invertSteer = x, "Если ровер поворачивает в обратную сторону.");
                    Sld("Мёртвая зона педалей", 0f, 0.2f, 0.01f, () => S.pedalDeadzone, x => S.pedalDeadzone = x, Pct, "Лёгкое касание педали не будет давать газ или тормоз.");
                    Head("Самолётный джойстик");
                    Sld("Чувствительность", 0.3f, 2f, 0.05f, () => S.stickSensitivity, x => S.stickSensitivity = x, x => $"×{x:0.00}", "Множитель отклонения ручки при рулении.");
                    Sld("Мёртвая зона", 0f, 0.3f, 0.01f, () => S.stickDeadzone, x => S.stickDeadzone = x, Pct, "Центральная зона ручки без реакции.");
                    Sld("Кривая отклика (expo)", 0f, 1f, 0.05f, () => S.stickExpo, x => S.stickExpo = x, Pct, "Чем больше — тем мягче реакция около центра и точнее мелкие поправки.");
                    Tog("Инверсия тангажа", () => S.invertStickPitch, x => S.invertStickPitch = x, "По умолчанию ручка от себя — газ, на себя — тормоз.");
                    Tog("Рычаг тяги (РУД)", () => S.useThrottleLever, x => S.useThrottleLever = x, "Использовать РУД джойстика как постоянный газ.");
                    Head("Клавиатура и мышь");
                    Sld("Скорость руления (клавиши)", 1f, 8f, 0.25f, () => S.keyboardSteerSpeed, x => S.keyboardSteerSpeed = x, x => $"{x:0.00}", "Как быстро руль доходит до упора при нажатии A/D.");
                    Sld("Чувствительность мыши", 0.05f, 0.5f, 0.01f, () => S.mouseSensitivity, x => S.mouseSensitivity = x, x => $"{x:0.00}", "Обзор мышью (с зажатой ПКМ) без шлема.");
                    break;

                case 5: // ПЛАТФОРМА И ЖИЛЕТ
                    Head("Платформа 4DOF");
                    Tog("Движение платформы", () => S.motionEnabled, x => S.motionEnabled = x,
                        "Полностью отключить движение (например, для новичков). Аварийная остановка — F12 на клавиатуре оператора.");
                    Sld("Интенсивность", 0f, 1f, 0.05f, () => S.motionIntensity, x => S.motionIntensity = x, Pct,
                        "Общий масштаб наклонов и толчков. Начинайте с 30% и повышайте, если пилоту комфортно.");
                    Sld("Скорость осей", 0.5f, 6f, 0.25f, () => S.motionSmoothness, x => S.motionSmoothness = x, x => $"{x:0.00}/с",
                        "Ограничение скорости движения осей. Меньше — плавнее и безопаснее, больше — резче удары.");
                    Tog("Тангаж (наклон вперёд/назад)", () => S.motionPitch, x => S.motionPitch = x, "Разгон, торможение, склоны кратеров.");
                    Tog("Крен (наклон вбок)", () => S.motionRoll, x => S.motionRoll = x, "Повороты, боковые склоны, порывы ветра в буре.");
                    Tog("Подъём (вверх/вниз)", () => S.motionHeave, x => S.motionHeave = x, "Кочки, прыжки, приземления, лунотрясение.");
                    Tog("Рыскание (поворот)", () => S.motionYaw, x => S.motionYaw = x, "Короткий импульс при резких поворотах.");
                    Sld("Тряска по камням", 0f, 2f, 0.1f, () => S.roughnessIntensity, x => S.roughnessIntensity = x, x => $"×{x:0.0}", "Мелкая вибрация на каменистых участках.");
                    Head("Тактильный жилет bHaptics");
                    Tog("Тактильный отклик", () => S.hapticsEnabled, x => S.hapticsEnabled = x, "Включить/выключить жилет целиком.");
                    Sld("Интенсивность жилета", 0f, 1f, 0.05f, () => S.hapticsIntensity, x => S.hapticsIntensity = x, Pct, "Сила всех эффектов: удары, сканирование, тревоги.");
                    Sld("Фоновая вибрация", 0f, 2f, 0.1f, () => S.hapticsRumble, x => S.hapticsRumble = x, x => $"×{x:0.0}", "Постоянный «гул» мотора, камней, бури и лунотрясения.");
                    Act("Проверка жилета", "ТЕСТ", () => applier?.TestHaptics(), "Пробегает по всем моторам: перед сверху вниз, затем спина. Убедитесь, что чувствуется каждый.");
                    break;

                case 6: // ИГРА
                    Head("Экспедиция");
                    Opt("Сложность", new[] { "Лёгкая", "Нормальная", "Сложная" }, () => (int)S.difficulty, x => S.difficulty = (Difficulty)x,
                        "Лёгкая — кислорода на 30% больше, урон корпусу вдвое меньше.\nНормальная — как задумано.\nСложная — кислорода на 20% меньше, урон ×1.5.\n\nПрименяется со следующего запуска миссии.");
                    Tog("Стрелка-компас", () => S.compass, x => S.compass = x, "Стрелка на приборной панели, указывающая на текущую цель.");
                    Tog("Подсказки этапов", () => S.hints, x => S.hints = x, "Строка «что делать сейчас» на экране пульта.");
                    Opt("Единицы скорости", new[] { "км/ч", "м/с" }, () => S.speedUnits, x => S.speedUnits = x, "Как показывать скорость на пульте.");
                    Head("Данные");
                    Act("Сбросить все настройки", "СБРОС", GameSettings.ResetToDefaults, "Вернуть заводские значения во всех разделах.");
                    break;
            }

            foreach (var f in footer) v.items.Add(f);
            foreach (var m in v.items) m.Refresh();
            v.sel = Mathf.Clamp(v.sel, 0, v.items.Count - 1);
            if (current == Page.Settings) ApplySelection(v);
            scroll.verticalNormalizedPosition = 1f;
        }

        void RefreshAll()
        {
            foreach (var v in views.Values)
                foreach (var m in v.items)
                    if (m != null) m.Refresh();
        }

        void OpenSettings(Page from)
        {
            settingsReturn = from;
            Show(Page.Settings);
        }

        void CloseSettings()
        {
            GameSettings.Save();
            Show(settingsReturn);
        }

        void RevertSettings() => GameSettings.Revert();

        static void Quit()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ================================================================ страницы

        void OnState(GameState s)
        {
            switch (s)
            {
                case GameState.Menu: Show(Page.Main); break;
                case GameState.Playing: Show(Page.None); break;
                case GameState.Paused:
                    FillPause();
                    if (current != Page.Settings) Show(Page.Pause);
                    break;
                case GameState.Result: FillResult(); Show(Page.Result); break;
            }
        }

        void Show(Page p)
        {
            if (p == current) return;
            if (current != Page.None && p != Page.None && current != Page.Settings && p != Page.Settings && current != Page.ConfirmExit)
                GameAudio.PlayUi(UiSound.Click);
            current = p;
            if (views.TryGetValue(p, out var v))
            {
                v.rt.gameObject.SetActive(true);
                v.shown = 0f;
                v.sel = FirstSelectable(v);
                ApplySelection(v);
                if (p == Page.Settings) UpdateDescription(v);
            }
            if (p == Page.Main && titleLine != null) titleLine.rectTransform.sizeDelta = new Vector2(0f, 4f);
        }

        static int FirstSelectable(View v)
        {
            for (int i = 0; i < v.items.Count; i++) if (v.items[i].Selectable) return i;
            return 0;
        }

        void FillPause()
        {
            if (flow == null || pauseInfo == null) return;
            int t = Mathf.FloorToInt(flow.MissionTime);
            var m = flow.Mission;
            string stage = m != null ? $"Этап {m.StageIndex + 1}/{m.StageCount} · {m.Title}" : $"Образцы {flow.Collected}/{flow.Total}";
            int o2 = flow.Oxygen != null ? Mathf.CeilToInt(flow.Oxygen.Remaining) : 0;
            string hull = flow.Hull != null ? $"   ·   корпус {flow.Hull.Normalized * 100f:0}%" : "";
            pauseInfo.text = $"{stage}\n<color=#8fa3b8>{t / 60}:{t % 60:00} в пути   ·   O₂ {o2 / 60}:{o2 % 60:00}{hull}</color>";
        }

        void FillResult()
        {
            if (flow == null) return;
            bool ok = flow.LastSuccess;
            resultTitle.text = UIKit.Spaced(ok ? "ЭКСПЕДИЦИЯ ЗАВЕРШЕНА" : string.IsNullOrEmpty(flow.FailReason) ? "МИССИЯ ПРОВАЛЕНА" : flow.FailReason);
            resultTitle.color = ok ? UIKit.Good : UIKit.Bad;
            int t = Mathf.FloorToInt(flow.MissionTime);
            int calls = 0;
            foreach (var kv in HapticsDirector.Calls) calls += kv.Value;
            var m = flow.Mission;
            string stage = m != null ? $"{m.StageIndex + 1} / {m.StageCount}  {m.Title}" : $"{flow.Collected}/{flow.Total}";
            string hull = flow.Hull != null ? $"{flow.Hull.Normalized * 100f:0}%" : "—";
            string o2 = flow.Oxygen != null ? $"{Mathf.CeilToInt(flow.Oxygen.Remaining) / 60}:{Mathf.CeilToInt(flow.Oxygen.Remaining) % 60:00}" : "—";
            resultStats.text =
                $"<color=#8fa3b8>Время</color>   {t / 60}:{t % 60:00}\n" +
                $"<color=#8fa3b8>Этап</color>   {stage}\n" +
                $"<color=#8fa3b8>Образцы</color>   {flow.Collected} / {flow.Total}\n" +
                $"<color=#8fa3b8>Корпус</color>   {hull}      <color=#8fa3b8>O₂</color>   {o2}\n" +
                $"<color=#8fa3b8>Тактильных эффектов</color>   {calls}  ({HapticsDirector.Calls.Count} видов)";
        }

        // ================================================================ кадр

        void Update()
        {
            // После перезагрузки скриптов в Play Mode несериализуемые поля пусты — меню неработоспособно до перезапуска.
            if (input == null || canvas == null) return;
            UpdateMode();
            input.Update();

            if (flow != null && flow.State == GameState.Playing)
            {
                if (input.Pause) flow.TogglePause();
            }
            else if (views.TryGetValue(current, out var v))
            {
                Navigate(v);
            }

            Animate();
            UpdateCameras();
            statusTimer -= Time.unscaledDeltaTime;
            if (statusTimer <= 0f && current == Page.Main) { statusTimer = 1f; UpdateStatus(); }
        }

        void Navigate(View v)
        {
            if (v.items.Count == 0) return;
            if (input.Vertical != 0) Move(v, input.Vertical);
            if (input.Horizontal != 0)
            {
                var item = v.items[v.sel];
                // У кнопок «влево/вправо» — переход к соседней кнопке (ряды «Да / Нет», нижняя панель настроек).
                if (item is MenuButton) Move(v, input.Horizontal);
                else item.Adjust(input.Horizontal);
            }
            if (input.Tab != 0 && current == Page.Settings) tabsItem.Adjust(input.Tab);
            if (input.Submit) v.items[v.sel].Submit();
            if (input.Back)
            {
                GameAudio.PlayUi(UiSound.Back);
                switch (current)
                {
                    case Page.Settings: CloseSettings(); break;
                    case Page.Pause: flow.TogglePause(); break;
                    case Page.ConfirmExit: Show(Page.Main); break;
                    case Page.Main: Show(Page.ConfirmExit); break;
                }
            }
        }

        void Move(View v, int dir)
        {
            int n = v.items.Count, i = v.sel;
            for (int k = 0; k < n; k++)
            {
                i = (i + dir + n) % n;
                if (v.items[i].Selectable) break;
            }
            if (i == v.sel) return;
            v.sel = i;
            GameAudio.PlayUi(UiSound.Hover);
            ApplySelection(v);
        }

        /// <summary>Мышь навелась на пункт.</summary>
        public void Hover(MenuItem item)
        {
            if (!views.TryGetValue(current, out var v)) return;
            int i = v.items.IndexOf(item);
            if (i < 0 || i == v.sel) return;
            v.sel = i;
            GameAudio.PlayUi(UiSound.Hover);
            ApplySelection(v, false);
        }

        void ApplySelection(View v, bool scrollTo = true)
        {
            for (int i = 0; i < v.items.Count; i++) v.items[i].Selected = i == v.sel;
            if (current != Page.Settings) return;
            UpdateDescription(v);
            if (scrollTo) ScrollTo(v.items[v.sel]);
        }

        void UpdateDescription(View v)
        {
            if (v.sel < 0 || v.sel >= v.items.Count) return;
            var item = v.items[v.sel];
            var label = item.transform.Find("Label")?.GetComponent<Text>();
            descTitle.text = item == tabsItem ? Tabs[tab] : label != null ? label.text : item.name;
            descText.text = item.Description;
        }

        void ScrollTo(MenuItem item)
        {
            if (scroll == null || item == null || !item.transform.IsChildOf(settingsList)) return;
            Canvas.ForceUpdateCanvases();
            var rt = (RectTransform)item.transform;
            float viewH = scroll.viewport.rect.height;
            float contentH = settingsList.rect.height;
            if (contentH <= viewH) return;
            float top = -rt.anchoredPosition.y - rt.rect.height * (1f - rt.pivot.y);
            float bottom = top + rt.rect.height;
            float y = settingsList.anchoredPosition.y;
            if (top < y) y = top - 50f;
            else if (bottom > y + viewH) y = bottom - viewH + 20f;
            settingsList.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, 0f, contentH - viewH));
        }

        void Animate()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var kv in views)
            {
                var v = kv.Value;
                bool on = kv.Key == current;
                v.shown = Mathf.MoveTowards(v.shown, on ? 1f : 0f, dt * (on ? 3.5f : 6f));
                float e = 1f - Mathf.Pow(1f - v.shown, 3f);
                v.group.alpha = e;
                v.group.interactable = v.group.blocksRaycasts = on;
                v.rt.anchoredPosition = new Vector2(0f, -24f * (1f - e));
                if (!on && v.shown <= 0f && v.rt.gameObject.activeSelf) v.rt.gameObject.SetActive(false);
            }

            bool open = current != Page.None;
            bool main = current == Page.Main || current == Page.ConfirmExit || (current == Page.Settings && settingsReturn == Page.Main);
            Fade(backdrop, open && !xr && !main ? 0.62f : 0f, dt);
            Fade(sideShade, current == Page.Main && !xr ? 0.92f : 0f, dt);
            Fade(bottomShade, current == Page.Main && !xr ? 0.75f : 0f, dt);
            Fade(vrPlate, open && xr ? 0.82f : 0f, dt);
            canvas.enabled = open || AnyVisible();

            if (current == Page.Main)
            {
                float target = 220f;
                var s = titleLine.rectTransform.sizeDelta;
                titleLine.rectTransform.sizeDelta = new Vector2(UIKit.Smooth(s.x, target, 4f), 4f);
                float p = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
                pulseDot.color = new Color(UIKit.Accent2.r, UIKit.Accent2.g, UIKit.Accent2.b, 0.4f + 0.6f * p);
            }

            // Смена плана облётной камеры — через затемнение.
            float cut = 0f;
            if (menuCamera != null && menuCamera.isActiveAndEnabled)
            {
                float tc = menuCamera.TimeToCut;
                cut = tc < 0.5f ? 1f - tc / 0.5f : 0f;
            }
            Fade(cutFade, cut, dt * 3f);
        }

        bool AnyVisible()
        {
            foreach (var v in views.Values) if (v.shown > 0f) return true;
            return false;
        }

        static void Fade(Image img, float target, float dt)
        {
            if (img == null) return;
            var c = img.color;
            c.a = Mathf.MoveTowards(c.a, target, dt * 2.5f);
            img.color = c;
            img.enabled = c.a > 0.001f;
        }

        // ================================================================ режим (монитор / VR) и камеры

        void UpdateMode()
        {
            bool now = VRSeatRig.XRActive || DebugVrLayout;
            if (modeSet && now == xr) return;
            xr = now;
            modeSet = true;
            if (xr)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = cockpitCamera;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.dynamicPixelsPerUnit = 2f;
                // Панель висит перед кабиной на уровне глаз и едет вместе с ровером.
                Transform anchor = seatRig != null ? seatRig.transform.parent : rover != null ? rover.transform : null;
                root.SetParent(anchor, false);
                Vector3 eye = seatRig != null ? seatRig.transform.localPosition : new Vector3(0f, 1.5f, 0f);
                root.localPosition = eye + new Vector3(0f, 0.22f, vrDistance);
                root.localRotation = Quaternion.identity;
                root.sizeDelta = new Vector2(1920f, 1080f);
                root.localScale = Vector3.one * vrScale;
            }
            else
            {
                root.SetParent(transform, false);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                root.localScale = Vector3.one;
            }
        }

        void UpdateCameras()
        {
            bool main = current == Page.Main || current == Page.ConfirmExit || (current == Page.Settings && settingsReturn == Page.Main);
            bool cinematic = !xr && main && menuCamera != null;
            if (menuCamera != null && menuCamera.gameObject.activeSelf != cinematic) menuCamera.gameObject.SetActive(cinematic);
            if (cockpitCamera != null && !xr) cockpitCamera.enabled = !cinematic;
            else if (cockpitCamera != null) cockpitCamera.enabled = true;
        }

        // ================================================================ состояние стенда

        void UpdateStatus()
        {
            string Dot(bool ok) => ok ? "<color=#66ff8c>●</color>" : "<color=#ffb04a>●</color>";
            bool vr = VRSeatRig.XRActive;
            var inp = rover != null ? rover.Input : null;
            string wheel = inp != null && inp.WheelDevices.Count > 0 ? inp.WheelDevices[0] : "не найден";
            string stick = inp != null && inp.FlightStickDevices.Count > 0 ? inp.FlightStickDevices[0] : "нет";
            var vest = FindFirstObjectByType<BhapticsVestDevice>();
            bool vestOk = vest != null && vest.IsConnected;
            bool platformOk = false;
            foreach (var p in FindObjectsByType<MotionPlatformBehaviour>(FindObjectsSortMode.None))
                if (p is not DebugMotionPlatform && p.IsConnected) platformOk = true;
            statusText.text =
                $"{Dot(vr)}  Шлем VR  <color=#8fa3b8>{(vr ? "подключён" : "нет — режим монитора")}</color>\n" +
                $"{Dot(inp != null && inp.WheelDevices.Count > 0)}  Руль  <color=#8fa3b8>{Short(wheel)}</color>\n" +
                $"{Dot(inp != null && inp.FlightStickDevices.Count > 0)}  Джойстик  <color=#8fa3b8>{Short(stick)}</color>\n" +
                $"{Dot(vestOk)}  Жилет  <color=#8fa3b8>{(vestOk ? "bHaptics SDK2" : "эмуляция (F1)")}</color>\n" +
                $"{Dot(platformOk)}  Платформа  <color=#8fa3b8>{(platformOk ? "подключена" : "эмуляция (F1)")}</color>";
        }

        static string Short(string s) => s.Length > 26 ? s.Substring(0, 25) + "…" : s;

        void OnGUI()
        {
            if (!S.showFps) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.UpperRight };
            GUI.Label(new UnityEngine.Rect(Screen.width - 130f, 8f, 120f, 24f), $"{1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0} FPS", style);
        }
    }
}
