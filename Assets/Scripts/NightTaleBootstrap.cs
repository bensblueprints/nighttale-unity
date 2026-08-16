using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NightTale
{
    /// <summary>
    /// Entry point. Builds the entire UI at runtime: game picker (with a topbar and
    /// account button), name/language modal, story view (topbar, chapter banner,
    /// portrait, stat bar, scrolling story log, odds card, choices, free-text input),
    /// auth (sign up / log in), account, paywall, and toasts. Talks to the Flask
    /// backend at apiBaseUrl via NightTaleApi.
    /// </summary>
    public class NightTaleBootstrap : MonoBehaviour
    {
        [Header("Server")]
        public string apiBaseUrl = "https://play.nighttalegames.com";

        [Header("Optional Unity Ads (leave blank to use backend house ads)")]
        public string unityAdsGameId = "";
        public string unityAdsRewardedPlacementId = "rewardedVideo";

        private const int GUEST_TURNS = 12;
        private const int AD_REWARD_TURNS = 5;

        private static readonly string[] LANGUAGES = { "en", "es", "fr", "de", "pt", "it", "ja", "zh", "ko", "vi", "ru" };

        // state
        private UserState _user;        // null == guest
        private string _sessionId;
        private int _guestTurnsLeft = GUEST_TURNS;
        private string _storyLang = "en";
        private bool _busy;
        private string _selectedGameSlug;
        private string _selectedGameTitle;
        private string _selectedGameSubtitle;
        private List<GameInfo> _games;

        // canvas
        private RectTransform _root;

        // picker
        private GameObject _picker;
        private Text _pickerAccountLabel;

        // story view
        private GameObject _storyView;
        private Text _storyAccountLabel;
        private Text _titleText;
        private Text _turnsText;
        private RawImage _portrait;
        private GameObject _portraitGo;
        private RectTransform _storyLogRect;
        private RectTransform _statBar;
        private Text _chapterBanner;
        private ScrollRect _storyLogScroll;
        private RectTransform _storyLogContent;
        private RectTransform _oddsCard;
        private RectTransform _choicesPanel;
        private InputField _actionInput;
        private Button _rollButton;

        // name modal
        private GameObject _nameModal;
        private Text _nameTitle;
        private Text _nameSub;
        private InputField _nameInput;

        // auth modal
        private GameObject _authModal;
        private bool _authSignup = true;
        private Text _authBlurb;
        private Text _authError;
        private GameObject _authSignupForm;
        private GameObject _authLoginForm;
        private InputField _suName, _suUsername, _suEmail, _suPassword, _liIdentifier, _liPassword;
        private Toggle _suTos;

        // account modal
        private GameObject _accountModal;
        private RectTransform _accountBody;
        private RectTransform _accountActions;

        // paywall
        private GameObject _paywall;
        private Text _paywallMsg;
        private Text _paywallTurns;
        private Text _paywallPlan;
        private Button _watchAdButton;
        private Button _paywallSignupButton;

        // toast
        private Text _toast;
        private Coroutine _toastCo;

        private enum StoryKind { Narration, Player, System }

        private void Start()
        {
            NightTaleApi.BaseUrl = apiBaseUrl;
            AdManager.Init(unityAdsGameId, unityAdsRewardedPlacementId);
            BuildUi();
            DetectLoginThenShowPicker();
        }

        // ================================================================ UI core

        private void BuildUi()
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(transform);

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvas.GetComponent<RectTransform>();

            // Toast (always on top).
            var toastGo = new GameObject("Toast", typeof(RectTransform), typeof(Image));
            toastGo.transform.SetParent(_root, false);
            var toastRt = toastGo.GetComponent<RectTransform>();
            toastRt.SetAnchor(0.5f, 0, 0.5f, 0, new Vector2(-440, 40), new Vector2(440, 130));
            toastGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);
            _toast = Text("ToastText", toastRt, "", 34, TextAnchor.MiddleCenter);
            _toast.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, 0, 0);
            toastGo.SetActive(false);
        }

        private RectTransform Panel(string name, Color bg, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            go.GetComponent<Image>().color = bg;
            return rt;
        }

        private static Sprite CreateVerticalGradient(Color top, Color bottom, int height = 256)
        {
            var tex = new Texture2D(1, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
            {
                float t = (float)y / (height - 1);
                tex.SetPixel(0, y, Color.Lerp(bottom, top, t));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, height), new Vector2(0.5f, 0.5f));
        }

        private Text Text(string name, Transform parent, string content, int size,
            TextAnchor align = TextAnchor.UpperLeft, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = content; t.fontSize = size; t.alignment = align;
            t.color = color ?? Color.white;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        private Button Button(string name, Transform parent, string label, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.16f, 0.16f, 0.28f);
            var txt = Text(name + "_label", rt, label, 40, TextAnchor.MiddleCenter);
            var txtRt = txt.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;
            go.GetComponent<Button>().onClick.AddListener(() => onClick());
            return go.GetComponent<Button>();
        }

        private InputField MakeInputField(string name, Transform parent, string placeholder,
            InputField.ContentType contentType = InputField.ContentType.Standard,
            InputField.LineType lineType = InputField.LineType.SingleLine)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 96);
            go.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.3f);
            var field = go.GetComponent<InputField>();

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 34; text.alignment = TextAnchor.MiddleLeft; text.color = Color.white;
            text.supportRichText = false;
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(16, 4); textRt.offsetMax = new Vector2(-16, -4);

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(go.transform, false);
            var ph = phGo.GetComponent<Text>();
            ph.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ph.text = placeholder; ph.fontSize = 34; ph.fontStyle = FontStyle.Italic;
            ph.alignment = TextAnchor.MiddleLeft; ph.color = new Color(1f, 1f, 1f, 0.6f);
            ph.supportRichText = false;
            var phRt = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(16, 4); phRt.offsetMax = new Vector2(-16, -4);

            field.textComponent = text;
            field.placeholder = ph;
            field.contentType = contentType;
            field.lineType = lineType;
            return field;
        }

        private Toggle Toggle(string name, Transform parent, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 64);

            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(go.transform, false);
            var bg = bgGo.GetComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.26f);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 0.5f); bgRt.anchorMax = new Vector2(0, 0.5f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.sizeDelta = new Vector2(52, 52);
            bgRt.anchoredPosition = new Vector2(34, 0);

            var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkGo.transform.SetParent(bgGo.transform, false);
            var check = checkGo.GetComponent<Image>();
            check.color = Color.white;
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = Vector2.zero; checkRt.anchorMax = Vector2.one;
            checkRt.offsetMin = new Vector2(9, 9); checkRt.offsetMax = new Vector2(-9, -9);

            var lbl = Text(name + "_label", rt, label, 32, TextAnchor.MiddleLeft);
            lbl.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 96, 0, 0, 0);

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = check;
            toggle.isOn = false;
            return toggle;
        }

        private RectTransform ModalOverlay(string name, Vector2 cardHalf, out RectTransform card)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0.78f);

            var c = new GameObject("Card", typeof(RectTransform), typeof(Image));
            c.transform.SetParent(rt, false);
            var crt = c.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f); crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = cardHalf * 2f;
            c.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.12f);
            card = crt;
            return rt;
        }

        // ================================================================ boot

        private void DetectLoginThenShowPicker()
        {
            StartCoroutine(NightTaleApi.Me((user, err) =>
            {
                if (user != null && !string.IsNullOrEmpty(user.username))
                    _user = user;
                ShowPicker();
            }));
        }

        // ================================================================ topbar

        private Text BuildTopbar(Transform parent, bool withBack)
        {
            var bar = new GameObject("Topbar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(parent, false);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(0, -110); rt.offsetMax = new Vector2(0, 0);
            bar.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.1f);

            Text brand;
            if (withBack)
            {
                var back = Button("Back", rt, "\u2190 Games", Vector2.zero, Vector2.one,
                    new Vector2(12, 12), new Vector2(-760, -12), ShowPicker);
                back.GetComponent<RectTransform>().SetAnchor(0, 0, 0, 1, 12, 12, 240, -12);
                brand = Text("Brand", rt, "NightTale", 34, TextAnchor.MiddleLeft);
                brand.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 252, 0, -240, 0);
            }
            else
            {
                brand = Text("Brand", rt, "NightTale", 44, TextAnchor.MiddleLeft);
                brand.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 24, 0, -240, 0);
            }
            brand.color = new Color(0.9f, 0.75f, 1f);

            var acct = Button("Account", rt, "Guest", Vector2.zero, Vector2.one,
                new Vector2(0, 0), new Vector2(0, 0), OpenAccount);
            acct.GetComponent<RectTransform>().SetAnchor(1, 0, 1, 1, -240, 14, -12, -14);
            var label = acct.GetComponentInChildren<Text>();
            label.fontSize = 32;
            return label;
        }

        private void RefreshAccountLabels()
        {
            var txt = _user != null ? _user.username : "Guest";
            if (_pickerAccountLabel != null) _pickerAccountLabel.text = txt;
            if (_storyAccountLabel != null) _storyAccountLabel.text = txt;
        }

        // ================================================================ picker

        private void ShowPicker()
        {
            if (_storyView != null) _storyView.SetActive(false);
            if (_paywall != null) _paywall.SetActive(false);
            CloseModal(_nameModal); CloseModal(_authModal); CloseModal(_accountModal);

            if (_picker == null)
            {
                var p = Panel("Picker", new Color(0.03f, 0.03f, 0.06f),
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var pImg = p.GetComponent<Image>();
                pImg.sprite = CreateVerticalGradient(new Color(0.07f, 0.05f, 0.15f), new Color(0.015f, 0.015f, 0.03f));
                pImg.color = Color.white;
                _pickerAccountLabel = BuildTopbar(p, false);

                Text("PickerTitle", p, "Choose your story", 54, TextAnchor.MiddleCenter)
                    .GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -200, -30, -120);
                Text("PickerSub", p, "AI-written worlds. Your choices decide everything.", 32,
                        TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.6f))
                    .GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -245, -30, -190);

                var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
                scrollGo.transform.SetParent(p, false);
                var scrollRt = scrollGo.GetComponent<RectTransform>();
                scrollRt.SetAnchor(0, 0, 1, 1, 30, -30, -30, -270);
                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                viewport.transform.SetParent(scrollRt, false);
                viewport.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, 0, 0);
                var content = new GameObject("Content", typeof(RectTransform),
                    typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                content.transform.SetParent(viewport.transform, false);
                var crt = content.GetComponent<RectTransform>();
                crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
                crt.pivot = new Vector2(0.5f, 1);
                var vlg = content.GetComponent<VerticalLayoutGroup>();
                vlg.spacing = 16; vlg.padding = new RectOffset(20, 20, 20, 20);
                vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
                vlg.childControlHeight = false; vlg.childForceExpandHeight = false;
                content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var sr = scrollGo.GetComponent<ScrollRect>();
                sr.viewport = viewport.GetComponent<RectTransform>();
                sr.content = crt; sr.horizontal = false; sr.vertical = true;
                _picker = p.gameObject;

                StartCoroutine(NightTaleApi.GetGames((games, err) =>
                {
                    if (games == null)
                    {
                        Text("Err", crt, "Failed to load games: " + err, 36);
                        return;
                    }
                    _games = games;
                    foreach (var g in games)
                    {
                        if (g.coming_soon) continue;
                        AddGameButton(crt, g);
                    }
                }));
            }
            RefreshAccountLabels();
            _picker.SetActive(true);
        }

        private void AddGameButton(Transform parent, GameInfo g)
        {
            var go = new GameObject("Game_" + g.slug, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 188);
            go.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.2f);

            // left accent strip
            var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(rt, false);
            var ar = accent.GetComponent<RectTransform>();
            ar.SetAnchor(0, 0, 0, 1, 0, 0, 8, 0);
            accent.GetComponent<Image>().color = new Color(0.62f, 0.45f, 0.95f);

            var coverGo = new GameObject("Cover", typeof(RectTransform), typeof(RawImage));
            coverGo.transform.SetParent(rt, false);
            var crt = coverGo.GetComponent<RectTransform>();
            crt.SetAnchor(0, 0, 0, 1, 20, 14, 184, -14);
            var coverImg = coverGo.GetComponent<RawImage>();
            coverImg.color = new Color(0.22f, 0.22f, 0.3f);

            var title = Text("Title", rt, g.title, 38, TextAnchor.MiddleLeft);
            title.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 204, -34, -20, -70);
            var sub = Text("Sub", rt, g.subtitle ?? "", 27, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.55f));
            sub.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 204, -74, -20, -120);

            var slug = g.slug;
            go.GetComponent<Button>().onClick.AddListener(() => ShowNameModal(slug));
            if (!string.IsNullOrEmpty(g.cover))
                StartCoroutine(LoadImage(coverImg, g.cover));
        }

        // ================================================================ name modal

        private void ShowNameModal(string slug)
        {
            _selectedGameSlug = slug;
            var g = _games != null ? _games.Find(x => x.slug == slug) : null;
            _selectedGameTitle = g != null ? g.title : slug;
            _selectedGameSubtitle = g != null ? (g.subtitle ?? "") : "";

            if (_nameModal == null)
            {
                var overlay = ModalOverlay("NameModal", new Vector2(460, 560), out var card);
                _nameModal = overlay.gameObject;

                _nameTitle = Text("NameTitle", card, "Begin your story", 46, TextAnchor.MiddleCenter);
                _nameTitle.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 40, -60, -40, -10);
                _nameSub = Text("NameSub", card, "", 32, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.6f));
                _nameSub.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 40, -115, -40, -65);

                Text("NameLabel", card, "Character name", 30);
                _nameInput = MakeInputField("NameInput", card, "Wanderer");
                _nameInput.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 40, -240, -40, -155);
                _nameInput.text = "Wanderer";

                Text("LangLabel", card, "Language", 30).GetComponent<RectTransform>()
                    .SetAnchor(0, 1, 1, 1, 40, -300, -40, -270);

                var langRow = new GameObject("LangRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                langRow.transform.SetParent(card, false);
                var lr = langRow.GetComponent<RectTransform>();
                lr.SetAnchor(0, 1, 1, 1, 40, -430, -40, -310);
                var hlg = langRow.GetComponent<HorizontalLayoutGroup>();
                hlg.spacing = 10; hlg.childControlWidth = false; hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
                foreach (var lang in LANGUAGES)
                {
                    var code = lang;
                    var lb = Button("Lang_" + lang, langRow.transform, lang.ToUpperInvariant(),
                        Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, () => _storyLang = code);
                    var lrt = lb.GetComponent<RectTransform>();
                    lrt.sizeDelta = new Vector2(78, 64);
                }

                Button("NameCancel", card, "Cancel", Vector2.zero, Vector2.one,
                        Vector2.zero, Vector2.zero, () => CloseModal(_nameModal))
                    .GetComponent<RectTransform>().SetAnchor(0, 0, 0.5f, 0, 40, 40, -10, 130);
                Button("NameStart", card, "Begin", Vector2.zero, Vector2.one,
                        Vector2.zero, Vector2.zero, ConfirmStart)
                    .GetComponent<RectTransform>().SetAnchor(0.5f, 0, 1, 0, 10, 40, -40, 130);
            }

            _nameTitle.text = _selectedGameTitle;
            _nameSub.text = string.IsNullOrEmpty(_selectedGameSubtitle) ? "" : _selectedGameSubtitle;
            _nameModal.SetActive(true);
        }

        private void SetLanguage(string lang)
        {
            _storyLang = lang;
        }

        private void ConfirmStart()
        {
            var name = (_nameInput != null ? _nameInput.text : "").Trim();
            if (string.IsNullOrEmpty(name)) name = "Wanderer";
            CloseModal(_nameModal);
            StartGame(name);
        }

        // ================================================================ start game

        private void StartGame(string playerName)
        {
            _sessionId = null;
            if (_storyView == null) BuildStoryView();
            _picker.SetActive(false);
            _paywall?.SetActive(false);
            _storyView.SetActive(true);
            _titleText.text = _selectedGameTitle;
            RenderTurns();
            ClearStoryLog();
            SetStatBar(null);
            SetChapterBanner(null);
            SetPortrait(null);
            HideOdds();
            ClearChoices();
            if (_actionInput != null) _actionInput.text = "";
            _rollButton.gameObject.SetActive(false);

            AppendStory("Summoning the opening scene\u2026", StoryKind.System);
            _busy = true;
            if (_user != null)
                StartCoroutine(NightTaleApi.Start(playerName, _selectedGameSlug, OnStory));
            else
                StartCoroutine(NightTaleApi.GuestStart(playerName, _selectedGameSlug, OnStory));
        }

        // ================================================================ story view

        private void BuildStoryView()
        {
            var p = Panel("StoryView", new Color(0.04f, 0.04f, 0.07f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var pImg2 = p.GetComponent<Image>();
            pImg2.sprite = CreateVerticalGradient(new Color(0.08f, 0.06f, 0.17f), new Color(0.02f, 0.02f, 0.04f));
            pImg2.color = Color.white;
            _storyAccountLabel = BuildTopbar(p, true);

            _titleText = Text("Title", p, "", 42, TextAnchor.MiddleCenter);
            _titleText.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -165, -30, -115);

            _turnsText = Text("Turns", p, "", 30, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.7f));
            _turnsText.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -200, -30, -168);

            _chapterBanner = Text("Chapter", p, "", 30, TextAnchor.MiddleCenter, new Color(0.9f, 0.75f, 1f));
            _chapterBanner.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -238, -30, -206);
            _chapterBanner.gameObject.SetActive(false);

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
            portraitGo.transform.SetParent(p, false);
            _portraitGo = portraitGo;
            var prt = portraitGo.GetComponent<RectTransform>();
            prt.SetAnchor(0, 1, 1, 1, 30, -390, -30, -210);
            _portrait = portraitGo.GetComponent<RawImage>();
            _portrait.color = new Color(0.1f, 0.1f, 0.14f);
            _portraitGo.SetActive(false);

            var statBar = new GameObject("StatBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            statBar.transform.SetParent(p, false);
            _statBar = statBar.GetComponent<RectTransform>();
            _statBar.SetAnchor(0, 1, 1, 1, 30, -442, -30, -398);
            var slg = statBar.GetComponent<HorizontalLayoutGroup>();
            slg.spacing = 10; slg.childControlWidth = false; slg.childControlHeight = true;
            slg.childForceExpandWidth = false; slg.childForceExpandHeight = false;
            _statBar.gameObject.SetActive(false);

            // Story log (scrollable).
            var storyScroll = new GameObject("StoryScroll", typeof(RectTransform), typeof(ScrollRect));
            storyScroll.transform.SetParent(p, false);
            var srt = storyScroll.GetComponent<RectTransform>();
            srt.SetAnchor(0, 0, 1, 1, 30, 470, -30, -210);
            _storyLogRect = srt;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(srt, false);
            viewport.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, 0, 0);
            var content = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 14; vlg.padding = new RectOffset(6, 6, 0, 20);
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _storyLogScroll = storyScroll.GetComponent<ScrollRect>();
            _storyLogScroll.viewport = viewport.GetComponent<RectTransform>();
            _storyLogScroll.content = crt; _storyLogScroll.horizontal = false; _storyLogScroll.vertical = true;
            _storyLogContent = crt;

            // Odds card (above choices).
            var oddsGo = new GameObject("Odds", typeof(RectTransform), typeof(Image));
            oddsGo.transform.SetParent(p, false);
            _oddsCard = oddsGo.GetComponent<RectTransform>();
            _oddsCard.SetAnchor(0, 0, 1, 0, 20, 470, -20, 650);
            oddsGo.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.16f);
            _oddsCard.gameObject.SetActive(false);

            // Choices (2-column grid).
            _choicesPanel = new GameObject("Choices", typeof(RectTransform)).GetComponent<RectTransform>();
            _choicesPanel.SetParent(p, false);
            _choicesPanel.SetAnchor(0, 0, 1, 0, 20, 110, -20, 480);

            // Free-text input bar.
            var inputBar = new GameObject("InputBar", typeof(RectTransform));
            inputBar.transform.SetParent(p, false);
            var ibrt = inputBar.GetComponent<RectTransform>();
            ibrt.SetAnchor(0, 0, 1, 0, 20, 15, -20, 95);
            _actionInput = MakeInputField("ActionInput", ibrt, "What do you do? (or pick a choice above)",
                InputField.ContentType.Standard, InputField.LineType.MultiLineNewline);
            _actionInput.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, -100, 0);
            var send = Button("Send", ibrt, "\u2192", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, SendFreeAction);
            send.GetComponent<RectTransform>().SetAnchor(1, 0, 1, 1, -96, 0, 0, 0);

            _rollButton = Button("Roll", p, "Roll the dice", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, OnRoll);
            _rollButton.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 0, 20, 110, -20, 480);
            _rollButton.gameObject.SetActive(false);

            _storyView = p.gameObject;
        }

        private void OnStory(StoryResponse r)
        {
            _busy = false;
            if (r == null)
            {
                AppendStory("Error talking to the server.", StoryKind.System);
                return;
            }

            // Session may carry a user state (e.g. after ad grant).
            if (r.state != null) _user = r.state;

            if (!string.IsNullOrEmpty(r.error))
            {
                HandleError(r);
                return;
            }

            if (_storyView == null) BuildStoryView();
            if (_picker != null) _picker.SetActive(false);
            _paywall?.SetActive(false);
            _storyView.SetActive(true);
            RefreshAccountLabels();

            _sessionId = r.session_id ?? _sessionId;
            if (!string.IsNullOrEmpty(r.language)) _storyLang = r.language;
            if (r.game != null) _titleText.text = r.game.title;
            else if (_titleText != null && string.IsNullOrEmpty(_titleText.text)) _titleText.text = _selectedGameTitle;

            if (r.guest_turns_left != null) _guestTurnsLeft = Math.Max(0, r.guest_turns_left.Value);

            if (!string.IsNullOrEmpty(r.story)) AppendStory(r.story, StoryKind.Narration);
            else if (!string.IsNullOrEmpty(r.raw_story)) AppendStory(r.raw_story, StoryKind.Narration);

            var portraitUrl = !string.IsNullOrEmpty(r.portrait) ? r.portrait
                : !string.IsNullOrEmpty(r.image) ? r.image : null;
            if (!string.IsNullOrEmpty(portraitUrl))
            {
                _portraitGo.SetActive(true);
                SetStoryLogTop(450);
                StartCoroutine(LoadImage(_portrait, portraitUrl));
            }
            else
            {
                _portraitGo.SetActive(false);
                SetStoryLogTop(210);
            }

            SetStatBar(r);
            SetChapterBanner(r);
            RenderExtras(r);
            RebuildChoices(r);
            RenderTurns();
        }

        private void HandleError(StoryResponse r)
        {
            var msg = r.error ?? r.message ?? "Something went wrong.";
            if (msg == "guest_wall")
            {
                AppendStory("That's your " + GUEST_TURNS + " free guest turns. Create a free account to keep this story going \u2014 you'll get 25 more turns, plus watch ads for +5 turns each, and keep all your progress.", StoryKind.System);
                ShowAuth(true);
            }
            else if (msg == "story_unavailable")
            {
                AppendStory(r.message ?? "The story engine hiccuped \u2014 try that again.", StoryKind.System);
            }
            else if (msg == "out_of_turns" || msg == "verify_email")
            {
                ShowPaywall(msg == "verify_email"
                    ? (r.message ?? "Please verify your email to continue.")
                    : "You're out of turns. Watch an ad to earn +5 turns and keep playing.");
            }
            else if (msg == "coming_soon")
            {
                ShowPaywall(r.message ?? "This game is coming soon.");
            }
            else if (msg.Contains("403") || msg.Contains("belongs"))
            {
                AppendStory("This session belongs to another account. Start a new game from the picker to continue.", StoryKind.System);
            }
            else
            {
                AppendStory("Error: " + msg, StoryKind.System);
            }
        }

        // ---- story log ---------------------------------------------------------

        private void ClearStoryLog()
        {
            if (_storyLogContent == null) return;
            foreach (Transform c in _storyLogContent) Destroy(c.gameObject);
        }

        private void AppendStory(string text, StoryKind kind)
        {
            if (string.IsNullOrEmpty(text) || _storyLogContent == null) return;
            var go = new GameObject("Para", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_storyLogContent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text; t.fontSize = 40; t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = kind == StoryKind.Player ? new Color(0.55f, 0.8f, 1f)
                : kind == StoryKind.System ? new Color(1f, 0.9f, 0.55f) : Color.white;
            Canvas.ForceUpdateCanvases();
            if (_storyLogScroll != null) _storyLogScroll.verticalNormalizedPosition = 0f;
        }

        // ---- stats -------------------------------------------------------------

        private void SetStatBar(StoryResponse r)
        {
            if (_statBar == null) return;
            foreach (Transform c in _statBar) Destroy(c.gameObject);
            int count = 0;
            if (r != null)
            {
                if (r.max_health > 0 || r.health > 0)
                    AddStatChip("HP", r.health + "/" + r.max_health);
                if (r.gold != 0) AddStatChip("Gold", r.gold.ToString());
                if (!string.IsNullOrEmpty(r.location)) AddStatChip("Location", r.location);
                var ss = r.stage_stats ?? r.stage_sheet;
                if (ss != null)
                    foreach (var s in ss)
                        AddStatChip(s.label ?? s.key, s.value != null ? s.value.ToString() : "");
                else if (r.stats != null)
                    foreach (var kv in r.stats)
                        AddStatChip(kv.Key.ToUpperInvariant(), kv.Value != null ? kv.Value.ToString() : "");
            }
            _statBar.gameObject.SetActive(count > 0);
        }

        private void AddStatChip(string label, string value)
        {
            var go = new GameObject("Chip", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_statBar, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 44);
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.28f);
            var t = Text("ChipText", rt, label + " " + value, 28, TextAnchor.MiddleCenter);
            t.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, 0, 0);
        }

        private void SetChapterBanner(StoryResponse r)
        {
            if (_chapterBanner == null) return;
            if (r != null && r.stage != null && !string.IsNullOrEmpty(r.stage.title))
            {
                _chapterBanner.text = r.stage.title + (string.IsNullOrEmpty(r.stage.goal) ? "" : " \u2014 " + r.stage.goal);
                _chapterBanner.gameObject.SetActive(true);
            }
            else
            {
                _chapterBanner.gameObject.SetActive(false);
            }
        }

        private void SetPortrait(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            _portraitGo.SetActive(true);
            SetStoryLogTop(450);
            StartCoroutine(LoadImage(_portrait, url));
        }

        private void SetStoryLogTop(float fromTop)
        {
            if (_storyLogRect == null) return;
            var om = _storyLogRect.offsetMax;
            _storyLogRect.offsetMax = new Vector2(om.x, -fromTop);
        }

        // ---- odds / extras -----------------------------------------------------

        private void HideOdds()
        {
            if (_oddsCard != null) _oddsCard.gameObject.SetActive(false);
        }

        private void RenderExtras(StoryResponse r)
        {
            if (r == null) return;
            if (r.stage_changed != null && !string.IsNullOrEmpty(r.stage_changed.title))
                Toast("Chapter: " + r.stage_changed.title, 3000);

            BuildOddsCard(r);

            if (r.stage_roll != null)
            {
                AppendStory("Rolled " + (r.stage_roll.value != null ? r.stage_roll.value.ToString() : "?")
                    + " \u2014 " + (r.stage_roll.band ?? "").ToUpperInvariant(), StoryKind.System);
            }
            else if (r.roll_result != null)
            {
                AppendStory((r.roll_result.stat ?? "STAT").ToUpperInvariant() + " check DC " + r.roll_result.dc
                    + ": rolled " + r.roll_result.roll + " \u2014 " + (r.roll_result.success ? "SUCCESS" : "FAILURE"), StoryKind.System);
            }

            if (r.deltas != null && r.deltas.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in r.deltas)
                {
                    var v = kv.Value;
                    parts.Add(kv.Key + " " + (v is long || v is int || v is double
                        ? (Convert.ToDouble(v) >= 0 ? "+" + v : v.ToString()) : "=" + v));
                }
                AppendStory(string.Join("  \u00b7  ", parts), StoryKind.System);
            }

            if (r.dice != null)
            {
                var vals = (r.dice.rolls != null && r.dice.rolls.Count > 0)
                    ? string.Join(", ", r.dice.rolls) : (r.dice.value != null ? r.dice.value.ToString() : "");
                if (!string.IsNullOrEmpty(vals)) AppendStory("Dice: " + vals, StoryKind.System);
            }

            if (r.ending != null)
            {
                var endingText = r.ending is string ? (string)r.ending : "";
                if (string.IsNullOrEmpty(endingText))
                {
                    var dict = r.ending as Dictionary<string, object>;
                    if (dict != null && dict.TryGetValue("title", out var tv) && tv != null) endingText = tv.ToString();
                }
                if (!string.IsNullOrEmpty(endingText)) AppendStory("ENDING: " + endingText, StoryKind.System);
            }

            if (r.completed)
                AppendStory("The story is complete. You can start a new one from the game picker.", StoryKind.System);
        }

        private void BuildOddsCard(StoryResponse r)
        {
            if (_oddsCard == null) return;
            foreach (Transform c in _oddsCard) Destroy(c.gameObject);

            bool show = false;
            var title = "Test your luck";
            string reason = null;
            var bands = r.odds != null ? r.odds.bands : null;

            if (r.odds != null)
            {
                show = true;
                title = r.odds.check_label ?? r.odds.label ?? "Test your luck";
                reason = r.odds.reason;
            }
            else if (r.roll_required != null && r.roll_required.roll_required)
            {
                show = true;
                var rr = r.roll_required;
                title = (rr.stat ?? "STAT").ToUpperInvariant() + " check \u2014 DC " + rr.dc;
                reason = "Roll against your " + (rr.stat ?? "stat") + " (" + (rr.score != 0 ? rr.score.ToString() : "10") + ").";
            }

            if (!show) { _oddsCard.gameObject.SetActive(false); _rollButton.gameObject.SetActive(false); return; }

            Text("OddsTitle", _oddsCard, title, 34, TextAnchor.MiddleCenter, Color.white)
                .GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 16, -16, -16, -50);
            if (!string.IsNullOrEmpty(reason))
                Text("OddsReason", _oddsCard, reason, 28, TextAnchor.UpperLeft, new Color(1, 1, 1, 0.7f))
                    .GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 16, -56, -16, -16);

            var bandRow = new GameObject("Bands", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bandRow.transform.SetParent(_oddsCard, false);
            var br = bandRow.GetComponent<RectTransform>();
            br.SetAnchor(0, 0, 1, 0, 16, 8, -16, 78);
            var hlg = bandRow.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10; hlg.childControlWidth = true; hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = false;
            if (bands != null)
            {
                AddBand(br, "Success", bands.success, new Color(0.2f, 0.6f, 0.3f));
                AddBand(br, "Mixed", bands.mixed, new Color(0.7f, 0.6f, 0.2f));
                AddBand(br, "Failure", bands.failure, new Color(0.7f, 0.25f, 0.25f));
            }

            var rollBtn = Button("RollBtn", _oddsCard, "Roll the dice", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, OnRoll);
            rollBtn.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 16, -16, -16, -104);
            _oddsCard.gameObject.SetActive(true);
            _rollButton.gameObject.SetActive(false);
        }

        private void AddBand(Transform parent, string label, double? pct, Color color)
        {
            var go = new GameObject("Band_" + label, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var t = Text("BandText", go.transform, label + "\n" + (pct != null ? pct.Value.ToString() + "%" : "\u2014"),
                30, TextAnchor.MiddleCenter, Color.white);
            t.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, 0, 0);
        }

        // ---- choices -----------------------------------------------------------

        private void ClearChoices()
        {
            if (_choicesPanel == null) return;
            foreach (Transform c in _choicesPanel) Destroy(c.gameObject);
        }

        private void RebuildChoices(StoryResponse r)
        {
            ClearChoices();
            if (r.completed)
            {
                var t = Text("End", _choicesPanel, "The End \u2014 thanks for playing!", 40, TextAnchor.MiddleCenter);
                var trt = t.GetComponent<RectTransform>();
                trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
                trt.pivot = new Vector2(0.5f, 1); trt.sizeDelta = new Vector2(0, 120);
                return;
            }

            // buttons[] (stage engine) or choices[] (legacy text list).
            int added = 0;
            if (r.buttons != null && r.buttons.Count > 0)
            {
                foreach (var b in r.buttons)
                {
                    var label = b.label;
                    if (!string.IsNullOrEmpty(b.description) && b.description != b.label)
                        label = b.label + " \u2014 " + b.description;
                    AddChoiceButton(label, b.action, added);
                    added++;
                }
            }
            else if (r.choices != null && r.choices.Count > 0)
            {
                for (int i = 0; i < r.choices.Count; i++)
                {
                    AddChoiceButton(r.choices[i], r.choices[i], added);
                    added++;
                }
            }

            if (added == 0)
            {
                var t = Text("NoChoices", _choicesPanel, "No actions available.", 34);
                var trt = t.GetComponent<RectTransform>();
                trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
                trt.pivot = new Vector2(0.5f, 1); trt.sizeDelta = new Vector2(0, 100);
            }
        }

        private void AddChoiceButton(string label, string action, int index)
        {
            // Compress: cap length, then lay out in a 2-column grid.
            if (label.Length > 60) label = label.Substring(0, 57) + "...";
            int row = index / 2;
            int col = index % 2;
            var go = new GameObject("Choice", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_choicesPanel, false);
            var cr = go.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0.5f, 0.5f);
            cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.anchoredPosition = new Vector2(col == 0 ? -125f : 125f, row == 0 ? 90f : -90f);
            cr.sizeDelta = new Vector2(240, 170);
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.28f);
            var txt = Text("ChoiceLabel", go.transform, label, 26, TextAnchor.MiddleCenter);
            var trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(-16, -12);
            var a = action;
            go.GetComponent<Button>().onClick.AddListener(() => Choose(a));
        }

        private void Choose(string action)
        {
            if (_busy) return;
            AppendStory(action, StoryKind.Player);
            _busy = true;
            if (_user != null)
                StartCoroutine(NightTaleApi.Action(_sessionId, action, OnStory));
            else
                StartCoroutine(NightTaleApi.GuestAction(_sessionId, action, OnStory));
        }

        private void SendFreeAction()
        {
            if (_busy || _actionInput == null) return;
            var text = _actionInput.text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _actionInput.text = "";
            Choose(text);
        }

        private void OnRoll()
        {
            if (_busy) return;
            _busy = true;
            if (_user != null)
                StartCoroutine(NightTaleApi.Roll(_sessionId, OnStory));
            else
                StartCoroutine(NightTaleApi.GuestRoll(_sessionId, OnStory));
        }

        // ---- turns -------------------------------------------------------------

        private void RenderTurns()
        {
            if (_turnsText == null) return;
            if (_user == null)
            {
                _turnsText.text = "Guest \u2014 " + _guestTurnsLeft + " turns left";
            }
            else if (_user.subscribed)
            {
                _turnsText.text = "Unlimited turns";
            }
            else
            {
                var parts = new List<string>();
                if (_user.free_turns_remaining > 0) parts.Add(_user.free_turns_remaining + " free turns");
                if (_user.credits > 0) parts.Add(_user.credits + " credits");
                _turnsText.text = parts.Count > 0 ? string.Join(" \u00b7 ", parts) : "No turns left";
            }
        }

        // ================================================================ auth modal

        private void ShowAuth(bool signup)
        {
            if (_authModal == null) BuildAuthModal();
            SetAuthMode(signup);
            _authError.gameObject.SetActive(false);
            _authModal.SetActive(true);
        }

        private void BuildAuthModal()
        {
            var overlay = ModalOverlay("AuthModal", new Vector2(470, 780), out var card);
            _authModal = overlay.gameObject;

            var tabs = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tabs.transform.SetParent(card, false);
            var trt = tabs.GetComponent<RectTransform>();
            trt.SetAnchor(0, 1, 1, 1, 30, -70, -30, -10);
            var hlg = tabs.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12; hlg.childControlWidth = true; hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
            Button("TabSignup", tabs.transform, "Sign Up", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, () => SetAuthMode(true));
            Button("TabLogin", tabs.transform, "Log In", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, () => SetAuthMode(false));

            _authBlurb = Text("Blurb", card, "", 28, TextAnchor.UpperLeft, new Color(1, 1, 1, 0.7f));
            _authBlurb.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -150, -30, -80);

            // Sign-up form.
            _authSignupForm = new GameObject("SignupForm", typeof(RectTransform));
            _authSignupForm.transform.SetParent(card, false);
            var sf = _authSignupForm.GetComponent<RectTransform>();
            sf.SetAnchor(0, 0, 1, 1, 30, 60, -30, -150);

            _suName = MakeInputField("SuName", sf, "Display name");
            _suName.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 0, -116, 0, -20);
            _suUsername = MakeInputField("SuUsername", sf, "Username");
            _suUsername.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 0, -221, 0, -125);
            _suEmail = MakeInputField("SuEmail", sf, "Email");
            _suEmail.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 0, -326, 0, -230);
            _suPassword = MakeInputField("SuPassword", sf, "Password (min 6 chars)", InputField.ContentType.Password);
            _suPassword.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 0, -431, 0, -335);
            _suTos = Toggle("Tos", sf, "I am 18+ and accept the Terms of Service.");
            _suTos.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 0, -480, 0, -440);

            // Login form.
            _authLoginForm = new GameObject("LoginForm", typeof(RectTransform));
            _authLoginForm.transform.SetParent(card, false);
            var lf = _authLoginForm.GetComponent<RectTransform>();
            lf.SetAnchor(0, 0, 1, 1, 30, 60, -30, -150);
            _liIdentifier = MakeInputField("LiIdentifier", lf, "Username or email");
            _liIdentifier.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 0, -116, 0, -20);
            _liPassword = MakeInputField("LiPassword", lf, "Password", InputField.ContentType.Password);
            _liPassword.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 0, -221, 0, -125);

            _authError = Text("AuthError", card, "", 30, TextAnchor.UpperLeft, new Color(1f, 0.5f, 0.5f));
            _authError.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 0, 30, 130, -30, 200);
            _authError.gameObject.SetActive(false);

            Button("AuthCancel", card, "Cancel", Vector2.zero, Vector2.one,
                    Vector2.zero, Vector2.zero, () => CloseModal(_authModal))
                .GetComponent<RectTransform>().SetAnchor(0, 0, 0.5f, 0, 30, 30, -10, 120);
            Button("AuthSubmit", card, "Continue", Vector2.zero, Vector2.one,
                    Vector2.zero, Vector2.zero, SubmitAuth)
                .GetComponent<RectTransform>().SetAnchor(0.5f, 0, 1, 0, 10, 30, -30, 120);
        }

        private void SetAuthMode(bool signup)
        {
            _authSignup = signup;
            if (_authSignupForm != null) _authSignupForm.SetActive(signup);
            if (_authLoginForm != null) _authLoginForm.SetActive(!signup);
            if (_authBlurb != null)
                _authBlurb.text = signup
                    ? "Create a free account \u2014 25 free turns, keep your progress, and watch ads for +5 turns each."
                    : "Log in to continue your stories.";
        }

        private void SubmitAuth()
        {
            if (_busy) return;
            _authError.gameObject.SetActive(false);

            if (_authSignup)
            {
                var username = _suUsername.text.Trim();
                var email = _suEmail.text.Trim();
                var name = _suName.text.Trim();
                var password = _suPassword.text;
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email) ||
                    string.IsNullOrEmpty(name) || password.Length < 6)
                {
                    _authError.text = "Fill in all fields \u2014 password must be at least 6 characters.";
                    _authError.gameObject.SetActive(true);
                    return;
                }
                if (!_suTos.isOn)
                {
                    _authError.text = "You must be 18+ and accept the Terms of Service.";
                    _authError.gameObject.SetActive(true);
                    return;
                }
                _busy = true;
                StartCoroutine(NightTaleApi.Register(username, email, name, password, OnAuthDone));
            }
            else
            {
                var identifier = _liIdentifier.text.Trim();
                var password = _liPassword.text;
                if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
                {
                    _authError.text = "Enter your username/email and password.";
                    _authError.gameObject.SetActive(true);
                    return;
                }
                _busy = true;
                StartCoroutine(NightTaleApi.Login(identifier, password, OnAuthDone));
            }
        }

        private void OnAuthDone(UserState user, string err)
        {
            _busy = false;
            if (user != null && !string.IsNullOrEmpty(user.username))
            {
                _user = user;
                _guestTurnsLeft = GUEST_TURNS;
                CloseModal(_authModal);
                RefreshAccountLabels();
                RenderTurns();
                Toast(_authSignup
                    ? "Welcome! You have " + user.free_turns_remaining + " free turns."
                    : "Welcome back!");
            }
            else
            {
                _authError.text = err ?? "Something went wrong.";
                _authError.gameObject.SetActive(true);
            }
        }

        // ================================================================ account modal

        private void OpenAccount()
        {
            if (_accountModal == null)
            {
                var overlay = ModalOverlay("AccountModal", new Vector2(460, 560), out var card);
                _accountModal = overlay.gameObject;
                Text("AccountTitle", card, "Account", 46, TextAnchor.MiddleCenter)
                    .GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -80, -30, -20);
                _accountBody = new GameObject("AccountBody", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                _accountBody.SetParent(card, false);
                _accountBody.SetAnchor(0, 1, 1, 1, 50, -100, -50, -760);
                _accountActions = new GameObject("AccountActions", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                _accountActions.SetParent(card, false);
                _accountActions.SetAnchor(0, 0, 1, 0, 40, 40, -40, 170);
            }

            foreach (Transform c in _accountBody) Destroy(c.gameObject);
            foreach (Transform c in _accountActions) Destroy(c.gameObject);

            bool loggedIn = _user != null;
            string bodyText;
            if (loggedIn)
            {
                var u = _user;
                bodyText = "Username: " + u.username + "\n\n" +
                           "Name: " + (string.IsNullOrEmpty(u.name) ? "\u2014" : u.name) + "\n\n" +
                           "Email: " + (string.IsNullOrEmpty(u.email) ? "\u2014" : u.email) + "\n\n" +
                           "Plan: " + (u.subscribed ? "Unlimited" : "Free") + "\n\n" +
                           "Free turns: " + (u.subscribed ? "\u221e" : u.free_turns_remaining.ToString()) + "\n\n" +
                           "Credits: " + (u.credits > 0 ? u.credits.ToString() : "0");
            }
            else
            {
                bodyText = "You're playing as a guest with " + GUEST_TURNS + " free turns.\n\nCreate a free account to keep your progress and watch ads for more turns.";
            }

            var body = Text("AccountBodyText", _accountBody, bodyText, 36, TextAnchor.UpperLeft);
            body.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, 0, 0);

            if (loggedIn)
            {
                Button("AcctLogout", _accountActions, "Log out", Vector2.zero, Vector2.one,
                        Vector2.zero, Vector2.zero, Logout)
                    .GetComponent<RectTransform>().SetAnchor(0, 0, 0.5f, 0, 0, 0, -10, 100);
            }
            else
            {
                Button("AcctSignup", _accountActions, "Create free account", Vector2.zero, Vector2.one,
                        Vector2.zero, Vector2.zero, () => { CloseModal(_accountModal); ShowAuth(true); })
                    .GetComponent<RectTransform>().SetAnchor(0, 0, 0.5f, 0, 0, 0, -10, 100);
            }
            Button("AcctClose", _accountActions, "Close", Vector2.zero, Vector2.one,
                    Vector2.zero, Vector2.zero, () => CloseModal(_accountModal))
                .GetComponent<RectTransform>().SetAnchor(0.5f, 0, 1, 0, 10, 0, 0, 100);

            _accountModal.SetActive(true);
        }

        private void Logout()
        {
            StartCoroutine(NightTaleApi.Logout(() =>
            {
                _user = null;
                _sessionId = null;
                _guestTurnsLeft = GUEST_TURNS;
                CloseModal(_accountModal);
                RefreshAccountLabels();
                ShowPicker();
                Toast("Logged out \u2014 playing as guest.");
            }));
        }

        // ================================================================ paywall

        private void ShowPaywall(string msg)
        {
            if (_paywall == null)
            {
                var overlay = ModalOverlay("Paywall", new Vector2(470, 520), out var card);
                _paywall = overlay.gameObject;

                Text("PaywallTitle", card, "Your tale pauses here", 44, TextAnchor.MiddleCenter)
                    .GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -80, -30, -20);
                _paywallMsg = Text("PaywallMsg", card, "", 32, TextAnchor.UpperLeft);
                _paywallMsg.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 40, -180, -40, -90);

                _watchAdButton = Button("WatchAd", card, "Watch Ad for +5 Turns", Vector2.zero, Vector2.one,
                        Vector2.zero, Vector2.zero, WatchAd)
                    .GetComponent<Button>();
                _watchAdButton.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 60, -340, -60, -210);

                _paywallSignupButton = Button("PaywallSignup", card, "Create a free account \u2014 get 25 turns",
                        Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                        () => { CloseModal(_paywall); ShowAuth(true); })
                    .GetComponent<Button>();
                _paywallSignupButton.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 60, -410, -60, -280);

                var stats = new GameObject("PaywallStats", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                stats.transform.SetParent(card, false);
                var srt = stats.GetComponent<RectTransform>();
                srt.SetAnchor(0, 1, 1, 1, 60, -470, -60, -430);
                var hlg = stats.GetComponent<HorizontalLayoutGroup>();
                hlg.spacing = 12; hlg.childControlWidth = true; hlg.childForceExpandWidth = true;
                hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
                _paywallTurns = Text("PTurns", srt, "", 28, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.8f));
                _paywallPlan = Text("PPlan", srt, "", 28, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.8f));

                Button("PaywallClose", card, "Back to game", Vector2.zero, Vector2.one,
                        Vector2.zero, Vector2.zero, () => CloseModal(_paywall))
                    .GetComponent<RectTransform>().SetAnchor(0, 0, 1, 0, 60, 30, -60, 130);
            }

            _paywallMsg.text = msg;
            _paywallTurns.text = _user == null
                ? "Guest \u2014 " + _guestTurnsLeft + " turns left"
                : (_user.subscribed ? "Unlimited" : _user.free_turns_remaining + " free turns \u00b7 " + _user.credits + " credits");
            _paywallPlan.text = _user != null && _user.subscribed ? "Unlimited plan" : (_user != null ? "Free account" : "Guest");
            bool loggedIn = _user != null;
            _watchAdButton.gameObject.SetActive(loggedIn);
            _paywallSignupButton.gameObject.SetActive(!loggedIn);
            _paywall.SetActive(true);
        }

        private void WatchAd()
        {
            AdManager.ShowRewarded((rewarded) =>
            {
                if (!rewarded) return;
                StartCoroutine(NightTaleApi.AdSlot(slot =>
                {
                    if (slot == null || string.IsNullOrEmpty(slot.slot_id)) return;
                    StartCoroutine(NightTaleApi.AdComplete(slot.slot_id, state =>
                    {
                        if (state != null) _user = state;
                        CloseModal(_paywall);
                        RenderTurns();
                        RefreshAccountLabels();
                        if (_storyView != null) _storyView.SetActive(true);
                        Toast("+" + AD_REWARD_TURNS + " turns granted!");
                    }));
                }));
            });
        }

        // ================================================================ toast

        private void Toast(string msg, int ms = 2600)
        {
            if (_toast == null) return;
            _toast.text = msg;
            _toast.transform.parent.gameObject.SetActive(true);
            if (_toastCo != null) StopCoroutine(_toastCo);
            _toastCo = StartCoroutine(HideToast(ms));
        }

        private IEnumerator HideToast(int ms)
        {
            yield return new WaitForSecondsRealtime(ms / 1000f);
            if (_toast != null) _toast.transform.parent.gameObject.SetActive(false);
        }

        // ================================================================ helpers

        private void CloseModal(GameObject modal)
        {
            if (modal != null) modal.SetActive(false);
        }

        private static IEnumerator LoadImage(RawImage img, string url)
        {
            if (url != null && url.EndsWith(".webp"))
                url = url.Replace("/thumbs/", "/").Replace(".webp", ".png");
            var full = url.StartsWith("http") ? url : NightTaleApi.BaseUrl.TrimEnd('/') + url;
            using (var req = UnityWebRequestTexture.GetTexture(full))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var tex = DownloadHandlerTexture.GetContent(req);
                    img.texture = tex;
                    img.color = Color.white;
                }
            }
        }
    }
}
