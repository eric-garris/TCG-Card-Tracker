using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TCGCardTracker
{
    /// <summary>
    /// Bulk amounts in the Point And Slots point shop, whose cards only have - and + buttons that change the cart by one.
    /// Holding Shift makes a click change it by 10, and Ctrl by 100. The amount between the buttons can also be
    /// clicked and typed into; Enter (or clicking away) puts exactly that many in the cart, and 0 takes the card out.
    /// The shop's own ChangeCartQuantity still does the change, so its sounds, totals and checkout checks all apply.
    /// </summary>
    internal static class ShopQuantity
    {
        private const string Shop = "PromoRewardsMod.Core.PromoShopScreen";
        private const int MaxTyped = 9999;

        private static System.Reflection.MethodInfo _change = null!;
        private static System.Reflection.MethodInfo _get = null!;
        private static bool _exact;

        public static void Install(Harmony harmony)
        {
            try
            {
                var shop = AccessTools.TypeByName(Shop);
                if (shop == null) return;
                _change = AccessTools.Method(shop, "ChangeCartQuantity");
                _get = AccessTools.Method(shop, "GetCartQuantity");
                var create = AccessTools.Method(shop, "CreateCardWidget");
                if (_change == null || _get == null || create == null)
                {
                    Plugin.Log.LogWarning("Point And Slots' shop has changed; bulk amounts are off.");
                    return;
                }
                harmony.Patch(_change, prefix: new HarmonyMethod(typeof(ShopQuantity), nameof(BeforeChange)));
                harmony.Patch(create, postfix: new HarmonyMethod(typeof(ShopQuantity), nameof(AfterCreateWidget)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not add bulk amounts to Point And Slots' shop: " + e.Message);
            }
        }

        /// <summary>Prefix on PromoShopScreen.ChangeCartQuantity: Shift multiplies a click by 10, Ctrl by 100.</summary>
        private static void BeforeChange(ref int delta)
        {
            if (_exact) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) delta *= 100;
            else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) delta *= 10;
        }

        /// <summary>Postfix on PromoShopScreen.CreateCardWidget: lays a number field over the card's amount.</summary>
        private static void AfterCreateWidget(object __instance, object __result)
        {
            try
            {
                var widget = Traverse.Create(__result);
                var label = widget.Field("QuantityPixelLabel").GetValue<Transform>();
                var name = widget.Field("NameText").GetValue<Text>();
                if (label == null) return;

                var box = new GameObject("TrackerTypedAmount", typeof(RectTransform));
                box.transform.SetParent(label, false);
                var rect = (RectTransform)box.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                // Clear until typed into, but still catches the click.
                var background = box.AddComponent<Image>();
                background.color = Color.clear;

                var textObject = new GameObject("Text", typeof(RectTransform));
                textObject.transform.SetParent(box.transform, false);
                var textRect = (RectTransform)textObject.transform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = textRect.offsetMax = Vector2.zero;
                var text = textObject.AddComponent<Text>();
                text.font = name != null && name.font != null ? name.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.fontSize = 18;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.supportRichText = false;

                var field = box.AddComponent<InputField>();
                field.textComponent = text;
                field.contentType = InputField.ContentType.IntegerNumber;
                field.characterLimit = MaxTyped.ToString().Length;
                field.lineType = InputField.LineType.SingleLine;
                field.caretColor = Color.white;
                field.targetGraphic = background;
                box.AddComponent<TypedAmount>().Background = background;

                var shop = __instance;
                field.onEndEdit.AddListener(typed =>
                {
                    field.text = string.Empty;
                    if (!int.TryParse(typed, out var amount)) return;
                    SetAmount(shop, widget.Field("BoundEntry").GetValue(), Mathf.Clamp(amount, 0, MaxTyped));
                });
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pas:typed-amount", "Could not add a typed amount to Point And Slots' shop: " + e.Message);
            }
        }

        private static void SetAmount(object shop, object? entry, int amount)
        {
            if (entry == null) return;
            try
            {
                var current = (int)_get.Invoke(shop, new[] { entry });
                _exact = true;
                _change.Invoke(shop, new object[] { entry, amount - current });
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pas:set-amount", "Could not set the amount in Point And Slots' cart: " + e.Message);
            }
            finally
            {
                _exact = false;
            }
        }

        /// <summary>Covers the shop's own pixel digits while the amount is being typed.</summary>
        private sealed class TypedAmount : MonoBehaviour
        {
            private static readonly Color Panel = new Color(0.13f, 0.16f, 0.2f, 1f);
            public Image Background = null!;
            private InputField? _field;

            private void Update()
            {
                if (_field == null) _field = GetComponent<InputField>();
                Background.color = _field != null && _field.isFocused ? Panel : Color.clear;
            }
        }
    }
}
