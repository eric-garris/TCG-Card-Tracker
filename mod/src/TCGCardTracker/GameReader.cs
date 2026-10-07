using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// Reads the player's cards straight from the game's live data (CPlayerData), building the
    /// catalog from the game's own monster lists so names, order and editions always match the
    /// installed version.
    ///
    /// Per-set lists are looked up by field name, so the same build works on game versions
    /// with or without Ascension (added in 1.0).
    /// </summary>
    internal static class GameReader
    {
        private sealed class SetTemplate
        {
            public string Key = "";
            public string Name = "";
            public int Expansion;
            public int PerNoFoil = 6;
            public ListDef[] Lists = Array.Empty<ListDef>();
            public string[] FallbackEditions = Array.Empty<string>();
            /// <summary>Unreleased sets only content mods use: shown only when they hold cards.</summary>
            public bool OnlyIfOwned;
        }

        private static readonly string[] StandardEditions = { "Basic", "First Edition", "Silver", "Gold", "EX", "Full Art" };
        private static readonly string[] AscensionEditions = { "Basic", "Silver", "Gold", "Silver Full Art", "EX Full Art", "Borderless Full Art" };

        private static readonly SetTemplate[] Templates =
        {
            new SetTemplate { Key = "tetramon", Name = "Tetramon", Expansion = 0, Lists = new[] { new ListDef { Suffix = "" } }, FallbackEditions = StandardEditions },
            new SetTemplate { Key = "destiny", Name = "Destiny", Expansion = 1, Lists = new[] { new ListDef { Suffix = "Destiny", IsDestiny = true } }, FallbackEditions = StandardEditions },
            new SetTemplate { Key = "ascension", Name = "Ascension", Expansion = 7, Lists = new[] { new ListDef { Suffix = "Ascension" } }, FallbackEditions = AscensionEditions },
            new SetTemplate
            {
                Key = "ghost", Name = "Ghost", Expansion = 2, PerNoFoil = 1,
                Lists = new[]
                {
                    new ListDef { Suffix = "Ghost", Label = "White" },
                    new ListDef { Suffix = "GhostBlack", IsDestiny = true, Label = "Black" },
                },
                FallbackEditions = new[] { "Full Art" },
            },
            new SetTemplate { Key = "megabot", Name = "Megabot", Expansion = 3, Lists = new[] { new ListDef { Suffix = "Megabot" } }, FallbackEditions = StandardEditions, OnlyIfOwned = true },
            new SetTemplate { Key = "fantasyrpg", Name = "Fantasy RPG", Expansion = 4, Lists = new[] { new ListDef { Suffix = "FantasyRPG" } }, FallbackEditions = StandardEditions, OnlyIfOwned = true },
            new SetTemplate { Key = "catjob", Name = "Cat Job", Expansion = 5, Lists = new[] { new ListDef { Suffix = "CatJob" } }, FallbackEditions = StandardEditions, OnlyIfOwned = true },
        };

        private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static object? StaticField(string name)
        {
            try
            {
                return typeof(CPlayerData).GetField(name, StaticPublic)?.GetValue(null);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("field:" + name, $"Could not read CPlayerData.{name}: {e.Message}");
                return null;
            }
        }

        private static List<int>? AlbumList(string suffix) => StaticField("m_CardCollectedList" + suffix) as List<int>;

        /// <summary>
        /// True when a shop is loaded. The ShelfManager check keeps us off the title screen, where
        /// the game helpers we call (which go through CSingleton) would create phantom managers.
        /// </summary>
        public static bool IsReady()
        {
            try
            {
                return GameInstance.m_FinishedSavefileLoading
                    && UnityEngine.Object.FindObjectOfType<ShelfManager>() != null
                    && CPlayerData.m_CardCollectedList != null
                    && CPlayerData.m_CardCollectedList.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static Catalog BuildCatalog()
        {
            var sets = new List<SetDef>();
            foreach (var t in Templates)
            {
                // Skip sets this game version doesn't have (e.g. Ascension before 1.0).
                var firstList = AlbumList(t.Lists[0].Suffix);
                if (firstList == null) continue;

                int[] shown = ShownMonsters(t.Expansion);
                if (shown.Length == 0) continue;

                if (t.OnlyIfOwned && !HasAnyCards(t)) continue;

                var editions = new string[t.PerNoFoil];
                for (int b = 0; b < t.PerNoFoil; b++)
                    editions[b] = BorderName(t.Expansion, t.PerNoFoil == 1 ? 5 : b, t.Lists[0].IsDestiny) ?? t.FallbackEditions[Math.Min(b, t.FallbackEditions.Length - 1)];

                sets.Add(new SetDef
                {
                    Key = t.Key,
                    Name = t.Name,
                    ExpansionType = t.Expansion,
                    Lists = t.Lists,
                    Shown = shown,
                    PerNoFoil = t.PerNoFoil,
                    Editions = editions,
                });
            }
            return Catalog.Build(sets, MonsterName, MonsterTier);
        }

        private static bool HasAnyCards(SetTemplate t)
        {
            foreach (var l in t.Lists)
            {
                var list = AlbumList(l.Suffix);
                if (list == null) continue;
                foreach (var n in list)
                    if (n > 0) return true;
            }
            return false;
        }

        private static int[] ShownMonsters(int expansion)
        {
            try
            {
                var list = InventoryBase.GetShownMonsterList((ECardExpansionType)expansion);
                if (list == null) return Array.Empty<int>();
                var ids = new int[list.Count];
                for (int i = 0; i < list.Count; i++) ids[i] = (int)list[i];
                return ids;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("shown:" + expansion, $"No monster list for expansion {expansion}: {e.Message}");
                return Array.Empty<int>();
            }
        }

        /// <summary>
        /// Localized edition name from the game. 1.0+ has GetCardBorderName(expansion, border, isDestiny)
        /// (per-set names such as Ascension's); older builds only GetCardBorderName(border).
        /// </summary>
        private static string? BorderName(int expansion, int border, bool isDestiny)
        {
            try
            {
                object? result = null;
                foreach (var m in typeof(CPlayerData).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "GetCardBorderName") continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 3)
                    {
                        result = m.Invoke(null, new object[] { (ECardExpansionType)expansion, (ECardBorderType)border, isDestiny });
                        break;
                    }
                    if (ps.Length == 1 && expansion != 7) result = m.Invoke(null, new object[] { (ECardBorderType)border });
                }
                var name = result as string;
                return string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string MonsterName(int id)
        {
            try
            {
                var data = InventoryBase.GetMonsterData((EMonsterType)id);
                var name = data?.GetName();
                if (!string.IsNullOrWhiteSpace(name)) return name!;
            }
            catch
            {
                // Fall through to the enum name.
            }
            return ((EMonsterType)id).ToString();
        }

        private static Tier MonsterTier(int id)
        {
            try
            {
                var data = InventoryBase.GetMonsterData((EMonsterType)id);
                if (data != null)
                {
                    int r = (int)data.Rarity;
                    if (r >= 0 && r <= 3) return (Tier)r;
                }
            }
            catch
            {
                // Unknown rarity counts as Common.
            }
            return Tier.Common;
        }

        /// <summary>
        /// Asks the game to rebuild its shelf/storage save lists from the objects in the shop,
        /// the same step it runs before every save. Without it, displays reflect the last save.
        /// </summary>
        public static bool RefreshShelfData()
        {
            try
            {
                var shelves = UnityEngine.Object.FindObjectOfType<ShelfManager>();
                if (shelves == null || !shelves.m_FinishLoadingObjectData) return false;
                shelves.SaveInteractableObjectData();
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("shelf-refresh", "Could not refresh shelf data, using the last saved shelves: " + e.Message);
                return false;
            }
        }

        public static Collection Read(Catalog catalog)
        {
            var c = new Collection(catalog);

            foreach (var set in catalog.Sets)
                for (int li = 0; li < set.Lists.Length; li++)
                    c.AddAlbum(set, li, AlbumList(set.Lists[li].Suffix));

            foreach (var row in Safe(CPlayerData.m_GradedCardInventoryList))
                c.AddGradedAlbumRow(Compact(row));

            Containers(c, Loc.Displays, CPlayerData.m_CardShelfSaveDataList);
            Containers(c, Loc.Displays, CPlayerData.m_CardItemCombiShelfSaveDataList);
            Containers(c, Loc.Storage, CPlayerData.m_CardStorageShelfSaveDataList);
            Containers(c, Loc.Decks, CPlayerData.m_DeckCompactCardDataList);
            Containers(c, Loc.Grading, CPlayerData.m_GradeCardInProgressList);
            if (CPlayerData.m_CurrentGradeCardSubmitSet != null)
                Containers(c, Loc.Grading, new[] { CPlayerData.m_CurrentGradeCardSubmitSet });
            Containers(c, Loc.Boxes, CPlayerData.m_PackageBoxCardSaveDataList);
            Containers(c, Loc.Donation, CPlayerData.m_BulkDonationSaveDataList);
            Containers(c, Loc.Openers, CPlayerData.m_AutoPackOpenerSaveDataList);
            foreach (var card in Safe(CPlayerData.m_HoldCardDataList))
                if (card != null) c.AddPhysical(Loc.Hand, Physical(card));

            return c;
        }

        private static IEnumerable<T> Safe<T>(IEnumerable<T>? list) => list ?? Array.Empty<T>();

        private static PhysicalRec Physical(CardData d) => new PhysicalRec
        {
            Expansion = (int)d.expansionType,
            Monster = (int)d.monsterType,
            Border = (int)d.borderType,
            Foil = d.isFoil,
            Destiny = d.isDestiny,
            Grade = d.cardGrade,
        };

        private static CompactRec Compact(CompactCardDataAmount r) => new CompactRec
        {
            Expansion = (int)r.expansionType,
            SaveIndex = r.cardSaveIndex,
            Amount = r.amount,
            GradedIndex = r.gradedCardIndex,
            Destiny = r.isDestiny,
        };

        private static readonly Dictionary<Type, FieldInfo[]> CardFields = new Dictionary<Type, FieldInfo[]>();

        /// <summary>
        /// Walks save-data containers (shelves, storage, decks, grading sets, boxes) and counts
        /// every List&lt;CardData&gt; and List&lt;CompactCardDataAmount&gt; field they carry.
        /// </summary>
        private static void Containers(Collection c, Loc loc, IEnumerable? containers)
        {
            if (containers == null) return;
            foreach (var container in containers)
            {
                if (container == null) continue;
                var type = container.GetType();
                if (!CardFields.TryGetValue(type, out var fields))
                {
                    var found = new List<FieldInfo>();
                    foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        if (f.FieldType == typeof(List<CardData>) || f.FieldType == typeof(List<CompactCardDataAmount>))
                            found.Add(f);
                    fields = found.ToArray();
                    CardFields[type] = fields;
                }
                foreach (var f in fields)
                {
                    var value = f.GetValue(container);
                    if (value is List<CardData> cards)
                    {
                        foreach (var card in cards)
                            if (card != null) c.AddPhysical(loc, Physical(card));
                    }
                    else if (value is List<CompactCardDataAmount> rows)
                    {
                        foreach (var row in rows)
                            if (row != null) c.AddCompact(loc, Compact(row));
                    }
                }
            }
        }

        /// <summary>Today's market price for one copy, using the game's own pricing (null if unavailable).</summary>
        public static float? MarketPrice(Card card, int grade)
        {
            if (grade < 0 || grade > 10) return null;
            try
            {
                float p = CPlayerData.GetCardMarketPrice(card.SaveIndex, (ECardExpansionType)card.Set.ExpansionType, card.Set.Lists[card.List].IsDestiny, grade);
                return p > 0f ? p : (float?)null;
            }
            catch
            {
                return null;
            }
        }

        public static string PriceString(float value)
        {
            try
            {
                return GameInstance.GetPriceString(value);
            }
            catch
            {
                return "$" + value.ToString("N2");
            }
        }

        public static int CurrentDay()
        {
            try
            {
                return CPlayerData.m_CurrentDay;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>Hands the cursor to our panel the way the game's own menus do.</summary>
        public static InteractionPlayerController? EnterUIMode()
        {
            try
            {
                var controller = UnityEngine.Object.FindObjectOfType<InteractionPlayerController>();
                if (controller != null && !controller.IsInUIMode())
                {
                    controller.EnterUIMode();
                    return controller;
                }
            }
            catch (Exception e)
            {
                Plugin.LogOnce("ui-enter", "Could not enter UI mode: " + e.Message);
            }
            return null;
        }

        public static void ExitUIMode(InteractionPlayerController? controller)
        {
            if (controller == null) return;
            try
            {
                controller.ExitUIMode();
            }
            catch (Exception e)
            {
                Plugin.LogOnce("ui-exit", "Could not leave UI mode: " + e.Message);
            }
        }
    }
}
