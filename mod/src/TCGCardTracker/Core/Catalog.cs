using System;
using System.Collections.Generic;

namespace TCGCardTracker.Core
{
    public enum Tier
    {
        Common,
        Rare,
        Epic,
        Legendary,
    }

    /// <summary>One list family in the player data. Ghost has two (White and Black).</summary>
    public sealed class ListDef
    {
        public string Suffix = "";
        public bool IsDestiny;
        public string Label = "";
    }

    public sealed class SetDef
    {
        public string Key = "";
        public string Name = "";
        public int ExpansionType;
        public ListDef[] Lists = Array.Empty<ListDef>();
        /// <summary>Monster ids in the game's shown-monster order (this order defines save indices).</summary>
        public int[] Shown = Array.Empty<int>();
        /// <summary>Border slots per monster before foil: 6, or 1 for Ghost.</summary>
        public int PerNoFoil = 6;
        public string[] Editions = Array.Empty<string>();

        public int PerMonster => PerNoFoil * 2;
        public int CardsPerList => Shown.Length * PerMonster;
    }

    public sealed class Card
    {
        public int Index;
        public SetDef Set = null!;
        public int List;
        public int SaveIndex;
        public int MonsterId;
        /// <summary>1-based position in the shown list (the binder number).</summary>
        public int Num;
        public string Name = "";
        public Tier Tier;
        public int Edition;
        public string EditionName = "";
        public bool Foil;
        /// <summary>Drill-down level 1: edition, or White/Black for Ghost.</summary>
        public string Group = "";

        public string Label
        {
            get
            {
                var g = Set.Lists.Length > 1 ? Group : EditionName;
                return Foil ? $"{Name} · {g} · Foil" : $"{Name} · {g}";
            }
        }
    }

    /// <summary>
    /// Every collectible card variant, laid out the way the game indexes them
    /// (CPlayerData.GetCardSaveIndex): shownPos * perMonster + border + (foil ? perNoFoil : 0).
    /// </summary>
    public sealed class Catalog
    {
        public readonly List<Card> Cards = new List<Card>();
        public readonly List<SetDef> Sets = new List<SetDef>();
        private readonly Dictionary<string, List<Card>> _bySet = new Dictionary<string, List<Card>>();
        private readonly Dictionary<string, List<string>> _groups = new Dictionary<string, List<string>>();
        private readonly Dictionary<long, Card> _byIndex = new Dictionary<long, Card>();
        private readonly Dictionary<int, SetDef> _byExpansion = new Dictionary<int, SetDef>();

        public static Catalog Build(IEnumerable<SetDef> sets, Func<int, string> monsterName, Func<int, Tier> monsterTier)
        {
            var cat = new Catalog();
            foreach (var set in sets)
            {
                var setCards = new List<Card>();
                for (int li = 0; li < set.Lists.Length; li++)
                {
                    var list = set.Lists[li];
                    for (int pos = 0; pos < set.Shown.Length; pos++)
                    {
                        int monster = set.Shown[pos];
                        string name = monsterName(monster);
                        Tier tier = monsterTier(monster);
                        for (int v = 0; v < set.PerMonster; v++)
                        {
                            int edition = v % set.PerNoFoil;
                            string editionName = edition < set.Editions.Length ? set.Editions[edition] : $"Border {edition}";
                            var card = new Card
                            {
                                Index = cat.Cards.Count,
                                Set = set,
                                List = li,
                                SaveIndex = pos * set.PerMonster + v,
                                MonsterId = monster,
                                Num = pos + 1,
                                Name = name,
                                Tier = tier,
                                Edition = edition,
                                EditionName = editionName,
                                Foil = v >= set.PerNoFoil,
                                Group = set.Lists.Length > 1 ? list.Label : editionName,
                            };
                            cat.Cards.Add(card);
                            setCards.Add(card);
                            cat._byIndex[Key(set.ExpansionType, li, card.SaveIndex)] = card;
                        }
                    }
                }
                cat.Sets.Add(set);
                cat._bySet[set.Key] = setCards;
                cat._byExpansion[set.ExpansionType] = set;
                var groups = new List<string>();
                if (set.Lists.Length > 1)
                    foreach (var l in set.Lists) groups.Add(l.Label);
                else
                    groups.AddRange(set.Editions);
                cat._groups[set.Key] = groups;
            }
            return cat;
        }

        private static long Key(int expansion, int list, int saveIndex) => ((long)expansion << 40) | ((long)list << 32) | (uint)saveIndex;

        public IReadOnlyList<Card> CardsOf(SetDef set) => _bySet.TryGetValue(set.Key, out var l) ? l : (IReadOnlyList<Card>)Array.Empty<Card>();

        public IReadOnlyList<string> GroupsOf(SetDef set) => _groups.TryGetValue(set.Key, out var g) ? g : (IReadOnlyList<string>)Array.Empty<string>();

        public SetDef? SetForExpansion(int expansionType) => _byExpansion.TryGetValue(expansionType, out var s) ? s : null;

        private static int ListIndex(SetDef set, bool isDestiny) => set.Lists.Length > 1 && isDestiny ? 1 : 0;

        /// <summary>Compact rows: (expansionType, cardSaveIndex, isDestiny). isDestiny only matters for Ghost.</summary>
        public Card? FromCompact(int expansionType, int saveIndex, bool isDestiny)
        {
            var set = SetForExpansion(expansionType);
            if (set == null) return null;
            return _byIndex.TryGetValue(Key(expansionType, ListIndex(set, isDestiny), saveIndex), out var c) ? c : null;
        }

        /// <summary>Physical CardData identity. Unknown monsters return null instead of the game's index-0 fallback.</summary>
        public Card? FromPhysical(int expansionType, int monsterType, int borderType, bool isFoil, bool isDestiny)
        {
            var set = SetForExpansion(expansionType);
            if (set == null) return null;
            int pos = Array.IndexOf(set.Shown, monsterType);
            if (pos < 0) return null;
            int border = set.PerNoFoil == 1 ? 0 : borderType;
            if (border < 0 || border >= set.PerNoFoil) return null;
            int saveIndex = pos * set.PerMonster + border + (isFoil ? set.PerNoFoil : 0);
            return _byIndex.TryGetValue(Key(expansionType, ListIndex(set, isDestiny), saveIndex), out var c) ? c : null;
        }
    }
}
