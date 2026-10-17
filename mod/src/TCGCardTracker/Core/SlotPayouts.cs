using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TCGCardTracker.Core
{
    /// <summary>
    /// What each row of Point And Slots' payout table pays, read from its CombinationRewards setting
    /// ("combo|weight|rarity[|expansion]; ..."): the rarities a combination gives (weight above 0) and its share of
    /// all spins, losses (LossWeight) included. Zero-weight fallback rules don't change a row.
    /// </summary>
    public static class SlotPayouts
    {
        /// <summary>The payout table's rows, top to bottom.</summary>
        public static readonly string[] Order = { "7gold", "777", "r", "pika", "psy", "slow", "mag", "vol", "shell", "star", "che3", "che2", "che1" };

        public struct Row
        {
            public string Pattern;
            public string Rarities;
            public double Percent;
            public string Label => Percent <= 0 ? "No reward" : $"{Rarities}  {Format(Percent)}";
        }

        public static List<Row> Rows(string rules, int lossWeight)
        {
            var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var names = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in (rules ?? "").Split(';'))
            {
                var parts = rule.Split(new[] { '|' }, 4);
                if (parts.Length < 3 || !decimal.TryParse(parts[1].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var weight) || weight <= 0m)
                    continue;
                string pattern = parts[0].Trim().ToLowerInvariant(), rarity = parts[2].Trim();
                totals[pattern] = (totals.TryGetValue(pattern, out var t) ? t : 0m) + weight;
                if (!names.TryGetValue(pattern, out var list)) names[pattern] = list = new List<string>();
                if (!list.Contains(rarity, StringComparer.OrdinalIgnoreCase)) list.Add(rarity);
            }
            decimal all = totals.Values.Sum() + Math.Max(0, lossWeight);
            return Order.Select(p => new Row
            {
                Pattern = p,
                Rarities = names.TryGetValue(p, out var n) ? string.Join(" / ", n) : "",
                Percent = all > 0m && totals.TryGetValue(p, out var w) ? (double)(w * 100m / all) : 0,
            }).ToList();
        }

        /// <summary>"12%", "3.5%", "0.25%": no more decimals than it needs, at most two.</summary>
        public static string Format(double percent) =>
            percent.ToString(percent >= 10 ? "0.#" : percent >= 1 ? "0.##" : "0.###", CultureInfo.InvariantCulture) + "%";
    }
}
