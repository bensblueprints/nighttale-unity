using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NightTale
{
    /// <summary>
    /// Entry point. Attach to an empty GameObject in an empty scene, set the API
    /// base URL (defaults to the live backend), and press Play. Everything — canvas,
    /// event system, game picker, story view, paywall — is built at runtime so no
    /// scene wiring is required.
    /// </summary>
    public class NightTaleBootstrap : MonoBehaviour
    {
        [Header("Server")]
        public string apiBaseUrl = "https://play.nighttalegames.com";

        [Header("Optional Unity Ads (leave blank to use backend house ads)")]
        public string unityAdsGameId = "";
        public string unityAdsRewardedPlacementId = "rewardedVideo";

        private Canvas _canvas;
        private RectTransform _root;

        // picker
        private GameObject _picker;

        // story view
        private GameObject _storyView;
        private Text _titleText;
        private Text _turnsText;
        private RawImage _portrait;
        private Text _storyText;
        private RectTransform _choicesPanel;
        private Button _rollButton;
        private Button _backButton;

        // paywall
        private GameObject _paywall;
        private Text _paywallMsg;

        private string _sessionId;
        private bool _busy;

        private void Start()
        {
            NightTaleApi.BaseUrl = apiBaseUrl;
            AdManager.Init(unityAdsGameId, unityAdsRewardedPlacementId);
            BuildUi();
            ShowPicker();
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
            es.transform.SetParent(transform);

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            _root = _canvas.GetComponent<RectTransform>();
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

        private Text Text(string name, Transform parent, string content, int size,
            TextAnchor align = TextAnchor.UpperLeft, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = content; t.fontSize = size; t.alignment = align;
            t.color = color ?? Color.white;
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

        // --------------------------------------------------------------- picker

        private void ShowPicker()
        {
            if (_storyView != null) _storyView.SetActive(false);
            if (_paywall != null) _paywall.SetActive(false);
            if (_picker == null)
            {
                var p = Panel("Picker", new Color(0.03f, 0.03f, 0.06f),
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Text("PickerTitle", p, "NightTale — Choose Your Story", 52, TextAnchor.MiddleCenter)
                    .GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -90, -30, -10);
                var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
                scrollGo.transform.SetParent(p, false);
                var scrollRt = scrollGo.GetComponent<RectTransform>();
                scrollRt.SetAnchor(0, 0, 0, 1, 30, -30, 160, -60);
                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image),
                    typeof(Mask));
                viewport.transform.SetParent(scrollRt, false);
                viewport.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, 0, 0);
                viewport.GetComponent<Image>().color = Color.clear;
                var content = new GameObject("Content", typeof(RectTransform),
                    typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                content.transform.SetParent(viewport.transform, false);
                var crt = content.GetComponent<RectTransform>();
                crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
                crt.pivot = new Vector2(0.5f, 1);
                var vlg = content.GetComponent<VerticalLayoutGroup>();
                vlg.spacing = 20; vlg.padding = new RectOffset(20, 20, 20, 20);
                vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
                var csf = content.GetComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var sr = scrollGo.GetComponent<ScrollRect>();
                sr.viewport = viewport.GetComponent<RectTransform>();
                sr.content = crt; sr.horizontal = false; sr.vertical = true;
                _picker = p.gameObject;

                StartCoroutine(NightTaleApi.GetGames((games, err) =>
                {
                    if (games == null)
                    {
                        Text("Err", content.transform, "Failed to load games: " + err, 36);
                        return;
                    }
                    foreach (var g in games)
                    {
                        if (g.coming_soon) continue;
                        var label = g.title + (g.subtitle != null ? " — " + g.subtitle : "");
                        Button("Game_" + g.slug, content.transform, label,
                            Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -130),
                            () => StartGame(g.slug));
                    }
                }));
            }
            _picker.SetActive(true);
        }

        private void StartGame(string slug)
        {
            // Guest-first: anonymous play gets 12 free turns, then the paywall.
            // (Account login/register UI is a follow-up — swap GuestStart for
            // NightTaleApi.Start once auth is wired.)
            StartCoroutine(NightTaleApi.GuestStart("Wanderer", slug, OnStory));
        }

        // ------------------------------------------------------------ story view

        private void BuildStoryView()
        {
            var p = Panel("StoryView", new Color(0.04f, 0.04f, 0.07f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            _titleText = Text("Title", p, "", 42, TextAnchor.MiddleCenter);
            _titleText.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 30, -30, 20, -80);

            _turnsText = Text("Turns", p, "", 36, TextAnchor.MiddleCenter);
            _turnsText.GetComponent<RectTransform>().SetAnchor(1, 1, 1, 1, -280, -30, 0, -80);

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
            portraitGo.transform.SetParent(p, false);
            var prt = portraitGo.GetComponent<RectTransform>();
            prt.SetAnchor(0, 1, 1, 1, 30, -60, -30, -420);
            _portrait = portraitGo.GetComponent<RawImage>();
            _portrait.color = new Color(0.1f, 0.1f, 0.14f);

            var storyScroll = new GameObject("StoryScroll", typeof(RectTransform), typeof(ScrollRect));
            storyScroll.transform.SetParent(p, false);
            var srt = storyScroll.GetComponent<RectTransform>();
            srt.SetAnchor(0, 0, 1, 1, 30, -760, -30, 140);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(srt, false);
            viewport.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, 0, 0);
            viewport.GetComponent<Image>().color = Color.clear;
            var content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.sizeDelta = new Vector2(0, 0);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = storyScroll.GetComponent<ScrollRect>();
            sr.viewport = viewport.GetComponent<RectTransform>(); sr.content = crt;
            sr.horizontal = false; sr.vertical = true;

            _storyText = Text("Story", crt, "", 38);
            _storyText.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 0, 0, 0, 0);

            _choicesPanel = new GameObject("Choices", typeof(RectTransform),
                typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            _choicesPanel.SetParent(p, false);
            _choicesPanel.SetAnchor(0, 0, 1, 0, 20, -20, 0, 140);
            var vlg = _choicesPanel.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 12; vlg.childControlHeight = true; vlg.childForceExpandHeight = false;

            _rollButton = Button("Roll", p, "Roll", Vector2.zero, Vector2.one,
                new Vector2(30, 0), new Vector2(-30, 0), OnRoll);
            _rollButton.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 0, 20, -20, 60, 140);
            _rollButton.gameObject.SetActive(false);

            _backButton = Button("Back", p, "Back to games", Vector2.zero, Vector2.one,
                new Vector2(30, 0), new Vector2(-30, 0), ShowPicker);
            _backButton.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 0, 20, -20, 0, 60);
            _backButton.gameObject.SetActive(false);

            _storyView = p.gameObject;
        }

        private void OnStory(StoryResponse r)
        {
            if (r == null)
            {
                _storyText.text = "Error talking to the server.";
                return;
            }
            if (!string.IsNullOrEmpty(r.error))
            {
                if (r.error == "coming_soon")
                {
                    ShowPaywall(r.message ?? "This game is coming soon.");
                    return;
                }
                ShowPaywall(r.error);
                return;
            }
            if (_storyView == null) BuildStoryView();
            if (_picker != null) _picker.SetActive(false);
            _paywall?.SetActive(false);
            _storyView.SetActive(true);

            _sessionId = r.session_id ?? _sessionId;
            _titleText.text = r.game != null ? r.game.title : "";
            if (r.state != null)
                _turnsText.text = r.state.free_turns_remaining + " turns";
            _storyText.text = r.story ?? r.raw_story ?? "";
            if (!string.IsNullOrEmpty(r.portrait)) StartCoroutine(LoadImage(_portrait, r.portrait));
            else if (!string.IsNullOrEmpty(r.image)) StartCoroutine(LoadImage(_portrait, r.image));

            RebuildChoices(r);
            if (r.roll_required != null && r.roll_required.roll_required)
                _rollButton.gameObject.SetActive(true);
            else
                _rollButton.gameObject.SetActive(false);

            if (!string.IsNullOrEmpty(r.error) || r.state == null || !r.state.can_play)
                _backButton.gameObject.SetActive(true);
            else
                _backButton.gameObject.SetActive(false);
        }

        private void RebuildChoices(StoryResponse r)
        {
            foreach (Transform c in _choicesPanel) Destroy(c.gameObject);
            if (r.completed)
            {
                var t = Text("End", _choicesPanel, "The End — thanks for playing!", 40,
                    TextAnchor.MiddleCenter);
                t.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 120);
                _backButton.gameObject.SetActive(true);
                return;
            }
            if (r.buttons == null || r.buttons.Count == 0)
            {
                var t = Text("NoChoices", _choicesPanel, "No actions available.", 36);
                t.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 100);
                return;
            }
            foreach (var b in r.buttons)
            {
                var label = b.label;
                if (b.description != null && b.description != b.label)
                    label = b.label + " — " + b.description;
                var go = new GameObject("Choice", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_choicesPanel, false);
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 120);
                go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.28f);
                var txt = Text("ChoiceLabel", go.transform, label, 36, TextAnchor.MiddleCenter);
                txt.GetComponent<RectTransform>().SetAnchor(0, 0, 1, 1, 0, 0, 0, 0);
                var action = b.action;
                go.GetComponent<Button>().onClick.AddListener(() => Choose(action));
            }
        }

        private void Choose(string action)
        {
            if (_busy) return;
            _busy = true;
            StartCoroutine(NightTaleApi.GuestAction(_sessionId, action, r =>
            {
                _busy = false;
                OnStory(r);
            }));
        }

        private void OnRoll()
        {
            if (_busy) return;
            _busy = true;
            StartCoroutine(NightTaleApi.GuestRoll(_sessionId, r =>
            {
                _busy = false;
                OnStory(r);
            }));
        }

        // ---------------------------------------------------------------- paywall

        private void ShowPaywall(string msg)
        {
            if (_paywall == null)
            {
                var p = Panel("Paywall", new Color(0, 0, 0, 0.9f),
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                _paywallMsg = Text("PaywallMsg", p, "", 40, TextAnchor.MiddleCenter);
                _paywallMsg.GetComponent<RectTransform>().SetAnchor(0, 1, 1, 1, 40, -40, -500, -700);
                Button("WatchAd", p, "Watch Ad for +5 Turns", Vector2.zero, Vector2.one,
                    new Vector2(80, 0), new Vector2(-80, 0), WatchAd)
                    .GetComponent<RectTransform>().SetAnchor(0.5f, 0.5f, 0.5f, 0.5f,
                    new Vector2(-400, 200), new Vector2(400, 360));
                Button("Web", p, "Get Turns at nighttalegames.com", Vector2.zero, Vector2.one,
                    new Vector2(80, 0), new Vector2(-80, 0), () => Application.OpenURL("https://nighttalegames.com"))
                    .GetComponent<RectTransform>().SetAnchor(0.5f, 0.5f, 0.5f, 0.5f,
                    new Vector2(-400, -40), new Vector2(400, 120));
                Button("BackFromPaywall", p, "Back to games", Vector2.zero, Vector2.one,
                    new Vector2(80, 0), new Vector2(-80, 0), ShowPicker)
                    .GetComponent<RectTransform>().SetAnchor(0.5f, 0.5f, 0.5f, 0.5f,
                    new Vector2(-400, -280), new Vector2(400, -120));
                _paywall = p.gameObject;
            }
            _paywallMsg.text = msg;
            _paywall.SetActive(true);
        }

        private void WatchAd()
        {
            AdManager.ShowRewarded((rewarded) =>
            {
                if (!rewarded) return; // ad skipped/failed — no reward
                // The backend is the source of truth: claim the slot only after full watch.
                StartCoroutine(NightTaleApi.AdSlot(slot =>
                {
                    if (slot == null || slot.slot_id == null) return;
                    StartCoroutine(NightTaleApi.AdComplete(slot.slot_id, state =>
                    {
                        _paywall.SetActive(false);
                        if (state != null) _turnsText.text = state.free_turns_remaining + " turns";
                        if (_storyView != null) _storyView.SetActive(true);
                    }));
                }));
            });
        }

        private static IEnumerator LoadImage(RawImage img, string url)
        {
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
