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
    /// </summary>
    internal static class PackSkins
    {
        private const string Injector = "TCGCustomCards.Runtime.ItemInjector";
        private const string Registry = "TCGCustomCards.Runtime.Registry";
        private const string ImageCache = "TCGCustomCards.Runtime.ImageCache";

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
                    if (Fix(meshes, vPack, (EItemType)t.Field("PackItem").GetValue(), setDef, def.Field("PackTexture").GetValue() as string)) packs++;
                    if (Fix(meshes, vBox, (EItemType)t.Field("BoxItem").GetValue(), setDef, def.Field("BoxTexture").GetValue() as string)) boxes++;
                }
                Plugin.Log.LogInfo($"TCG Studio pack and box art: {packs} pack(s) and {boxes} box(es) now draw their own art.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not fix TCG Studio's pack art: " + e);
            }
        }

        /// <summary>Gives one custom item a one-layer array of its art. False when it has no art or the copy fails.</summary>
        private static bool Fix(List<ItemMeshData> meshes, ItemMeshData vanilla, EItemType item, object? setDef, string? file)
        {
            int i = (int)item;
            if (item == EItemType.None || i < 0 || i >= meshes.Count || string.IsNullOrEmpty(file) || setDef == null) return false;
            var shared = vanilla.material;
            if (shared == null || !shared.HasProperty(ArrayId)) return false; // not an array material: nothing to fix
            var tex = Load(setDef, file!);
            if (tex == null) return false;
            var array = ToArray(tex, item);
            if (array == null) return false;

            var md = meshes[i];
            var copies = new Dictionary<Material, Material>();
            Material? Swap(Material? m)
            {
                if (m == null || !m.HasProperty(ArrayId)) return m;
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
            Arrays[item] = array;
            return true;
        }

        /// <summary>A one-layer Texture2DArray holding <paramref name="tex"/> (all mip levels), or null.</summary>
        private static Texture? ToArray(Texture tex, EItemType item)
        {
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

        /// <summary>The custom art through TCG Studio's own image cache (same texture it loaded), or null.</summary>
        private static Texture? Load(object setDef, string file)
        {
            var path = AccessTools.Method(setDef.GetType(), "Resolve")?.Invoke(setDef, new object[] { file }) as string;
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
