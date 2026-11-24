using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>The Prizes tab: both duel ladders, and the Prize Vault's unopened packs with buttons to open them.</summary>
    internal sealed partial class TrackerWindow
    {
        private Vector2 _prizeScroll;

        private void DrawPrizes()
        {
            if (!GameReader.IsReady())
            {
                GUILayout.Space(20);
                GUILayout.Label("Load your shop to see your ladders and Prize Vault.", Styles.Body);
                return;
            }
            var data = DuelLadder.Data;
            GUILayout.Space(6);
            GUILayout.Label("Duel ladders", Styles.Header);
            DrawLadder("Tetramon", data.tetramon);
            DrawLadder("Magic (TCG Studio)", data.magic);
            GUILayout.Label("Wins and tournament placings put packs in the vault; losses and quits lower your rating. Every 50 rating is a rank.", Styles.Muted);

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Prize Vault: {data.VaultPacks():N0} unopened packs", Styles.Header);
            GUILayout.FlexibleSpace();
            GUI.enabled = PrizeVault.CanOpen && data.VaultPacks() > 0;
            if (GUILayout.Button("Open all", GUILayout.Width(110))) PrizeVault.Open();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (PrizeVault.Opening)
                GUILayout.Label($"Opening… {PrizeVault.Done:N0} / {PrizeVault.Total:N0} packs (cards are added when all are rolled)", Styles.Body);
            else if (data.packsOpened > 0)
                GUILayout.Label($"Opened so far: {data.packsOpened:N0} packs, {data.cardsOpened:N0} cards.", Styles.Muted);

            _prizeScroll = GUILayout.BeginScrollView(_prizeScroll);
            if (data.vault.Count == 0) GUILayout.Label("No packs waiting. Win games at a play table to earn some.", Styles.Muted);
            foreach (var v in data.vault.ToArray())
            {
                GUILayout.BeginHorizontal(GUILayout.Height(TextRowHeight));
                GUILayout.Label(PrizeVault.PackName(v.itemType), Styles.RowBody, GUILayout.Width(320));
                GUILayout.Label($"x{v.count:N0}", Styles.RowBody, GUILayout.Width(100));
                GUI.enabled = PrizeVault.CanOpen;
                if (GUILayout.Button("Open", GUILayout.Width(80))) PrizeVault.Open(v.itemType);
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Packs open instantly, with no opening screen, straight into your binder. Saved with your game.", Styles.Muted);
        }

        private static void DrawLadder(string name, LadderState s)
        {
            int rank = s.Rank;
            int packs = Ladder.PrizePacks(rank, Plugin.LadderPrizeGrowth.Value, Plugin.LadderPrizeScale.Value);
            string next = rank >= Ladder.MaxRank ? "top rank" : $"next rank at {Ladder.NextRankAt(rank)}";
            GUILayout.BeginHorizontal(GUILayout.Height(TextRowHeight));
            GUILayout.Label(name, Styles.RowStrong, GUILayout.Width(170));
            GUILayout.Label($"Rank {rank}  ·  rating {s.rating} ({next})", Styles.RowBody, GUILayout.Width(330));
            GUILayout.Label($"{s.wins}W {s.losses}L{(s.draws > 0 ? $" {s.draws}D" : "")}", Styles.RowBody, GUILayout.Width(120));
            GUILayout.Label($"a win pays {packs:N0} pack{(packs == 1 ? "" : "s")}", Styles.RowBody);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
