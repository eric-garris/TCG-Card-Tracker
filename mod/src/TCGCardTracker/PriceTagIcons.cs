using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace TCGCardTracker
{
    /// <summary>
    /// Puts TCG Studio's pack and box icons back on shelf labels and on the labels of delivery boxes in the game's
    /// Unity 6 build (1.03+).
    ///
    /// Since 1.03 a label (UI_PriceTag) no longer shows the item's ItemData.icon sprite. One shader
    /// (Sprites/PriceTagInstanced, material Mat_PriceTagInstanced) draws the whole label from one prebaked atlas
    /// picture (PriceTagAtlas, 4096 x 4096, a grid of equal cells): digits, currency signs and every vanilla item's
    /// icon are cells of it, and the label's PriceTagAtlasMap gives the cell number for an item type
    /// (itemTypeValues / itemCellIndices). TCG Studio's items are new item types the map has never heard of, so the
    /// cell is -1 and the label shows no icon at all.
    ///
    /// The atlas grid isn't stored anywhere the game exposes, so it is found by matching: the atlas and a few vanilla
    /// item icons are read back small, and every grid size and row order is tried until the cells the map names look
    /// like those icons. Then a copy of the atlas gets each TCG Studio icon painted into a cell that no map entry uses
    /// and that is empty in the picture, the copy goes on the label material, and the new cells go into the map, so
    /// the game's own SetItemImage finds them. Packs get cells first, then boxes; a box left without a cell shows its
    /// set's pack icon. If no grid matches, custom packs and boxes show the basic pack and box icon instead of nothing.
    /// The material, the grid found and a small copy of the atlas (BepInEx\TCGCardTracker-dump\PriceTagAtlas.png) are
    /// written once for checking.
    ///
    /// WORKAROUND (added 2026-11-01, tracker 0.15.2, TCG Studio 1.14.0, game 1.03), part of the FixPackSkins workaround
    /// (see PackSkins): delete this file and its Install() call in Plugin.Awake once a TCG Studio release shows its
    /// icons on shelf labels by itself on 1.03. FixPackSkins = false turns it off too.
    /// </summary>
    internal static class PriceTagIcons
    {
        private const string Registry = "TCGCustomCards.Runtime.Registry";
        /// <summary>Side of the small read-back copy of the atlas used to find the grid.</summary>
        private const int Probe = 1024;
        /// <summary>Samples per cell side when comparing a cell with an icon.</summary>
        private const int Grain = 16;

        private static Type? _tagType, _mapType;
        private static Func<EItemType, bool>? _isCustom, _isPack, _isBox;
        private static FieldInfo? _atlasMap, _renderer, _itemValues, _itemCells, _itemCache, _objectCells, _digits;

        /// <summary>Atlas maps already given the custom cells.</summary>
        private static readonly HashSet<UnityEngine.Object> Extended = new HashSet<UnityEngine.Object>();
        /// <summary>Label materials already checked.</summary>
        private static readonly HashSet<Material> Done = new HashSet<Material>();
        /// <summary>Atlas copies with the custom icons, by vanilla atlas.</summary>
        private static readonly Dictionary<Texture, Texture> Copies = new Dictionary<Texture, Texture>();

        /// <summary>TCG Studio's items with an icon.</summary>
        private static List<(EItemType item, Sprite icon)> _items = new List<(EItemType, Sprite)>();
        private static bool _collected;
        /// <summary>Atlas cell for each custom item that got its own icon (null until the first label material is checked).</summary>
        private static Dictionary<EItemType, int>? _cells;

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
                CollectItems();
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material != null && Done.Add(material)) ExtendMaterial(map, material);
                if (_cells == null || !Extended.Add(map)) return;
                ExtendMap(map);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("label-icons", "Could not add TCG Studio's icons to a price label: " + e);
            }
        }

        /// <summary>TCG Studio's items that have an icon sprite (once: TCG Studio adds its items before any label shows one).</summary>
        private static void CollectItems()
        {
            if (_collected) return;
            var inv = CSingleton<InventoryBase>.Instance;
            if (inv == null || inv.m_StockItemData_SO == null || inv.m_StockItemData_SO.m_ItemDataList == null) return;
            _collected = true;
            var list = inv.m_StockItemData_SO.m_ItemDataList;
            for (int i = 0; i < list.Count; i++)
            {
                var icon = list[i]?.icon;
                if (icon != null && _isCustom!((EItemType)i)) _items.Add(((EItemType)i, icon));
            }
        }

        /// <summary>Finds the atlas grid, paints the custom icons into free cells of a copy and puts it on the material.</summary>
        private static void ExtendMaterial(UnityEngine.Object map, Material material)
        {
            var src = material.mainTexture as Texture2D;
            Plugin.Log.LogInfo($"Price label material: {Describe(material)}; cells named by the label map: {Used(map).Count} (highest {Used(map).DefaultIfEmpty(-1).Max()}); " +
                               $"cellAdvance entries: {(AccessTools.Field(_mapType, "cellAdvance")?.GetValue(map) as float[])?.Length ?? -1}; {_items.Count} custom item icon(s).");
            if (_cells != null) return; // a second label material: the cells are already chosen
            _cells = new Dictionary<EItemType, int>();
            if (src == null)
            {
                Plugin.Log.LogInfo("TCG Studio label icons: the label atlas is not a plain picture; custom packs and boxes get the basic pack and box icons.");
                return;
            }
            if (Copies.TryGetValue(src, out var done)) { material.mainTexture = done; return; }

            var atlas = ReadBack(src, new Rect(0, 0, 1, 1), Probe, Probe);
            Dump(atlas);
            var grid = FindGrid(map, atlas);
            if (grid == null)
            {
                Plugin.Log.LogInfo("TCG Studio label icons: could not work out the label atlas grid; custom packs and boxes get the basic pack and box icons.");
                return;
            }
            var (cols, rows, top) = grid.Value;

            var used = Used(map);
            var free = new Queue<int>(Enumerable.Range(0, cols * rows).Where(c => !used.Contains(c) && Empty(atlas, Cell(c, cols, rows, top, Probe, Probe))));
            int total = free.Count;
            // Packs first, then boxes, then anything else TCG Studio added (accessories).
            foreach (var (item, _) in _items.OrderBy(x => _isPack?.Invoke(x.item) == true ? 0 : _isBox?.Invoke(x.item) == true ? 1 : 2))
            {
                if (free.Count == 0) break;
                _cells[item] = free.Dequeue();
            }
            Plugin.Log.LogInfo($"TCG Studio label icons: label atlas is a {cols} x {rows} grid ({(top ? "rows from the top" : "rows from the bottom")}); " +
                               $"{total} free cell(s), {_cells.Count} of {_items.Count} custom icon(s) placed.");
            if (_cells.Count == 0) return;

            var copy = Paint(src, cols, rows, top);
            if (copy == null) { _cells.Clear(); return; }
            Copies[src] = copy;
            material.mainTexture = copy;
        }

        /// <summary>The atlas as a render texture with each placed custom icon copied into its cell. Null if that fails.</summary>
        private static Texture? Paint(Texture2D src, int cols, int rows, bool top)
        {
            try
            {
                bool mips = src.mipmapCount > 1;
                var rt = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "TCGCT_" + src.name,
                    useMipMap = mips,
                    autoGenerateMips = false,
                    wrapMode = src.wrapMode,
                    filterMode = src.filterMode,
                    anisoLevel = src.anisoLevel,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                rt.Create();
                Graphics.Blit(src, rt);
                foreach (var (item, icon) in _items)
                {
                    if (!_cells!.TryGetValue(item, out int cell)) continue;
                    var r = Cell(cell, cols, rows, top, src.width, src.height);
                    var tmp = RenderTexture.GetTemporary(r.w, r.h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(icon.texture, tmp, Scale(icon), Offset(icon));
                    Graphics.CopyTexture(tmp, 0, 0, 0, 0, r.w, r.h, rt, 0, 0, r.x, r.y);
                    RenderTexture.ReleaseTemporary(tmp);
                }
                if (mips) rt.GenerateMips();
                return rt;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("label-paint", $"Could not copy the price label atlas {src.name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Grid (columns, rows, whether cell 0 is at the top) under which the map's vanilla item cells best match those
        /// items' icon sprites, or null if none matches well.
        /// </summary>
        private static (int cols, int rows, bool top)? FindGrid(UnityEngine.Object map, Color32[] atlas)
        {
            var values = _itemValues!.GetValue(map) as int[] ?? new int[0];
            var cells = _itemCells!.GetValue(map) as int[] ?? new int[0];
            var refs = new List<(int cell, Color32[] px)>();
            for (int i = 0; i < Math.Min(values.Length, cells.Length) && refs.Count < 8; i++)
            {
                if (cells[i] < 0 || _isCustom!((EItemType)values[i])) continue;
                var icon = InventoryBase.GetItemData((EItemType)values[i])?.icon;
                if (icon == null) continue;
                refs.Add((cells[i], ReadBack(icon.texture, new Rect(Offset(icon), Scale(icon)), Grain, Grain)));
            }
            if (refs.Count == 0) return null;
            int need = refs.Max(r => r.cell) + 1;

            var results = new List<(double err, int cols, int rows, bool top)>();
            for (int cols = 2; cols <= 64; cols++)
                for (int rows = 2; rows <= 64; rows++)
                {
                    if (cols * rows < need) continue;
                    foreach (bool top in new[] { true, false })
                    {
                        double err = 0;
                        foreach (var (cell, px) in refs) err += Difference(atlas, Cell(cell, cols, rows, top, Probe, Probe), px);
                        results.Add((err / refs.Count, cols, rows, top));
                    }
                }
            results.Sort((a, b) => a.err.CompareTo(b.err));
            Plugin.Log.LogInfo("TCG Studio label icons: best atlas grids (error, columns x rows, order): " +
                               string.Join("; ", results.Take(5).Select(r => $"{r.err:F4} {r.cols}x{r.rows} {(r.top ? "top" : "bottom")}")) +
                               $" (median {results[results.Count / 2].err:F4}, {refs.Count} reference icon(s)).");
            var best = results[0];
            // A real match is close to the icons and clearly better than the typical wrong grid.
            if (best.err > 0.03 || best.err > results[results.Count / 2].err * 0.4) return null;
            return (best.cols, best.rows, best.top);
        }

        /// <summary>Mean squared difference (0 - 1) between a cell of the probe image and a Grain x Grain icon.</summary>
        private static double Difference(Color32[] atlas, (int x, int y, int w, int h) r, Color32[] icon)
        {
            double sum = 0;
            for (int j = 0; j < Grain; j++)
                for (int i = 0; i < Grain; i++)
                {
                    int x = r.x + (int)((i + 0.5f) * r.w / Grain), y = r.y + (int)((j + 0.5f) * r.h / Grain);
                    var a = atlas[Math.Min(y, Probe - 1) * Probe + Math.Min(x, Probe - 1)];
                    var b = icon[j * Grain + i];
                    // Colour counts only where either is visible; transparent pixels may hold any colour.
                    float wa = a.a / 255f, wb = b.a / 255f;
                    double dr = (a.r * wa - b.r * wb) / 255.0, dg = (a.g * wa - b.g * wb) / 255.0, db = (a.b * wa - b.b * wb) / 255.0, da = wa - wb;
                    sum += (dr * dr + dg * dg + db * db + da * da) / 4;
                }
            return sum / (Grain * Grain);
        }

        /// <summary>Whether a cell of the probe image is (almost) fully transparent.</summary>
        private static bool Empty(Color32[] atlas, (int x, int y, int w, int h) r)
        {
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                    if (atlas[Math.Min(y, Probe - 1) * Probe + Math.Min(x, Probe - 1)].a > 8) return false;
            return true;
        }

        /// <summary>Pixel rectangle (bottom-left origin) of a cell in a w x h picture.</summary>
        private static (int x, int y, int w, int h) Cell(int cell, int cols, int rows, bool top, int w, int h)
        {
            int col = cell % cols, row = cell / cols;
            if (top) row = rows - 1 - row;
            int x0 = col * w / cols, x1 = (col + 1) * w / cols, y0 = row * h / rows, y1 = (row + 1) * h / rows;
            return (x0, y0, x1 - x0, y1 - y0);
        }

        private static Vector2 Scale(Sprite s) => new Vector2(s.textureRect.width / s.texture.width, s.textureRect.height / s.texture.height);
        private static Vector2 Offset(Sprite s) => new Vector2(s.textureRect.x / s.texture.width, s.textureRect.y / s.texture.height);

        /// <summary>Reads part of any texture (uv rect) back as w x h pixels, bottom row first (GPU copy, so non-readable textures work).</summary>
        private static Color32[] ReadBack(Texture tex, Rect uv, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt, uv.size, uv.position);
                RenderTexture.active = rt;
                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply(false);
                var px = read.GetPixels32();
                UnityEngine.Object.Destroy(read);
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Saves the probe copy of the atlas as BepInEx\TCGCardTracker-dump\PriceTagAtlas.png, for checking.</summary>
        private static void Dump(Color32[] atlas)
        {
            try
            {
                var dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "TCGCardTracker-dump");
                Directory.CreateDirectory(dir);
                var t = new Texture2D(Probe, Probe, TextureFormat.RGBA32, false);
                t.SetPixels32(atlas);
                t.Apply(false);
                var path = Path.Combine(dir, "PriceTagAtlas.png");
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(t));
                UnityEngine.Object.Destroy(t);
                Plugin.Log.LogInfo("TCG Studio label icons: saved a copy of the label atlas to " + path);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("label-dump", "Could not save the label atlas picture: " + e.Message);
            }
        }

        /// <summary>
        /// Adds each custom item to the atlas map: its own cell if it got one, else (box) its set's pack cell, else the
        /// basic pack / box icon.
        /// </summary>
        private static void ExtendMap(UnityEngine.Object map)
        {
            var values = (_itemValues!.GetValue(map) as int[] ?? new int[0]).ToList();
            var cells = (_itemCells!.GetValue(map) as int[] ?? new int[0]).ToList();
            int n = Math.Min(values.Count, cells.Count);
            values = values.Take(n).ToList();
            cells = cells.Take(n).ToList();
            int VanillaCell(EItemType t) { int i = values.IndexOf((int)t); return i >= 0 ? cells[i] : -1; }
            int packCell = VanillaCell(EItemType.BasicCardPack), boxCell = VanillaCell(EItemType.BasicCardBox);

            int own = 0, shared = 0;
            foreach (var (item, _) in _items)
            {
                int cell;
                if (_cells!.TryGetValue(item, out cell)) own++;
                else if (_isBox?.Invoke(item) == true)
                {
                    var pack = PackOf(item);
                    cell = pack != EItemType.None && _cells.TryGetValue(pack, out int pc) ? pc : boxCell;
                    shared++;
                }
                else if (_isPack?.Invoke(item) == true) { cell = packCell; shared++; }
                else continue;
                if (cell < 0) continue;
                int at = values.IndexOf((int)item);
                if (at >= 0) cells[at] = cell;
                else { values.Add((int)item); cells.Add(cell); }
            }
            _itemValues.SetValue(map, values.ToArray());
            _itemCells.SetValue(map, cells.ToArray());
            _itemCache?.SetValue(map, null); // rebuilt from the arrays on the next lookup
            Plugin.Log.LogInfo($"TCG Studio label icons: {own} custom item(s) show their own label icon, {shared} a shared one.");
        }

        /// <summary>The pack item of the TCG Studio set a box item belongs to (Registry.GetByItem(box).PackItem), or None.</summary>
        private static EItemType PackOf(EItemType box)
        {
            var get = AccessTools.Method(AccessTools.TypeByName(Registry), "GetByItem", new[] { typeof(EItemType) });
            var pack = get?.Invoke(null, new object[] { box });
            return pack != null && Traverse.Create(pack).Field("PackItem").GetValue() is EItemType p ? p : EItemType.None;
        }

        /// <summary>Every atlas cell the label map names: characters, barcode and the item and object icons.</summary>
        private static HashSet<int> Used(UnityEngine.Object map)
        {
            var used = new HashSet<int>();
            foreach (var f in _mapType!.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name == "itemTypeValues" || f.Name == "objectTypeValues") continue; // item numbers, not cells
                var v = f.GetValue(map);
                if (v is int i && i >= 0) used.Add(i);
                else if (v is int[] a) foreach (var c in a) if (c >= 0) used.Add(c);
            }
            return used;
        }

        /// <summary>A TCG Studio Registry check such as IsCustomItem(EItemType), or null if it isn't there.</summary>
        private static Func<EItemType, bool>? Check(Type registry, string name)
        {
            var m = AccessTools.Method(registry, name, new[] { typeof(EItemType) });
            return m != null && m.ReturnType == typeof(bool) ? (Func<EItemType, bool>)Delegate.CreateDelegate(typeof(Func<EItemType, bool>), m) : null;
        }

        private static string Describe(Material m)
        {
            var parts = new List<string>();
            var sh = m.shader;
            if (sh != null)
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    string name = sh.GetPropertyName(i);
                    var type = sh.GetPropertyType(i);
                    string val = type switch
                    {
                        ShaderPropertyType.Texture => m.GetTexture(name) is Texture t
                            ? $"{t.name} {t.width}x{t.height} {(t is Texture2D t2 ? t2.format.ToString() : t.GetType().Name)} {t.graphicsFormat} mips={t.mipmapCount}"
                            : "none",
                        ShaderPropertyType.Vector => m.GetVector(name).ToString(),
                        ShaderPropertyType.Color => m.GetColor(name).ToString(),
                        _ => m.GetFloat(name).ToString("G4"),
                    };
                    parts.Add($"{name}={val}");
                }
            return $"{m.name} shader={(sh != null ? sh.name : "none")} [{string.Join(", ", parts)}]";
        }
    }
}
