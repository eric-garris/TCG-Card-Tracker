using System;
using System.Collections.Generic;
using System.Linq;

namespace TCGCardTracker.Core
{
    /// <summary>
    /// How the game's quick fill (bulk boxes, card storage shelves, donation bins) shares out spare cards, run over
    /// cards from any number of sets. Same rules as the game's own one-set version: cards with the most spares
    /// (copies above the "keep at least" number) go first, then the box is filled round-robin, each card giving all
    /// its spares at once (a random 1 to spares + 4, at most 1000, when it has 1000 or more), until the box is full
    /// or has as many different entries as it can show.
    /// Unlike the game, a card with no spares left is skipped instead of adding an empty entry.
    /// </summary>
    public static class QuickFillPlan
    {
        public readonly struct Candidate<T>
        {
            public readonly T Card;
            public readonly int Spares;

            public Candidate(T card, int spares)
            {
                Card = card;
                Spares = spares;
            }
        }

        /// <param name="candidates">Cards that pass the filters, in the order the sets list them.</param>
        /// <param name="capacity">How many more cards fit in the box.</param>
        /// <param name="maxEntries">How many entries the box can hold.</param>
        /// <param name="random">Returns a whole number from min (inclusive) to max (exclusive).</param>
        public static List<(T card, int amount)> Plan<T>(IEnumerable<Candidate<T>> candidates, int capacity, int maxEntries,
            Func<int, int, int> random)
        {
            var list = candidates.Where(c => c.Spares > 0).OrderByDescending(c => c.Spares).ToList(); // stable, like the game's insert
            var left = list.Select(c => c.Spares).ToArray();
            var result = new List<(T, int)>();
            int used = 0;
            bool progress = true;
            while (used < capacity && result.Count < maxEntries && progress)
            {
                progress = false;
                for (int k = 0; k < list.Count && used < capacity && result.Count < maxEntries; k++)
                {
                    if (left[k] <= 0) continue;
                    int take = left[k] < 1000 ? left[k] : Math.Max(1, Math.Min(1000, random(1, left[k] + 5)));
                    take = Math.Min(take, Math.Min(left[k], capacity - used));
                    if (take <= 0) continue;
                    used += take;
                    left[k] -= take;
                    result.Add((list[k].Card, take));
                    progress = true;
                }
            }
            return result;
        }

        /// <summary>
        /// The workbench's bulk box bundle, run over cards from any number of sets: <paramref name="size"/> cards taken
        /// round-robin, each card giving a random 1 to spares + 4 (at most <paramref name="perTurn"/>, never more than it
        /// has) per turn, as the game does for one set. Returns nothing when all spares together are fewer than
        /// <paramref name="size"/> (the game then says there aren't enough cards). Cards are taken in the order given;
        /// the caller shuffles them so one set doesn't fill the whole box.
        /// </summary>
        public static List<(T card, int amount)> Bundle<T>(IList<Candidate<T>> candidates, int size, int perTurn, Func<int, int, int> random)
        {
            var result = new List<(T, int)>();
            var list = candidates.Where(c => c.Spares > 0).ToList();
            if (list.Sum(c => (long)c.Spares) < size) return result;
            var left = list.Select(c => c.Spares).ToArray();
            int used = 0;
            while (used < size)
            {
                bool progress = false;
                for (int k = 0; k < list.Count && used < size; k++)
                {
                    if (left[k] <= 0) continue;
                    int take = Math.Max(1, Math.Min(perTurn, random(1, left[k] + 5)));
                    take = Math.Min(take, Math.Min(left[k], size - used));
                    used += take;
                    left[k] -= take;
                    result.Add((list[k].Card, take));
                    progress = true;
                }
                if (!progress) break;
            }
            return result;
        }
    }
}
