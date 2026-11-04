using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// Puts TCG Studio's custom booster pack and box art back on the 3D models in the game's Unity 6 build (1.03+).
    ///
    /// Since 1.03 the game draws packs and boxes with one shared material per kind (CardPackArrayMaterial,
    /// CardBoxMaterial, shader Unlit/UnlitWithShadowcasterTexture2dArray) whose art is a Texture2DArray
    /// (_MainTexArray); each item picks its layer with _TexIndex (ItemMeshData.textureIndex, set through a
    /// MaterialPropertyBlock). TCG Studio still swaps a plain texture into _MainTex, which that shader never reads,
    /// and its items copy the vanilla pack's layer number, so they show vanilla art.
    ///
    /// For each custom pack/box this builds a one-layer array of TCG Studio's art (a GPU copy, so it also works on
    /// its non-readable textures) and:
    /// - gives its ItemMeshData a material that draws that array, at layer 0 (shelves, hands, customers, boxes);
    /// - adds the array to the item's property block (the pack opening animation draws the shared vanilla
    ///   material with the item's block), and clears it again when a pooled item becomes something else.
    /// Card boxes also get their faces moved from TCG Studio's classic four-box atlas onto the 1.03 box layout
    /// (see BoxFaces).
    ///
    /// WORKAROUND (added 2026-10-31, tracker 0.15.0, TCG Studio 1.14.0, game 1.03): this only exists because TCG Studio
    /// doesn't support the 1.03 texture arrays yet. Once a TCG Studio release draws its packs and boxes correctly on
    /// 1.03 by itself, delete this file, its Install() call in Plugin.Awake, the [TCG Studio] FixPackSkins setting
    /// (Plugin.cs, TrackerWindow.Settings.cs) and its README entry. Until then FixPackSkins = false turns it off, which
    /// is also the way to check whether a new TCG Studio still needs it.
    /// </summary>
    internal static class PackSkins
    {
        private const string Injector = "TCGCustomCards.Runtime.ItemInjector";
        private const string Registry = "TCGCustomCards.Runtime.Registry";
        private const string ImageCache = "TCGCustomCards.Runtime.ImageCache";
        private const string StudioPaths = "TCGCustomCards.Core.StudioPaths";

        private static readonly int ArrayId = Shader.PropertyToID("_MainTexArray");
        private static readonly int IndexId = Shader.PropertyToID("_TexIndex");

        /// <summary>One-layer arrays by custom item.</summary>
        private static readonly Dictionary<EItemType, Texture> Arrays = new Dictionary<EItemType, Texture>();
        /// <summary>Renderers our array is set on (pooled items move between item types).</summary>
        private static readonly HashSet<Renderer> Marked = new HashSet<Renderer>();

        private static readonly FieldInfo? TextureIndex = AccessTools.Field(typeof(ItemMeshData), "textureIndex");
        private static readonly PropertyInfo? ItemBlock = AccessTools.Property(typeof(Item), "m_MaterialPropertyBlock");
        private static readonly FieldInfo? SecondaryRenderer = AccessTools.Field(typeof(Item), "m_MeshSecondary");
        private static object? _fixedFor;

        public static void Install()
        {
            try
            {
                var type = AccessTools.TypeByName(Injector);
                if (type == null) return; // TCG Studio isn't installed: nothing to do.
                var target = AccessTools.Method(type, "EnsureInjected");
                if (target == null)
                {
                    Plugin.Log.LogWarning("Could not find where TCG Studio adds its packs (a newer TCG Studio?); pack art is left to it.");
                    return;
                }
                if (TextureIndex == null)
                {
                    Plugin.Log.LogInfo("TCG Studio pack and box art: this game version draws packs the old way, left to TCG Studio.");
                    return;
                }
                var harmony = new Harmony(Plugin.Guid);
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(PackSkins), nameof(AfterInjected)));
                var setBlock = AccessTools.Method(typeof(Item), "SetPropertyBlock");
                if (setBlock != null) harmony.Patch(setBlock, postfix: new HarmonyMethod(typeof(PackSkins), nameof(AfterSetPropertyBlock)));
                var setMesh = AccessTools.Method(typeof(Item), nameof(Item.SetMesh));
                if (setMesh != null) harmony.Patch(setMesh, postfix: new HarmonyMethod(typeof(PackSkins), nameof(AfterSetMesh)));
                Plugin.Log.LogInfo("TCG Studio pack and box art: the tracker draws it through the game's pack texture arrays.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook TCG Studio's packs; pack art is left to it: " + e.Message);
            }
        }

        /// <summary>Harmony postfix on TCG Studio's ItemInjector.EnsureInjected (runs once per item list).</summary>
        private static void AfterInjected()
        {
            try
            {
                var so = AccessTools.Field(AccessTools.TypeByName(Injector), "_injectedInto")?.GetValue(null) as StockItemData_ScriptableObject;
                if (so == null || ReferenceEquals(so, _fixedFor)) return;
                _fixedFor = so;
                var meshes = so.m_ItemMeshDataList;
                var vPack = meshes[(int)EItemType.BasicCardPack];
                var vBox = meshes[(int)EItemType.BasicCardBox];
                Describe("Vanilla booster pack", vPack);
                Describe("Vanilla card box", vBox);
                if (!Plugin.FixPackSkins.Value)
                {
                    Plugin.Log.LogInfo("TCG Studio pack and box art: FixPackSkins is off, left as TCG Studio made it.");
                    return;
                }

                var list = AccessTools.Field(AccessTools.TypeByName(Registry), "Packs")?.GetValue(null) as IEnumerable;
                if (list == null)
                {
                    Plugin.Log.LogWarning("Could not read TCG Studio's pack list (a newer TCG Studio?); pack art is left to it.");
                    return;
                }
                int packs = 0, boxes = 0;
                foreach (var pack in list)
                {
                    var t = Traverse.Create(pack);
                    var def = t.Field("Def");
                    var setDef = t.Field("Set").Field("Def").GetValue();
                    if (Fix(meshes, vPack, false, (EItemType)t.Field("PackItem").GetValue(), setDef, def.Field("PackTexture").GetValue() as string)) packs++;
                    if (Fix(meshes, vBox, true, (EItemType)t.Field("BoxItem").GetValue(), setDef, def.Field("BoxTexture").GetValue() as string)) boxes++;
                }
                Plugin.Log.LogInfo($"TCG Studio pack and box art: {packs} pack(s) and {boxes} box(es) now draw their own art.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not fix TCG Studio's pack art: " + e);
            }
            FixAccessories();
        }

        /// <summary>
        /// Same for TCG Studio's accessories (sleeves, playmats, comics, collection books, figurines, ...): each one whose
        /// base item draws from a texture array gets a one-layer array of its own texture. Accessories with their own
        /// model (figurines) already show their texture, so they are left alone; the base models keep the UV layout the
        /// game gives them, which TCG Studio's templates may not match on 1.03 (logged, and the vanilla layer saved to
        /// BepInEx\TCGCardTracker-dump, so a different layout can be spotted).
        /// </summary>
        private static void FixAccessories()
        {
            try
            {
                var so = _fixedFor as StockItemData_ScriptableObject;
                var list = AccessTools.Field(AccessTools.TypeByName(Registry), "Accessories")?.GetValue(null) as IEnumerable;
                if (so == null || list == null) return;
                var meshes = so.m_ItemMeshDataList;
                var itemFile = AccessTools.Method(AccessTools.TypeByName(StudioPaths), "ItemFile");
                var described = new HashSet<EItemType>();
                int fixedCount = 0, total = 0;
                foreach (var acc in list)
                {
                    var t = Traverse.Create(acc);
                    var item = (EItemType)t.Field("Item").GetValue();
                    var def = t.Field("Def");
                    var baseItem = (EItemType)def.Field("BaseItem").GetValue();
                    string label = def.Field("Name").GetValue() as string ?? "?";
                    string? texFile = def.Field("Texture").GetValue() as string, meshFile = def.Field("Mesh").GetValue() as string;
                    if (item == EItemType.None || (int)item >= meshes.Count || baseItem < 0 || (int)baseItem >= meshes.Count) continue;
                    total++;
                    var vanilla = meshes[(int)baseItem];
                    if (described.Add(baseItem))
                    {
                        Describe($"Vanilla {baseItem}", vanilla);
                        DumpLayer(vanilla, baseItem.ToString());
                    }
                    var md = meshes[(int)item];
                    Describe($"TCG Studio accessory \"{label}\" (base {baseItem}{(string.IsNullOrEmpty(meshFile) ? "" : ", own model " + (md.mesh != null ? md.mesh.name : "none"))})", md);

                    if (!string.IsNullOrEmpty(meshFile) && md.mesh != null && md.mesh != vanilla.mesh)
                    {
                        // Figurines and other own models already show their texture on 1.03 (Eric, 2026-11-04).
                        Plugin.Log.LogInfo($"TCG Studio accessory \"{label}\": own model, left to TCG Studio.");
                        continue;
                    }
                    var shared = vanilla.material;
                    if (shared == null || !shared.HasProperty(ArrayId) || !(shared.GetTexture(ArrayId) is Texture2DArray))
                    {
                        Plugin.Log.LogInfo($"TCG Studio accessory \"{label}\": its base item isn't drawn from a texture array, left to TCG Studio.");
                        continue;
                    }
                    var folder = def.Field("FolderPath").GetValue() as string;
                    var path = string.IsNullOrEmpty(texFile) || folder == null ? null : itemFile?.Invoke(null, new object[] { folder, texFile! }) as string;
                    var sprite = string.IsNullOrEmpty(path) ? null
                        : AccessTools.Method(AccessTools.TypeByName(ImageCache), "Get", new[] { typeof(string) })?.Invoke(null, new object[] { path! }) as Sprite;
                    var array = ToArray(sprite != null ? sprite.texture : null, item);
                    if (array == null)
                    {
                        Plugin.Log.LogInfo($"TCG Studio accessory \"{label}\": no texture to show ({path ?? "none set"}).");
                        continue;
                    }
                    Use(md, (int)item, array, shared.GetTexture(ArrayId));
                    fixedCount++;
                }
                if (total > 0)
                    Plugin.Log.LogInfo($"TCG Studio accessories: {fixedCount} of {total} now draw their own texture.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not fix TCG Studio's accessory textures: " + e);
            }
        }

        /// <summary>
        /// Points every material slot of <paramref name="md"/> that draws <paramref name="vanillaArray"/> at
        /// <paramref name="array"/> instead (a copy for shared vanilla materials) and sets its layer to 0. Slots with
        /// another array or none (a binder's pages, a playmat's underside) keep theirs.
        /// </summary>
        private static void Use(ItemMeshData md, int i, Texture array, Texture? vanillaArray)
        {
            var copies = new Dictionary<Material, Material>();
            Material? Swap(Material? m)
            {
                if (m == null || !m.HasProperty(ArrayId)) return m;
                if (vanillaArray != null && m.GetTexture(ArrayId) != vanillaArray) return m;
                if (copies.TryGetValue(m, out var c)) return c;
                // TCG Studio's own per-item copy is changed in place; a shared vanilla material gets a copy of its own.
                c = m.name.StartsWith("TCGCC_", StringComparison.Ordinal) ? m
                    : new Material(m) { name = "TCGCT_" + i + "_" + m.name, hideFlags = HideFlags.DontUnloadUnusedAsset };
                c.SetTexture(ArrayId, array);
                return copies[m] = c;
            }
            md.material = Swap(md.material);
            md.materialSecondary = Swap(md.materialSecondary);
            if (md.materialList != null)
                for (int k = 0; k < md.materialList.Count; k++) md.materialList[k] = Swap(md.materialList[k])!;
            TextureIndex!.SetValue(md, (ushort)0);
        }

        /// <summary>Saves an item's vanilla texture-array layer (up to 1024 px) as BepInEx\TCGCardTracker-dump\&lt;name&gt;.png.</summary>
        private static void DumpLayer(ItemMeshData md, string name)
        {
            try
            {
                if (md.material == null || !md.material.HasProperty(ArrayId) || !(md.material.GetTexture(ArrayId) is Texture2DArray a)) return;
                int layer = Convert.ToInt32(TextureIndex!.GetValue(md));
                float scale = Mathf.Min(1f, 1024f / Mathf.Max(a.width, a.height));
                int w = Mathf.Max(1, (int)(a.width * scale)), h = Mathf.Max(1, (int)(a.height * scale));
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(a, rt, layer, 0);
                RenderTexture.active = rt;
                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "TCGCardTracker-dump");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), ImageConversion.EncodeToPNG(read));
                UnityEngine.Object.Destroy(read);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("dump-" + name, $"Could not save the {name} texture for checking: {e.Message}");
            }
        }

        /// <summary>Gives one custom item a one-layer array of its art. False when it has no art or the copy fails.</summary>
        private static bool Fix(List<ItemMeshData> meshes, ItemMeshData vanilla, bool box, EItemType item, object? setDef, string? file)
        {
            int i = (int)item;
            if (item == EItemType.None || i < 0 || i >= meshes.Count || string.IsNullOrEmpty(file) || setDef == null) return false;
            var shared = vanilla.material;
            if (shared == null || !shared.HasProperty(ArrayId)) return false; // not an array material: nothing to fix
            var array = box ? BoxArray(vanilla, Resolve(setDef, file!), item) : ToArray(Load(setDef, file!), item);
            if (array == null) return false;

            Use(meshes[i], i, array, null);
            Arrays[item] = array;
            return true;
        }

        /// <summary>A one-layer Texture2DArray holding <paramref name="tex"/> (all mip levels), or null.</summary>
        private static Texture? ToArray(Texture? tex, EItemType item)
        {
            if (tex == null) return null;
            try
            {
                if (!(tex is Texture2D t2)) return null;
                var array = new Texture2DArray(t2.width, t2.height, 1, t2.format, t2.mipmapCount > 1)
                {
                    name = "TCGCT_" + (int)item + "_" + t2.name,
                    wrapMode = t2.wrapMode,
                    filterMode = t2.filterMode,
                    anisoLevel = t2.anisoLevel,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                Graphics.CopyTexture(t2, 0, array, 0);
                return array;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pack-array-" + (int)item, $"Could not copy TCG Studio's art for item {(int)item} ({tex.name}): {e.Message}");
                return null;
            }
        }

        /// <summary>The absolute path of a set's file, through TCG Studio's SetDef.Resolve, or null.</summary>
        private static string? Resolve(object setDef, string file) =>
            AccessTools.Method(setDef.GetType(), "Resolve")?.Invoke(setDef, new object[] { file }) as string;

        // Card box faces as x, y, width, height fractions of the texture, top-left origin.
        // TCG Studio paints boxes on the classic box atlas (Studio's uvmap/models.go, 1024 px: front & back 202-561 x 153-374,
        // top & bottom 201-510 x 14-143, sides 11-171 x 18-266, all upright). Game 1.03 gives each box its own layer laid
        // out as a cross around the lid, the back upside down and the sides turned with their tops toward the lid; the
        // box's underside (below the front) keeps the vanilla look. "map" is where the classic face lands in 1.03 (found
        // by matching the classic basic box art against the vanilla 1.03 layer, 1024 px); "paint" is the area drawn,
        // up to halfway to the next face, so the art's edge pixels run on past the face edges instead of vanilla.
        private static readonly Rect StudioFront = Px(202, 153, 561, 374), StudioTop = Px(201, 14, 510, 143), StudioSide = Px(11, 18, 171, 266);
        private static readonly (Rect paint, Rect map, Rect src, int turns)[] BoxFaces =
        {
            (Px(306, 0, 716, 286), Px(324, 27, 682, 276), StudioFront, 2),    // back
            (Px(306, 512, 716, 785), Px(343, 518, 697, 770), StudioFront, 0), // front
            (Px(31, 269, 325, 527), Px(83, 298, 309, 503), StudioSide, 1),    // left side, turned a quarter clockwise
            (Px(700, 269, 990, 527), Px(715, 294, 938, 497), StudioSide, 3),  // right side, a quarter anticlockwise
            (Px(325, 286, 700, 512), Px(341, 296, 684, 506), StudioTop, 0),   // lid
        };

        private static Rect Px(float x0, float y0, float x1, float y1) => new Rect(x0 / 1024f, y0 / 1024f, (x1 - x0) / 1024f, (y1 - y0) / 1024f);

        private static Color32[]? _boxBase;
        private static int _boxW, _boxH;

        /// <summary>
        /// A one-layer array for a custom card box: the vanilla basic box layer with TCG Studio's faces redrawn on the
        /// 1.03 layout. TCG Studio's picture is read from its file (its own copy is GPU-only); null if anything fails.
        /// </summary>
        private static Texture? BoxArray(ItemMeshData vanilla, string? path, EItemType item)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            Texture2D? src = null, outTex = null;
            try
            {
                if (_boxBase == null)
                {
                    if (!(vanilla.material.GetTexture(ArrayId) is Texture2DArray va)) return null;
                    int layer = Convert.ToInt32(TextureIndex!.GetValue(vanilla));
                    _boxW = va.width; _boxH = va.height;
                    var rt = RenderTexture.GetTemporary(_boxW, _boxH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var prev = RenderTexture.active;
                    Graphics.Blit(va, rt, layer, 0);
                    RenderTexture.active = rt;
                    var read = new Texture2D(_boxW, _boxH, TextureFormat.RGBA32, false);
                    read.ReadPixels(new Rect(0, 0, _boxW, _boxH), 0, 0);
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    _boxBase = read.GetPixels32();
                    UnityEngine.Object.Destroy(read);
                }

                src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!src.LoadImage(File.ReadAllBytes(path))) return null;
                var sp = src.GetPixels32();
                int sw = src.width, sh = src.height, w = _boxW, h = _boxH;
                var px = (Color32[])_boxBase.Clone();
                foreach (var (paint, dest, srcRect, turns) in BoxFaces)
                {
                    int x0 = (int)(paint.xMin * w), x1 = Math.Min(w, (int)Math.Ceiling(paint.xMax * w));
                    int y0 = (int)(paint.yMin * h), y1 = Math.Min(h, (int)Math.Ceiling(paint.yMax * h));
                    for (int y = y0; y < y1; y++)
                    {
                        float v = Mathf.Clamp01((y + 0.5f - dest.yMin * h) / (dest.height * h));
                        for (int x = x0; x < x1; x++)
                        {
                            float u = Mathf.Clamp01((x + 0.5f - dest.xMin * w) / (dest.width * w));
                            // (a, b): where this pixel lies on the upright face, top-left origin.
                            float a, b;
                            switch (turns)
                            {
                                case 1: a = v; b = 1f - u; break;
                                case 2: a = 1f - u; b = 1f - v; break;
                                case 3: a = 1f - v; b = u; break;
                                default: a = u; b = v; break;
                            }
                            float sx = (srcRect.xMin + a * srcRect.width) * sw - 0.5f;
                            float sy = (srcRect.yMin + b * srcRect.height) * sh - 0.5f;
                            px[(h - 1 - y) * w + x] = Sample(sp, sw, sh, sx, sy);
                        }
                    }
                }

                outTex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "TCGCT_box_" + (int)item };
                outTex.SetPixels32(px);
                outTex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
                var array = new Texture2DArray(w, h, 1, TextureFormat.RGBA32, true)
                {
                    name = "TCGCT_" + (int)item + "_box",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                Graphics.CopyTexture(outTex, 0, array, 0);
                return array;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("box-array-" + (int)item, $"Could not lay out TCG Studio's box art for item {(int)item} ({path}): {e.Message}");
                return null;
            }
            finally
            {
                if (src != null) UnityEngine.Object.Destroy(src);
                if (outTex != null) UnityEngine.Object.Destroy(outTex);
            }
        }

        /// <summary>Bilinear sample of a bottom-up pixel array at (x, y) in top-left pixel coordinates.</summary>
        private static Color32 Sample(Color32[] p, int w, int h, float x, float y)
        {
            x = Mathf.Clamp(x, 0, w - 1); y = Mathf.Clamp(y, 0, h - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float fx = x - x0, fy = y - y0;
            Color32 At(int xx, int yy) => p[(h - 1 - yy) * w + xx];
            var top = Color32.Lerp(At(x0, y0), At(x1, y0), fx);
            var bottom = Color32.Lerp(At(x0, y1), At(x1, y1), fx);
            return Color32.Lerp(top, bottom, fy);
        }

        /// <summary>The custom art through TCG Studio's own image cache (same texture it loaded), or null.</summary>
        private static Texture? Load(object setDef, string file)
        {
            var path = Resolve(setDef, file);
            if (string.IsNullOrEmpty(path)) return null;
            var sprite = AccessTools.Method(AccessTools.TypeByName(ImageCache), "Get", new[] { typeof(string) })?.Invoke(null, new object[] { path! }) as Sprite;
            return sprite != null ? sprite.texture : null;
        }

        /// <summary>
        /// Harmony postfix on Item.SetPropertyBlock(ref MeshRenderer, ref ushort index): a custom item's block also
        /// carries its array, which the pack opening animation copies; any other item's block loses one set before.
        /// </summary>
        private static void AfterSetPropertyBlock(Item __instance, object[] __args)
        {
            try
            {
                if (!(__args[0] is MeshRenderer r)) return;
                var block = Apply(r, __instance.GetItemType(), __args[1] is ushort idx ? idx : (ushort)0);
                if (block != null) ItemBlock?.SetValue(__instance, block);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pack-block", "Could not set a custom pack's art: " + e.Message);
            }
        }

        /// <summary>Harmony postfix on Item.SetMesh (customers' bags, auto pack openers): same as above, both meshes.</summary>
        private static void AfterSetMesh(Item __instance, EItemType itemType)
        {
            try
            {
                var block = Apply(__instance.m_Mesh, itemType, null);
                if (SecondaryRenderer?.GetValue(__instance) is MeshRenderer second) Apply(second, itemType, null);
                if (block != null) ItemBlock?.SetValue(__instance, block);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pack-mesh", "Could not set a custom pack's art: " + e.Message);
            }
        }

        /// <summary>
        /// Sets (custom item) or clears (anything else we marked) the array override on one renderer.
        /// <paramref name="index"/> null keeps the renderer's layer number. Returns the block now set, or null if untouched.
        /// </summary>
        private static MaterialPropertyBlock? Apply(MeshRenderer? r, EItemType type, ushort? index)
        {
            if (r == null) return null;
            bool custom = Arrays.TryGetValue(type, out var array);
            if (!custom && !Marked.Contains(r)) return null;
            var old = new MaterialPropertyBlock();
            r.GetPropertyBlock(old);
            var block = new MaterialPropertyBlock();
            block.SetFloat(IndexId, custom ? 0f : index ?? old.GetFloat(IndexId));
            if (custom)
            {
                block.SetTexture(ArrayId, array);
                Marked.Add(r);
            }
            else Marked.Remove(r);
            r.SetPropertyBlock(block);
            return block;
        }

        /// <summary>Logs an item's materials: shader, every texture and its layer number.</summary>
        private static void Describe(string label, ItemMeshData md)
        {
            var parts = new List<string> { $"mesh={(md.mesh != null ? md.mesh.name : "none")}" };
            parts.Add("textureIndex=" + TextureIndex?.GetValue(md));
            parts.Add("arrayMaterialType=" + AccessTools.Field(typeof(ItemMeshData), "arrayMaterialType")?.GetValue(md));
            void Add(string what, Material? m) { if (m != null) parts.Add(what + "=" + Materials(m)); }
            Add("material", md.material);
            Add("materialSecondary", md.materialSecondary);
            if (md.materialList != null)
                for (int i = 0; i < md.materialList.Count; i++) Add($"materialList[{i}]", md.materialList[i]);
            Plugin.Log.LogInfo($"{label}: {string.Join("; ", parts)}");
        }

        private static string Materials(Material m)
        {
            var slots = m.GetTexturePropertyNames()
                .Select(p => (p, t: m.GetTexture(p)))
                .Where(x => x.t != null)
                .Select(x => $"{x.p}={x.t.name} {x.t.width}x{x.t.height}" + (x.t is Texture2DArray a ? $"x{a.depth}" : ""));
            return $"{m.name} shader={(m.shader != null ? m.shader.name : "none")} [{string.Join(", ", slots)}]";
        }
    }
}
