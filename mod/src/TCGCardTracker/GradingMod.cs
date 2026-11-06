using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using TCGCardTracker.Core;

namespace TCGCardTracker
{
    /// <summary>
    /// CardGradeOverhaul (com.qolmods.cardgradeoverhaul): slabs from several grading companies. The company is read
    /// from the slab's cert number (Core.Graders), so nothing of the mod's code is called for that.
    ///
    /// Its grading page lists the album by walking the values of the game's set enum (Grading.BuildCollection), so
    /// TCG Studio's sets (expansion ints 100+, not in the enum) never reach the page or its set menu, though the rest
    /// of the mod (counts, taking a card from the album, copy conditions) already handles them. A postfix on
    /// Grading.Gather, which BuildCollection calls right before its set loop, runs the mod's own per-set scan
    /// (Grading.Scan) for each custom set too, before the picked cards are matched back to the list. Found by name,
    /// so the mod isn't needed at build time.
    /// </summary>
    internal static class GradingMod
    {
        internal const string Guid = "com.qolmods.cardgradeoverhaul";
        private const string GradingType = "CardGradeOverhaul.Grading";
        // Custom sets sort after every vanilla set in the page's "by set" order.
        private const int CustomOrderStart = 100_000_000;

        private static MethodInfo? _scan;
        private static bool _building;
        private static bool? _present;

        public static bool Present => _present ??= Chainloader.PluginInfos.ContainsKey(Guid);

        /// <summary>The mod's LegacyGrader setting: the company shown on slabs graded before it was installed.</summary>
        public static int LegacyCompany()
        {
            try
            {
                if (Chainloader.PluginInfos.TryGetValue(Guid, out var info) && info.Instance != null
                    && info.Instance.Config.TryGetEntry<string>(new ConfigDefinition("General", "LegacyGrader"), out var entry))
                    return Graders.Parse(entry.Value);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("grading-legacy", "Could not read CardGradeOverhaul's LegacyGrader setting, counting older slabs as TCG: " + e.Message);
            }
            return 0;
        }

        public static void Install()
        {
            if (!Plugin.GradingCustomSets.Value || !Present) return;
            try
            {
                if (AccessTools.TypeByName(CustomTrades.StudioHook) == null) return; // No TCG Studio sets to add.
                var grading = AccessTools.TypeByName(GradingType);
                var build = grading == null ? null : AccessTools.Method(grading, "BuildCollection");
                var gather = grading == null ? null : AccessTools.Method(grading, "Gather");
                _scan = grading == null ? null : AccessTools.Method(grading, "Scan");
                var p = _scan?.GetParameters();
                if (build == null || gather == null || _scan == null || p!.Length != 4
                    || p[0].ParameterType != typeof(ECardExpansionType) || p[1].ParameterType != typeof(bool)
                    || p[3].ParameterType != typeof(int).MakeByRefType())
                {
                    Plugin.Log.LogWarning("CardGradeOverhaul's grading page changed; TCG Studio sets stay out of it.");
                    return;
                }
                var harmony = new Harmony(Plugin.Guid);
                harmony.Patch(build,
                    prefix: new HarmonyMethod(typeof(GradingMod), nameof(BeforeBuild)),
                    finalizer: new HarmonyMethod(typeof(GradingMod), nameof(AfterBuild)));
                harmony.Patch(gather, postfix: new HarmonyMethod(typeof(GradingMod), nameof(AfterGather)));
                Plugin.Log.LogInfo("TCG Studio sets added to CardGradeOverhaul's grading page.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook CardGradeOverhaul's grading page; TCG Studio sets stay out of it: " + e.Message);
            }
        }

        private static void BeforeBuild() => _building = true;

        private static Exception? AfterBuild(Exception? __exception)
        {
            _building = false;
            return __exception;
        }

        /// <summary>Postfix on Grading.Gather(CopyStore): adds each custom set to the album list being built.</summary>
        private static void AfterGather(object? s)
        {
            if (!_building || _scan == null) return;
            _building = false; // Once per build.
            int order = CustomOrderStart;
            foreach (int e in GameReader.CustomExpansions())
            {
                var args = new object?[] { (ECardExpansionType)e, false, s, order };
                try
                {
                    _scan.Invoke(null, args);
                    order = (int)args[3]!;
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
                    Plugin.LogOnce("grading-scan:" + e, $"Could not list TCG Studio set {e} on the grading page: {inner.GetType().Name}: {inner.Message}");
                }
            }
        }
    }
}
