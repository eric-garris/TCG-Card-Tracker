using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace TCGCardTracker
{
    /// <summary>
    /// Puts TCG Studio's custom booster pack and box art back on the 3D models in the game's Unity 6 build (1.03+).
    ///
    /// TCG Studio gives each custom pack/box a copy of the vanilla pack/box material with only
    /// <see cref="Material.mainTexture"/> swapped. The Unity 6 build draws items with Toony Colors Pro 2's Hybrid
    /// shader, which samples the same art again as _EmissionMap and _SpecGlossMap (and an item can draw more
    /// materials from materialSecondary / materialList), so the vanilla art keeps showing. A postfix on TCG Studio's
    /// ItemInjector.EnsureInjected gives each custom item a material copy where every texture slot and material
    /// that held the vanilla art draws the custom art. The vanilla materials are logged once, so the next game
    /// change can be read from a log.
    /// </summary>
    internal static class PackSkins
    {
        private const string Injector = "TCGCustomCards.Runtime.ItemInjector";
        private const string Registry = "TCGCustomCards.Runtime.Registry";
        private const string ImageCache = "TCGCustomCards.Runtime.ImageCache";

        private static readonly HashSet<EItemType> Logged = new HashSet<EItemType>();
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
                var harmony = new Harmony(Plugin.Guid);
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(PackSkins), nameof(AfterInjected)));
                var setMesh = AccessTools.Method(typeof(Item), nameof(Item.SetMesh));
                if (setMesh != null) harmony.Patch(setMesh, postfix: new HarmonyMethod(typeof(PackSkins), nameof(AfterSetMesh)));
                Plugin.Log.LogInfo("TCG Studio pack and box art: the tracker puts it in every texture slot.");
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

                int packs = 0, boxes = 0, slots = 0;
                var registry = AccessTools.TypeByName(Registry);
                var list = AccessTools.Field(registry, "Packs")?.GetValue(null) as IEnumerable;
                if (list == null)
                {
                    Plugin.Log.LogWarning("Could not read TCG Studio's pack list (a newer TCG Studio?); pack art is left to it.");
                    return;
                }
                foreach (var pack in list)
                {
                    var t = Traverse.Create(pack);
                    var def = t.Field("Def");
                    var setDef = t.Field("Set").Field("Def").GetValue();
                    int n;
                    if ((n = Fix(meshes, vPack, (EItemType)t.Field("PackItem").GetValue(), setDef, def.Field("PackTexture").GetValue() as string)) >= 0)
                    { packs++; slots += n; }
                    if ((n = Fix(meshes, vBox, (EItemType)t.Field("BoxItem").GetValue(), setDef, def.Field("BoxTexture").GetValue() as string)) >= 0)
                    { boxes++; slots += n; }
                }
                Plugin.Log.LogInfo($"TCG Studio pack and box art: {packs} pack(s) and {boxes} box(es) now draw their art in {slots} texture slot(s).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not fix TCG Studio's pack art: " + e);
            }
        }

        /// <summary>
        /// Gives one custom item a material that draws <paramref name="file"/> wherever the vanilla item drew its art.
        /// Returns the number of texture slots set, or -1 when the item has no custom art.
        /// </summary>
        private static int Fix(List<ItemMeshData> meshes, ItemMeshData vanilla, EItemType item, object? setDef, string? file)
        {
            int i = (int)item;
            if (item == EItemType.None || i < 0 || i >= meshes.Count || string.IsNullOrEmpty(file) || setDef == null) return -1;
            var tex = Load(setDef, file!);
            var art = Art(vanilla.material);
            if (tex == null || art == null) return -1;

            var md = meshes[i];
            int slots = 0;
            var copies = new Dictionary<Material, Material>();
            Material? Swap(Material? m)
            {
                if (m == null) return m;
                if (copies.TryGetValue(m, out var c)) return c;
                // TCG Studio's own per-item copy (art on the main slot only) is fixed in place; a shared vanilla
                // material that draws the art gets a copy of its own.
                bool own = m.name.StartsWith("TCGCC_", StringComparison.Ordinal);
                if (!own && m != vanilla.material && Art(m) != art) return copies[m] = m;
                c = own ? m : new Material(m) { name = "TCGCT_" + (int)item + "_" + m.name, hideFlags = HideFlags.DontUnloadUnusedAsset };
                foreach (var prop in c.GetTexturePropertyNames())
                    if (c.GetTexture(prop) == art) { c.SetTexture(prop, tex); slots++; }
                SetMain(c, tex);
                return copies[m] = c;
            }
            md.material = Swap(md.material);
            md.materialSecondary = Swap(md.materialSecondary);
            if (md.materialList != null)
                for (int k = 0; k < md.materialList.Count; k++) md.materialList[k] = Swap(md.materialList[k])!;
            return slots;
        }

        /// <summary>The custom art through TCG Studio's own image cache (same texture it loaded), or null.</summary>
        private static Texture? Load(object setDef, string file)
        {
            var path = AccessTools.Method(setDef.GetType(), "Resolve")?.Invoke(setDef, new object[] { file }) as string;
            if (string.IsNullOrEmpty(path)) return null;
            var sprite = AccessTools.Method(AccessTools.TypeByName(ImageCache), "Get", new[] { typeof(string) })?.Invoke(null, new object[] { path! }) as Sprite;
            return sprite != null ? sprite.texture : null;
        }

        /// <summary>The material's art: its main-flagged texture, else _BaseMap, else _MainTex.</summary>
        private static Texture? Art(Material? m)
        {
            var prop = MainProperty(m);
            return prop == null ? null : m!.GetTexture(prop);
        }

        private static string? MainProperty(Material? m)
        {
            if (m == null || m.shader == null) return null;
            var s = m.shader;
            for (int i = 0, n = s.GetPropertyCount(); i < n; i++)
                if (s.GetPropertyType(i) == ShaderPropertyType.Texture && (s.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) != 0)
                    return s.GetPropertyName(i);
            if (m.HasProperty("_BaseMap")) return "_BaseMap";
            if (m.HasProperty("_MainTex")) return "_MainTex";
            return null;
        }

        private static void SetMain(Material m, Texture tex)
        {
            var prop = MainProperty(m);
            if (prop != null) m.SetTexture(prop, tex);
        }

        /// <summary>Logs an item's materials: shader and every texture slot.</summary>
        private static void Describe(string label, ItemMeshData md)
        {
            var parts = new List<string> { $"mesh={(md.mesh != null ? md.mesh.name : "none")}" };
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
                .Select(x => $"{x.p}={x.t.name} {x.t.width}x{x.t.height}");
            return $"{m.name} shader={(m.shader != null ? m.shader.name : "none")} main={MainProperty(m) ?? "none"} [{string.Join(", ", slots)}]";
        }

        /// <summary>Harmony postfix on Item.SetMesh: logs what the first custom pack/box on screen actually draws.</summary>
        private static void AfterSetMesh(Item __instance, EItemType itemType)
        {
            try
            {
                if ((int)itemType < (int)EItemType.Max || Logged.Contains(itemType) || Logged.Count >= 2) return;
                var r = __instance.m_Mesh;
                if (r == null) return;
                Logged.Add(itemType);
                Plugin.Log.LogInfo($"Custom item {(int)itemType} on screen: renderer={r.GetType().Name} materials=[" +
                                   string.Join(" | ", r.sharedMaterials.Where(m => m != null).Select(Materials)) + "]");
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pack-skin-log", "Could not describe a custom item: " + e.Message);
            }
        }
    }
}
