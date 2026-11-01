using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// Puts TCG Studio's pack and box icons back on shelf labels and on the labels of delivery boxes in the game's
    /// Unity 6 build (1.03+).
    ///
    /// Since 1.03 a label (UI_PriceTag) no longer shows the item's ItemData.icon sprite. One shader draws the whole
    /// label from a prebaked atlas: digits, currency signs and every vanilla item's icon are cells of it, and the
    /// label's PriceTagAtlasMap gives the cell for an item type (itemTypeValues / itemCellIndices). TCG Studio's items
    /// are new item types that map has never heard of, so the cell is -1 and the label shows no icon at all.
    ///
    /// When the atlas is a Texture2DArray (one cell per layer), this builds a copy with one extra layer per custom item
    /// holding the item's own icon (the sprite TCG Studio gave its ItemData), puts the copy on the label materials and
    /// adds the new cells to the atlas map, so the game's own SetItemImage finds them. If the atlas turns out to be
    /// laid out some other way, custom packs and boxes get the vanilla basic pack and box icon instead of nothing.
    /// Either way the materials found are logged once.
    ///
    /// WORKAROUND (added 2026-11-01, tracker 0.15.2, TCG Studio 1.14.0, game 1.03), part of the FixPackSkins workaround
    /// (see PackSkins): delete this file and its Install() call in Plugin.Awake once a TCG Studio release shows its
    /// icons on shelf labels by itself on 1.03. FixPackSkins = false turns it off too.
    /// </summary>
    internal static class PriceTagIcons
    {
        private const string Registry = "TCGCustomCards.Runtime.Registry";

        private static Type? _tagType, _mapType;
        private static Func<EItemType, bool>? _isCustom, _isPack, _isBox;
        private static FieldInfo? _atlasMap, _renderer, _itemValues, _itemCells, _itemCache, _objectCells, _digits;

        /// <summary>Atlas maps already given the custom cells.</summary>
        private static readonly HashSet<UnityEngine.Object> Extended = new HashSet<UnityEngine.Object>();
        /// <summary>Label materials already checked.</summary>
        private static readonly HashSet<Material> Done = new HashSet<Material>();
        /// <summary>Extended atlas copies by vanilla atlas.</summary>
        private static readonly Dictionary<Texture, Texture> Copies = new Dictionary<Texture, Texture>();

        /// <summary>Custom items with an icon, in the order of their extra layers.</summary>
        private static List<(EItemType item, Sprite icon)> _items = new List<(EItemType, Sprite)>();
        private static int _builtForCount = -1;
        /// <summary>Layers in the vanilla atlas (first extra layer), or -1 before the first array is seen.</summary>
        private static int _depth = -1;
        /// <summary>Whether the label atlas is a texture array we could extend (null before the first label).</summary>
        private static bool? _arrayAtlas;

        public static void Install()
        {
            try
            {
                var registry = AccessTools.TypeByName(Registry);
                if (registry == null) return; // TCG Studio isn't installed: nothing to do.
                _isCustom = Check(registry, "IsCustomItem");
                _isPack = Check(registry, "IsCustomPackItem");
                _isBox = Check(registry, "IsCustomBoxItem");
                _tagType = AccessTools.TypeByName("UI_PriceTag");
                _mapType = AccessTools.TypeByName("PriceTagAtlasMap");
                if (_tagType == null || _mapType == null) return; // a game version that still uses icon sprites on labels
                if (!Plugin.FixPackSkins.Value)
                {
                    Plugin.Log.LogInfo("TCG Studio label icons: FixPackSkins is off, left as TCG Studio made them.");
                    return;
                }
                _atlasMap = AccessTools.Field(_tagType, "m_AtlasMap");
                _renderer = AccessTools.Field(_tagType, "m_InstancedRenderer");
                _itemValues = AccessTools.Field(_mapType, "itemTypeValues");
                _itemCells = AccessTools.Field(_mapType, "itemCellIndices");
                _itemCache = AccessTools.Field(_mapType, "m_ItemCache");
                _objectCells = AccessTools.Field(_mapType, "objectCellIndices");
                _digits = AccessTools.Field(_mapType, "digitIndex");
                var target = AccessTools.Method(_tagType, "SetItemImage");
                if (_isCustom == null)
                {
                    Plugin.Log.LogWarning("TCG Studio label icons: could not read TCG Studio's item list (a newer TCG Studio?); icons are left as they are.");
                    return;
                }
                if (target == null || _atlasMap == null || _renderer == null || _itemValues == null || _itemCells == null)
                {
                    Plugin.Log.LogWarning("TCG Studio label icons: the game's price labels changed; icons are left as they are.");
                    return;
                }
                new Harmony(Plugin.Guid).Patch(target, prefix: new HarmonyMethod(typeof(PriceTagIcons), nameof(BeforeSetItemImage)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook the game's price labels; TCG Studio icons are left as they are: " + e.Message);
            }
        }

        /// <summary>Harmony prefix on UI_PriceTag.SetItemImage: make sure this label's atlas knows the custom items first.</summary>
        private static void BeforeSetItemImage(object __instance, EItemType itemType)
        {
            try
            {
                if (!_isCustom!(itemType)) return; // vanilla items are in the atlas already
                var map = _atlasMap!.GetValue(__instance) as UnityEngine.Object;
                var renderer = _renderer!.GetValue(__instance) as Renderer;
                if (map == null) return;
                Prepare(map, renderer != null ? renderer.sharedMaterial : null);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("label-icons", "Could not add TCG Studio's icons to a price label: " + e);
            }
        }

        private static void Prepare(UnityEngine.Object map, Material? material)
        {
            CollectItems();
            if (material != null && !Done.Contains(material))
            {
                Done.Add(material);
                ExtendMaterial(map, material);
            }
            if (_arrayAtlas == null) return; // no material seen yet: nothing to point cells at
            if (Extended.Contains(map)) return;
            Extended.Add(map);
            ExtendMap(map);
        }

        /// <summary>TCG Studio's items that have an icon sprite.</summary>
        private static void CollectItems()
        {
            // Once: TCG Studio has added its items before any label shows one, and the atlas layers keep this order.
            if (_builtForCount >= 0) return;
            var inv = CSingleton<InventoryBase>.Instance;
            if (inv == null || inv.m_StockItemData_SO == null || inv.m_StockItemData_SO.m_ItemDataList == null) return;
            var list = inv.m_StockItemData_SO.m_ItemDataList;
            _builtForCount = list.Count;
            _items = new List<(EItemType, Sprite)>();
            for (int i = 0; i < list.Count; i++)
            {
                var icon = list[i]?.icon;
                if (icon != null && _isCustom!((EItemType)i)) _items.Add(((EItemType)i, icon));
            }
        }

        /// <summary>Swaps the material's atlas array for a copy with the custom icons as extra layers, if it is one.</summary>
        private static void ExtendMaterial(UnityEngine.Object map, Material material)
        {
            int maxCell = MaxCell(map);
            Plugin.Log.LogInfo($"Price label material: {Describe(material)}; atlas cells used by the game up to {maxCell}; {_items.Count} custom item icon(s).");
            bool any = false;
            foreach (var prop in material.GetTexturePropertyNames())
            {
                if (!(material.GetTexture(prop) is Texture2DArray src) || src.depth <= maxCell) continue;
                if (_depth >= 0 && src.depth != _depth)
                {
                    Plugin.LogOnce("label-depth-" + src.name, $"Price label atlas {src.name} has {src.depth} layers, another had {_depth}; left vanilla.");
                    continue;
                }
                if (!Copies.TryGetValue(src, out var copy))
                {
                    var built = Build(src);
                    if (built == null) continue;
                    Copies[src] = copy = built;
                }
                _depth = src.depth;
                material.SetTexture(prop, copy);
                any = true;
            }
            if (any)
            {
                _arrayAtlas = true;
                Plugin.Log.LogInfo($"TCG Studio label icons: {_items.Count} custom icon(s) added to the price label atlas of {material.name}.");
            }
            else if (_arrayAtlas == null)
            {
                _arrayAtlas = false;
                Plugin.Log.LogInfo("TCG Studio label icons: the label atlas is not a texture array; custom packs and boxes get the basic pack and box icons.");
            }
        }

        /// <summary>A render-texture array: the vanilla layers, then one layer per custom icon. Null if the copy fails.</summary>
        private static Texture? Build(Texture2DArray src)
        {
            try
            {
                bool mips = src.mipmapCount > 1;
                var rt = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "TCGCT_" + src.name,
                    dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                    volumeDepth = src.depth + _items.Count,
                    useMipMap = mips,
                    autoGenerateMips = false,
                    wrapMode = src.wrapMode,
                    filterMode = src.filterMode,
                    anisoLevel = src.anisoLevel,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                rt.Create();
                for (int i = 0; i < src.depth; i++) Graphics.Blit(src, rt, i, i);
                for (int k = 0; k < _items.Count; k++)
                {
                    var icon = _items[k].icon;
                    var tex = icon.texture;
                    var r = icon.textureRect;
                    // Blit reads source uv = target uv * scale + offset: map the whole layer onto the sprite's rect.
                    var scale = new Vector2(r.width / tex.width, r.height / tex.height);
                    var offset = new Vector2(r.x / tex.width, r.y / tex.height);
                    Graphics.Blit(tex, rt, scale, offset, 0, src.depth + k);
                }
                if (mips) rt.GenerateMips();
                return rt;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("label-build-" + src.name, $"Could not copy the price label atlas {src.name}: {e.Message}");
                return null;
            }
        }

        /// <summary>Adds each custom item to the atlas map: its own layer, or the basic pack / box icon.</summary>
        private static void ExtendMap(UnityEngine.Object map)
        {
            var values = (_itemValues!.GetValue(map) as int[] ?? new int[0]).ToList();
            var cells = (_itemCells!.GetValue(map) as int[] ?? new int[0]).ToList();
            int n = Math.Min(values.Count, cells.Count);
            values = values.Take(n).ToList();
            cells = cells.Take(n).ToList();
            int Cell(EItemType t) { int i = values.IndexOf((int)t); return i >= 0 ? cells[i] : -1; }
            int packCell = Cell(EItemType.BasicCardPack), boxCell = Cell(EItemType.BasicCardBox);

            int added = 0;
            for (int k = 0; k < _items.Count; k++)
            {
                var item = _items[k].item;
                int cell;
                if (_arrayAtlas == true && _depth >= 0) cell = _depth + k;
                else
                {
                    cell = _isBox?.Invoke(item) == true ? boxCell : _isPack?.Invoke(item) == true ? packCell : -1;
                }
                if (cell < 0) continue;
                int at = values.IndexOf((int)item);
                if (at >= 0) cells[at] = cell;
                else { values.Add((int)item); cells.Add(cell); }
                added++;
            }
            _itemValues.SetValue(map, values.ToArray());
            _itemCells.SetValue(map, cells.ToArray());
            _itemCache?.SetValue(map, null); // rebuilt from the arrays on the next lookup
            Plugin.Log.LogInfo($"TCG Studio label icons: {added} custom item(s) now have a label icon.");
        }

        /// <summary>A TCG Studio Registry check such as IsCustomItem(EItemType), or null if it isn't there.</summary>
        private static Func<EItemType, bool>? Check(Type registry, string name)
        {
            var m = AccessTools.Method(registry, name, new[] { typeof(EItemType) });
            return m != null && m.ReturnType == typeof(bool) ? (Func<EItemType, bool>)Delegate.CreateDelegate(typeof(Func<EItemType, bool>), m) : null;
        }

        /// <summary>The highest atlas cell the game's map names (digits, item and object icons), so a font texture isn't mistaken for the atlas.</summary>
        private static int MaxCell(UnityEngine.Object map)
        {
            int max = -1;
            foreach (var f in new[] { _itemCells, _objectCells, _digits })
                if (f?.GetValue(map) is int[] a && a.Length > 0) max = Math.Max(max, a.Max());
            return max;
        }

        private static string Describe(Material m)
        {
            var slots = m.GetTexturePropertyNames()
                .Select(p => (p, t: m.GetTexture(p)))
                .Where(x => x.t != null)
                .Select(x => $"{x.p}={x.t.name} {x.t.width}x{x.t.height}" +
                             (x.t is Texture2DArray a ? $"x{a.depth} {a.format} mips={a.mipmapCount}" : x.t is Texture2D t2 ? $" {t2.format}" : ""));
            return $"{m.name} shader={(m.shader != null ? m.shader.name : "none")} [{string.Join(", ", slots)}]";
        }
    }
}
