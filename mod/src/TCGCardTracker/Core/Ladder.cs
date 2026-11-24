using System;
using System.Collections.Generic;

namespace TCGCardTracker.Core
{
    /// <summary>One duel ladder (Tetramon or Magic): a rating, a record, and how often you beat each Tetramon deck.</summary>
    [Serializable]
    public sealed class LadderState
    {
        public int rating = Ladder.StartRating;
        public int wins, losses, draws;
        /// <summary>Tetramon only: wins and losses against each premade deck (index = the game's deck index).</summary>
        public List<int> deckWins = new List<int>();
        public List<int> deckLosses = new List<int>();

        public int Rank => Ladder.Rank(rating);

        public void Normalise()
        {
            if (rating < Ladder.StartRating) rating = Ladder.StartRating;
            deckWins ??= new List<int>();
            deckLosses ??= new List<int>();
        }
    }

    /// <summary>Unopened prize packs of one kind (the game's pack item type).</summary>
    [Serializable]
    public sealed class VaultEntry
    {
        public int itemType;
        public int count;
    }

    /// <summary>Everything the ladders keep for one save slot.</summary>
    [Serializable]
    public sealed class LadderSave
    {
        public int version = 1;
        public LadderState tetramon = new LadderState();
        public LadderState magic = new LadderState();
        public List<VaultEntry> vault = new List<VaultEntry>();
        public long packsOpened, cardsOpened;

        public void Normalise()
        {
            tetramon ??= new LadderState();
            magic ??= new LadderState();
            vault ??= new List<VaultEntry>();
            tetramon.Normalise();
            magic.Normalise();
            vault.RemoveAll(v => v == null || v.count <= 0);
        }

        public int VaultPacks()
        {
            long n = 0;
            foreach (var v in vault) n += v.count;
            return (int)Math.Min(int.MaxValue, n);
        }

        public void AddPacks(int itemType, int count)
        {
            if (count <= 0) return;
            foreach (var v in vault)
                if (v.itemType == itemType)
                {
                    v.count = (int)Math.Min(int.MaxValue, (long)v.count + count);
                    return;
                }
            vault.Add(new VaultEntry { itemType = itemType, count = count });
        }

        public void TakePacks(int itemType, int count)
        {
            foreach (var v in vault)
                if (v.itemType == itemType) v.count = Math.Max(0, v.count - count);
            vault.RemoveAll(v => v.count <= 0);
        }
    }

    public enum DuelResult { Loss, Draw, Win }

    /// <summary>
    /// The duel ladder's rules, kept free of the game so they can be tested. Rating works like chess Elo: a win against an
    /// opponent you were expected to beat gains little, an upset gains a lot, and losses take rating away. Every 50
    /// rating points is a rank (1 - 50); the rank sets how many packs a win puts in the Prize Vault.
    /// </summary>
    public static class Ladder
    {
        public const int StartRating = 1000;
        public const int PointsPerRank = 50;
        public const int MaxRank = 50;
        public const float K = 32f;

        public static int Rank(int rating) =>
            Math.Max(1, Math.Min(MaxRank, 1 + (rating - StartRating) / PointsPerRank));

        /// <summary>Rating that reaches the next rank (or the top rank's floor at rank 50).</summary>
        public static int NextRankAt(int rank) => StartRating + Math.Min(rank, MaxRank - 1) * PointsPerRank;

        /// <summary>
        /// Rating after a game. <paramref name="expected"/> = your chance of winning (0 - 1); never below the start rating,
        /// so rank 1 can't be lost further.
        /// </summary>
        public static int Rate(int rating, float expected, DuelResult result)
        {
            if (float.IsNaN(expected)) expected = 0.5f;
            expected = Math.Max(0.02f, Math.Min(0.98f, expected));
            float score = result == DuelResult.Win ? 1f : result == DuelResult.Draw ? 0.5f : 0f;
            int change = (int)Math.Round(K * (score - expected), MidpointRounding.AwayFromZero);
            return Math.Max(StartRating, rating + change);
        }

        /// <summary>Packs a win pays at a rank: growth^(rank - 1) times scale, at least 1.</summary>
        public static int PrizePacks(int rank, float growth, float scale)
        {
            if (float.IsNaN(growth) || growth < 1f) growth = 1f;
            if (float.IsNaN(scale) || scale <= 0f) return 0;
            rank = Math.Max(1, Math.Min(MaxRank, rank));
            double packs = Math.Pow(growth, rank - 1) * scale;
            return (int)Math.Max(1, Math.Min(1_000_000, Math.Round(packs)));
        }

        /// <summary>Packs for a game result: a win pays in full, a draw half, a loss nothing.</summary>
        public static int ResultPacks(DuelResult result, int rank, float growth, float scale) =>
            result == DuelResult.Win ? PrizePacks(rank, growth, scale)
            : result == DuelResult.Draw ? PrizePacks(rank, growth, scale) / 2
            : 0;

        /// <summary>Tournament placement prize, in wins' worth of packs: 1st 5, 2nd 3, 3rd 2, everyone else 1.</summary>
        public static int PlacementMultiplier(int placementIndex) =>
            placementIndex == 0 ? 5 : placementIndex == 1 ? 3 : placementIndex == 2 ? 2 : 1;

        /// <summary>TCG Studio Magic deck power for a rank: -1 (weak) at rank 1 to 1 (strong) at rank 50.</summary>
        public static double MagicPower(int rank) =>
            -1.0 + 2.0 * (Math.Max(1, Math.Min(MaxRank, rank)) - 1) / (MaxRank - 1);

        /// <summary>Your smoothed chance of beating a Tetramon deck from your record against it (unplayed = 50%).</summary>
        public static float DeckWinChance(LadderState s, int deck)
        {
            int w = deck < s.deckWins.Count ? s.deckWins[deck] : 0;
            int l = deck < s.deckLosses.Count ? s.deckLosses[deck] : 0;
            return (w + 1f) / (w + l + 2f);
        }

        /// <summary>
        /// Picks a Tetramon deck for a rank: decks are ordered from the one you beat most often to the one you beat least,
        /// rank 1 aims at the easiest, rank 50 at the hardest, and <paramref name="roll"/> (0 - 1) picks one of the up to
        /// three decks around that aim so opponents still vary.
        /// </summary>
        public static int PickDeck(LadderState s, int deckCount, int rank, double roll)
        {
            if (deckCount <= 1) return 0;
            var order = new List<int>();
            for (int i = 0; i < deckCount; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = DeckWinChance(s, b).CompareTo(DeckWinChance(s, a));
                return c != 0 ? c : a.CompareTo(b);
            });
            double t = (Math.Max(1, Math.Min(MaxRank, rank)) - 1) / (double)(MaxRank - 1);
            int aim = (int)Math.Round(t * (deckCount - 1));
            int lo = Math.Max(0, aim - 1), hi = Math.Min(deckCount - 1, aim + 1);
            if (double.IsNaN(roll) || roll < 0) roll = 0;
            int pick = lo + Math.Min(hi - lo, (int)(roll * (hi - lo + 1)));
            return order[pick];
        }

        /// <summary>Counts a Tetramon game against a deck.</summary>
        public static void RecordDeck(LadderState s, int deck, DuelResult result)
        {
            if (deck < 0 || deck > 1000 || result == DuelResult.Draw) return;
            while (s.deckWins.Count <= deck) s.deckWins.Add(0);
            while (s.deckLosses.Count <= deck) s.deckLosses.Add(0);
            if (result == DuelResult.Win) s.deckWins[deck]++;
            else s.deckLosses[deck]++;
        }

        /// <summary>Counts the game in the record.</summary>
        public static void Record(LadderState s, DuelResult result)
        {
            if (result == DuelResult.Win) s.wins++;
            else if (result == DuelResult.Draw) s.draws++;
            else s.losses++;
        }
    }
}
