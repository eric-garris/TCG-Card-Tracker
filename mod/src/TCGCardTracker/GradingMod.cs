using System;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using TCGCardTracker.Core;

namespace TCGCardTracker
{
    /// <summary>
    /// CardGradeOverhaul (com.qolmods.cardgradeoverhaul): slabs from several grading companies. The company is read
    /// from the slab's cert number (Core.Graders), so nothing of the mod's code is called.
    /// </summary>
    internal static class GradingMod
    {
        private const string Guid = "com.qolmods.cardgradeoverhaul";
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
    }
}
