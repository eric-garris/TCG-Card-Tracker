using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// Duel ladders: every game you play at a play table (vanilla Tetramon, or TCG Studio's Magic) changes that game's
    /// rating, and wins put packs in the Prize Vault, more the higher your rank. Quitting a game counts as a loss. As you
    /// climb, opponents get harder: Magic through TCG Studio's deck power, Tetramon by dealing the premade decks you beat
    /// least often. The ladders and vault are saved with the game, per save slot, in their own file next to the cfg.
    /// Game members newer than the reference build are reached by name, so a game update only switches parts off.
    /// </summary>
    internal static class DuelLadder
    {
        private static LadderSave _save = new LadderSave();
        internal static LadderSave Data => _save;

        private static bool _gameRunning;
        private static bool _gameMagic;
        private static bool _lastTournamentMagic;
        private static bool _setDeck;
        private static bool _dayOverBefore;

        private static Func<bool> _studioActive = () => false;
        private static FieldInfo? _debugDeck, _isRematch, _effectSo, _enemySet;

        internal static void Install()
        {
            if (!Plugin.LadderEnabled.Value) return;
            Harmony harmony;
            try
            {
                harmony = new Harmony(Plugin.Guid + ".ladder");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not start Harmony; the duel ladder is off: " + e.Message);
                return;
            }
            try
            {
                var game = typeof(PlayTableGame);
                _debugDeck = AccessTools.Field(game, "m_DebugEnemyDeckIndex");
                _isRematch = AccessTools.Field(game, "m_IsRematch");
                _effectSo = AccessTools.Field(game, "m_PlayCardEffect_SO");
                _enemySet = AccessTools.Field(game, "m_PlayCardSetEnemy");

                var session = AccessTools.TypeByName("TCGCustomCards.Hooks.TableSession");
                var active = session == null ? null : AccessTools.Property(session, "Active");
                if (active != null) _studioActive = () => (bool)active.GetValue(null);

                Patch(harmony, game, "DelayStart", nameof(BeforeStart), prefix: true, first: true);
                Patch(harmony, game, "ReportWinner", nameof(AfterReportWinner), prefix: false);
                Patch(harmony, game, "FinishLeaveGame", nameof(BeforeLeave), prefix: true, first: true);
                var manager = typeof(CustomerManager);
                Patch(harmony, manager, "OnCustomerFinishTournamentRound", nameof(BeforeRoundFinished), prefix: true);
                Patch(harmony, manager, "OnCustomerFinishTournamentRound", nameof(AfterRoundFinished), prefix: false);
                Patch(harmony, AccessTools.TypeByName("CGameManager"), "LoadMainLevelAsync", nameof(BeforeLoadLevel), prefix: true);
                Patch(harmony, AccessTools.TypeByName("CGameManager"), "LoadData", nameof(AfterLoadData), prefix: false);
                Patch(harmony, AccessTools.TypeByName("CSaveLoad"), "Save", nameof(AfterSave), prefix: false);
                var mode = AccessTools.TypeByName("TCGCustomCards.Runtime.Mtg.MtgMode");
                var power = mode == null ? null : AccessTools.Method(mode, "Power");
                if (power != null && power.ReturnType == typeof(double))
                    harmony.Patch(power, postfix: new HarmonyMethod(typeof(DuelLadder), nameof(AfterMagicPower)));
                else if (mode != null)
                    Plugin.Log.LogWarning("Duel ladder: TCG Studio changed; Magic opponents won't follow your rank.");
                PrizeVault.Install(harmony);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not set up the duel ladder: " + e.Message);
            }
        }

        private static void Patch(Harmony harmony, Type? type, string method, string handler, bool prefix, bool first = false)
        {
            var target = type == null ? null : AccessTools.Method(type, method);
            if (target == null)
            {
                Plugin.Log.LogWarning($"Duel ladder: could not find the game's {type?.Name}.{method}; that part is off.");
                return;
            }
            var patch = new HarmonyMethod(typeof(DuelLadder), handler);
            if (first) patch.priority = Priority.First;
            if (prefix) harmony.Patch(target, prefix: patch);
            else harmony.Patch(target, postfix: patch);
        }

        // ---------------------------------------------------------------- games

        /// <summary>Prefix on PlayTableGame.DelayStart (each game and rematch; TCG Studio runs Magic in its place).</summary>
        private static void BeforeStart(PlayTableGame __instance)
        {
            try
            {
                _gameRunning = true;
                _gameMagic = _studioActive();
                if (InTournament()) _lastTournamentMagic = _gameMagic;
                if (_gameMagic || _debugDeck == null) return;
                bool rematch = _isRematch != null && (bool)_isRematch.GetValue(__instance);
                if (!Plugin.LadderDifficulty.Value || rematch)
                {
                    // A rematch keeps the deck already dealt; with difficulty off the game deals at random again.
                    if (_setDeck) _debugDeck.SetValue(__instance, -1);
                    _setDeck = false;
                    return;
                }
                int count = DeckCount(__instance);
                if (count <= 1) return;
                // Vanilla deals Random.Range(0, count - 1), never the last deck in the list; the same decks are used here.
                int deck = Ladder.PickDeck(_save.tetramon, count - 1, _save.tetramon.Rank, UnityEngine.Random.value);
                _debugDeck.SetValue(__instance, deck);
                _setDeck = true;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("ladder-start", "Duel ladder could not set up a game: " + e.Message);
            }
        }

        /// <summary>Postfix on PlayTableGame.ReportWinner: every game's result (Tetramon, and Magic through TCG Studio).</summary>
        private static void AfterReportWinner(PlayTableGame __instance, bool isPlayerWin, bool isDraw)
        {
            try
            {
                bool magic = _gameRunning ? _gameMagic : _studioActive();
                _gameRunning = false;
                var result = isDraw ? DuelResult.Draw : isPlayerWin ? DuelResult.Win : DuelResult.Loss;
                Finish(result, magic, magic ? -1 : EnemyDeck(__instance), quit: false);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("ladder-result", "Duel ladder could not count a game: " + e);
            }
        }

        /// <summary>
        /// Prefix on PlayTableGame.FinishLeaveGame, before TCG Studio's: leaving while a game is still on (the quit screen,
        /// or standing up mid-game in Magic, which reports no result) counts as a loss.
        /// </summary>
        private static void BeforeLeave(PlayTableGame __instance)
        {
            try
            {
                if (!_gameRunning) return;
                _gameRunning = false;
                Finish(DuelResult.Loss, _gameMagic, _gameMagic ? -1 : EnemyDeck(__instance), quit: true);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("ladder-leave", "Duel ladder could not count a quit game: " + e.Message);
            }
        }

        private static void Finish(DuelResult result, bool magic, int deck, bool quit)
        {
            var state = magic ? _save.magic : _save.tetramon;
            string ladder = magic ? "Magic" : "Tetramon";
            int rank = state.Rank, before = state.rating;
            float expected = magic || deck < 0 ? 0.5f : Ladder.DeckWinChance(state, deck);
            state.rating = Ladder.Rate(state.rating, expected, result);
            Ladder.Record(state, result);
            if (!magic) Ladder.RecordDeck(state, deck, result);
            int packs = Ladder.ResultPacks(result, rank, Plugin.LadderPrizeGrowth.Value, Plugin.LadderPrizeScale.Value);
            int added = packs > 0 ? PrizeVault.AddPrize(packs) : 0;
            int change = state.rating - before;
            string what = result == DuelResult.Win ? "won" : result == DuelResult.Draw ? "drawn" : quit ? "quit (counts as a loss)" : "lost";
            Plugin.Log.LogInfo($"Duel ladder: {ladder} game {what}{(deck >= 0 ? $" vs deck {deck}" : "")}; rating {before} -> {state.rating}, rank {rank} -> {state.Rank}, +{added} packs to the Prize Vault.");
            if (!Plugin.NewCardPopup.Value) return;
            var lines = new List<string>
            {
                $"Rating {state.rating} ({(change >= 0 ? "+" : "")}{change})  ·  rank {state.Rank} of {Ladder.MaxRank}",
            };
            if (state.Rank > rank) lines.Add($"Rank up! Wins now pay {Ladder.PrizePacks(state.Rank, Plugin.LadderPrizeGrowth.Value, Plugin.LadderPrizeScale.Value):N0} packs");
            else if (state.Rank < rank) lines.Add($"Down to rank {state.Rank}");
            if (added > 0) lines.Add($"+{added:N0} packs in the Prize Vault (F7 > Prizes)");
            PackWatcher.Toast.ShowLines($"{ladder} game {(quit ? "quit" : what)}", lines, left: true);
        }

        /// <summary>
        /// Postfix on TCG Studio's MtgMode.Power (customer deck strength, -1 weak to 1 strong): follows your Magic rank
        /// instead of TCG Studio's own setting while ladder difficulty is on.
        /// </summary>
        private static void AfterMagicPower(ref double __result)
        {
            if (Plugin.LadderDifficulty.Value) __result = Ladder.MagicPower(_save.magic.Rank);
        }

        // ---------------------------------------------------------------- tournaments

        private static bool InTournament()
        {
            try
            {
                return AccessTools.Field(typeof(CPlayerData), "m_IsPlayerRegisteredForTournament")?.GetValue(null) is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static bool DayOver()
        {
            var data = AccessTools.Field(typeof(CPlayerData), "m_TournamentData")?.GetValue(null);
            return data != null && Traverse.Create(data).Field("m_IsTournamentDayOver").GetValue<bool>();
        }

        private static void BeforeRoundFinished()
        {
            try
            {
                _dayOverBefore = DayOver();
            }
            catch
            {
                _dayOverBefore = true;
            }
        }

        /// <summary>Postfix on CustomerManager.OnCustomerFinishTournamentRound: once the last round ends, the placement prize.</summary>
        private static void AfterRoundFinished()
        {
            try
            {
                if (_dayOverBefore || !DayOver() || !InTournament()) return;
                var player = AccessTools.Field(typeof(CPlayerData), "m_PlayerTournamentData")?.GetValue(null);
                if (player == null) return;
                int place = Traverse.Create(player).Field("m_TournamentPlacementIndex").GetValue<int>();
                var state = _lastTournamentMagic ? _save.magic : _save.tetramon;
                int multiplier = Ladder.PlacementMultiplier(place);
                int packs = multiplier * Ladder.PrizePacks(state.Rank, Plugin.LadderPrizeGrowth.Value, Plugin.LadderPrizeScale.Value);
                int added = PrizeVault.AddPrize(packs);
                string ladder = _lastTournamentMagic ? "Magic" : "Tetramon";
                Plugin.Log.LogInfo($"Duel ladder: {ladder} tournament finished in place {place + 1}; +{added} packs ({multiplier} wins' worth) to the Prize Vault.");
                PackWatcher.Toast.ShowLines($"Tournament: {Ordinal(place + 1)} place",
                    new List<string> { $"+{added:N0} packs in the Prize Vault (F7 > Prizes)", $"{ladder} rank {state.Rank}: {multiplier} wins' worth" }, left: true);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("ladder-tournament", "Duel ladder could not give the tournament prize: " + e.Message);
            }
        }

        private static string Ordinal(int n) =>
            n % 100 >= 11 && n % 100 <= 13 ? n + "th" : (n % 10) switch { 1 => n + "st", 2 => n + "nd", 3 => n + "rd", _ => n + "th" };

        // ---------------------------------------------------------------- decks

        private static int DeckCount(PlayTableGame game)
        {
            var so = _effectSo?.GetValue(game);
            var list = so == null ? null : Traverse.Create(so).Field("m_PlayCardDeckDataList").GetValue() as IList;
            return list?.Count ?? 0;
        }

        private static int EnemyDeck(PlayTableGame game)
        {
            try
            {
                var set = _enemySet?.GetValue(game);
                return set == null ? -1 : Traverse.Create(set).Field("m_DeckIndex").GetValue<int>();
            }
            catch
            {
                return -1;
            }
        }

        // ---------------------------------------------------------------- saving

        private static string Folder => Path.Combine(Paths.ConfigPath, "tcgcardtracker.ladder");
        private static string FileFor(int slot) => Path.Combine(Folder, $"slot{slot}.json");

        /// <summary>Prefix on CGameManager.LoadMainLevelAsync: a new game or another save starts from a clean ladder.</summary>
        private static void BeforeLoadLevel()
        {
            _save = new LadderSave();
            _gameRunning = false;
            PrizeVault.Cancel();
        }

        /// <summary>Postfix on CGameManager.LoadData: reads that save slot's ladders and vault.</summary>
        private static void AfterLoadData(object __instance)
        {
            try
            {
                int slot = Traverse.Create(__instance).Field("m_CurrentSaveLoadSlotSelectedIndex").GetValue<int>();
                var path = FileFor(slot);
                var loaded = File.Exists(path) ? JsonUtility.FromJson<LadderSave>(File.ReadAllText(path)) : null;
                _save = loaded ?? new LadderSave();
                _save.Normalise();
                Plugin.Log.LogInfo($"Duel ladder: slot {slot}: Tetramon rating {_save.tetramon.rating}, Magic rating {_save.magic.rating}, {_save.VaultPacks():N0} packs in the Prize Vault.");
            }
            catch (Exception e)
            {
                _save = new LadderSave();
                Plugin.Log.LogWarning("Duel ladder could not read its save; starting fresh for this session: " + e.Message);
            }
        }

        /// <summary>Postfix on CSaveLoad.Save: the ladders and vault are saved with the game, to the same slot.</summary>
        private static void AfterSave(int saveSlotIndex)
        {
            try
            {
                // Only a loaded shop's save: never write a blank ladder over a slot from the title screen.
                if (!GameReader.IsReady()) return;
                Directory.CreateDirectory(Folder);
                var path = FileFor(saveSlotIndex);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(_save, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("ladder-save", "Duel ladder could not save: " + e.Message);
            }
        }
    }
}
