using System;
using System.Collections.Generic;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// Banners on the trade screen: above the customer's card (left, m_CardData_L) and, in a trade, above the
    /// card they want from you (right, m_CardData_R), saying how many copies you own, ungraded and at each grade.
    /// A postfix on CustomerTradeCardScreen.SetCustomer reads both cards; the banners stay up until the screen
    /// closes or the next customer comes. Fields are read by name, so a renamed one is just skipped, and any error is logged while the
    /// game carries on.
    /// </summary>
    internal static class TradeBanner
    {
        private static readonly Banner Left = new Banner();
        private static readonly Banner Right = new Banner();
        private static CustomerTradeCardScreen? _screen;
        private static bool _open;
        private static GUIStyle? _box, _titleStyle, _cardStyle, _ownedStyle, _noneStyle, _lineStyle, _newStyle;

        private sealed class Banner
        {
            public bool Shown;
            public string Title = "";
            public string Card = "";
            public string? Note;
            public bool OwnsNone;
            public readonly List<string> Lines = new List<string>();
            public Component? Anchor;
        }

        public static void Install()
        {
            Harmony harmony;
            try
            {
                harmony = new Harmony(Plugin.Guid);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not start Harmony; the trade banners are off: " + e.Message);
                return;
            }
            PackWatcher.Patch(harmony, typeof(CustomerTradeCardScreen), "SetCustomer", typeof(TradeBanner), nameof(AfterOffer), prefix: false, "the trade banners", "trade offers");
            PackWatcher.Patch(harmony, typeof(CustomerTradeCardScreen), "OnCloseScreen", typeof(TradeBanner), nameof(AfterClose), prefix: false, "the trade banners", "closing the trade screen");
        }

        public static void Reset()
        {
            Left.Shown = Right.Shown = false;
            _screen = null;
            _open = false;
        }

        /// <summary>Harmony postfix on the trade screen opening for a customer, to trade or to sell.</summary>
        private static void AfterOffer(CustomerTradeCardScreen __instance, object[] __args)
        {
            try
            {
                Reset();
                if (!Plugin.TradeBanners.Value || !GameReader.IsReady()) return;
                object source = __instance;
                foreach (var a in __args)
                    if (a is CustomerTradeData) source = a;
                var offer = Traverse.Create(source);
                var offered = offer.Field("m_CardData_L").GetValue() as CardData;
                var wanted = offer.Field("m_CardData_R").GetValue() as CardData;
                bool trading = offer.Field("m_IsTrading").GetValue() as bool? ?? true;

                var owned = PackWatcher.Snapshot(out var catalog);
                var screen = Traverse.Create(__instance);
                Fill(Left, catalog, owned, offered, trading ? "Customer offers" : "Customer is selling", true, screen.Field("m_CardUI_L").GetValue() as Component);
                if (trading)
                    Fill(Right, catalog, owned, wanted, "Customer wants", false, screen.Field("m_CardUI_R").GetValue() as Component);
                if (Left.Shown || Right.Shown)
                {
                    _screen = __instance;
                    _open = true;
                }
            }
            catch (Exception e)
            {
                Reset();
                Plugin.LogOnce("trade-offer", "Trade banner could not check a trade offer: " + e);
            }
        }

        private static void AfterClose() => Reset();

        private static void Fill(Banner b, Catalog catalog, int[] owned, CardData? data, string title, bool offered, Component? anchor)
        {
            if (data == null || (int)data.monsterType <= 0) return;
            var card = catalog.FromPhysical((int)data.expansionType, (int)data.monsterType, (int)data.borderType, data.isFoil, data.isDestiny);
            if (card == null) return;
            int col = Collection.ColumnOf(data.cardGrade);
            var holding = CardQueries.HoldingOf(owned, card);
            b.Title = title;
            b.Card = $"{card.RarityLabel}  ({card.Set.Name})" + (col > 0 ? $"  {new Column(col).LongLabel}" : "");
            b.Lines.Clear();
            b.Lines.AddRange(holding.Lines());
            b.OwnsNone = holding.Copies == 0;
            b.Note = null;
            if (offered)
            {
                var gain = CardQueries.GainFrom(owned, card, col);
                if (gain != null) b.Note = gain.Value.IsNew ? "New for your collection" : "Better grade than your best copy";
            }
            b.Anchor = anchor;
            b.Shown = true;
            Plugin.Log.LogInfo($"{title}: {card.Label}; {string.Join(", ", b.Lines)}");
        }

        /// <summary>
        /// Called every frame: hides the banners while the trade screen isn't showing (e.g. the price chart is
        /// open over it). They go for good when it closes or the next customer arrives.
        /// </summary>
        public static void Tick()
        {
            if (_screen == null) return;
            try
            {
                _open = _screen.gameObject.activeInHierarchy
                    && (Traverse.Create(_screen).Field("m_IsScreenOpen").GetValue() as bool? ?? true);
            }
            catch
            {
                Reset();
            }
        }

        public static void Draw()
        {
            if (_screen == null || !_open || !Plugin.TradeBanners.Value) return;
            var saved = GUI.matrix;
            try
            {
                Ensure();
                float scale = Mathf.Clamp(Plugin.PopupScale.Value, 0.5f, 3f) * Mathf.Max(1f, Screen.height / 1080f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                float sw = Screen.width / scale;
                if (Left.Shown) DrawBanner(Left, scale, sw, sw * 0.3f);
                if (Right.Shown) DrawBanner(Right, scale, sw, sw * 0.7f);
            }
            catch (Exception e)
            {
                Reset();
                Plugin.LogOnce("banner-draw", "Trade banner failed to draw: " + e);
            }
            finally
            {
                GUI.matrix = saved;
            }
        }

        private static void DrawBanner(Banner b, float scale, float sw, float fallbackX)
        {
            const float pad = 16f, titleH = 32f, cardH = 28f, lineH = 26f, gap = 12f;
            var lines = new List<(string text, GUIStyle style)> { (b.Card, _cardStyle!) };
            if (b.Note != null) lines.Add((b.Note, _newStyle!));
            for (int i = 0; i < b.Lines.Count; i++)
                lines.Add((b.Lines[i], i == 0 ? (b.OwnsNone ? _noneStyle! : _ownedStyle!) : _lineStyle!));

            float w = _titleStyle!.CalcSize(new GUIContent(b.Title)).x;
            foreach (var l in lines) w = Mathf.Max(w, l.style.CalcSize(new GUIContent(l.text)).x);
            w = Mathf.Min(w + pad * 2f, sw / 2f - 20f);
            float h = pad + titleH + cardH + lineH * (lines.Count - 1) + pad;

            // Centred above the card; at the top of the screen when the card can't be found.
            float cx = fallbackX, top = 20f;
            var card = CardRect(b.Anchor);
            if (card != null)
            {
                var r0 = card.Value;
                cx = (r0.xMin + r0.xMax) / 2f / scale;
                top = r0.yMin / scale - gap - h;
            }
            float x = Mathf.Clamp(cx - w / 2f, 8f, sw - w - 8f);
            var r = new Rect(x, Mathf.Max(8f, top), w, h);

            GUI.Box(r, GUIContent.none, _box);
            float y = r.y + pad;
            GUI.Label(new Rect(r.x + pad, y, w - pad * 2f, titleH), b.Title, _titleStyle);
            y += titleH;
            for (int i = 0; i < lines.Count; i++)
            {
                float lh = i == 0 ? cardH : lineH;
                GUI.Label(new Rect(r.x + pad, y, w - pad * 2f, lh), lines[i].text, lines[i].style);
                y += lh;
            }
        }

        /// <summary>A card's on-screen rectangle in GUI pixels (top-left origin), or null when it can't be placed.</summary>
        private static Rect? CardRect(Component? anchor)
        {
            if (anchor == null || !anchor.gameObject.activeInHierarchy) return null;
            if (!(anchor.transform is RectTransform rt)) return null;
            var canvas = anchor.GetComponentInParent<Canvas>()?.rootCanvas;
            var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : (canvas.worldCamera != null ? canvas.worldCamera : Camera.main);
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var c in corners)
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, c);
                xMin = Mathf.Min(xMin, p.x);
                xMax = Mathf.Max(xMax, p.x);
                yMin = Mathf.Min(yMin, p.y);
                yMax = Mathf.Max(yMax, p.y);
            }
            if (xMax - xMin < 10f || yMax - yMin < 10f || xMax < 0f || xMin > Screen.width) return null;
            return Rect.MinMaxRect(xMin, Screen.height - yMax, xMax, Screen.height - yMin);
        }

        private static void Ensure()
        {
            if (_box != null) return;
            var skin = GUI.skin;
            _box = new GUIStyle(skin.box);
            var bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            bg.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.12f, 0.92f));
            bg.Apply();
            _box.normal.background = bg;
            _titleStyle = Style(skin, 22, FontStyle.Bold, new Color(1f, 0.84f, 0.35f));
            _cardStyle = Style(skin, 19, FontStyle.Bold, Color.white);
            _newStyle = Style(skin, 18, FontStyle.Bold, new Color(0.45f, 0.8f, 1f));
            _ownedStyle = Style(skin, 18, FontStyle.Bold, new Color(0.5f, 0.9f, 0.5f));
            _noneStyle = Style(skin, 18, FontStyle.Bold, new Color(1f, 0.55f, 0.45f));
            _lineStyle = Style(skin, 18, FontStyle.Normal, new Color(0.88f, 0.88f, 0.88f));
        }

        private static GUIStyle Style(GUISkin skin, int size, FontStyle font, Color color)
        {
            var s = new GUIStyle(skin.label) { fontSize = size, fontStyle = font, wordWrap = false, clipping = TextClipping.Clip };
            s.normal.textColor = color;
            return s;
        }
    }
}
