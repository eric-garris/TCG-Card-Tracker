using System;
using System.Collections.Generic;
using System.Text;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// The in-game panel (IMGUI). Overview: sets x (Ungraded, Grade 1-10, Any grade, Any form),
    /// each cell "owned / total" plus copies, expandable to edition and foil. Clicking a cell
    /// opens the card list for it (missing or owned).
    /// </summary>
    internal sealed class TrackerWindow
    {
        private const int WindowId = 0x54434754; // "TCGT"
        private const float NameWidth = 210f;
        private const float CellWidth = 64f;
        private const float RowHeight = 36f;
        private const float ListRowHeight = 24f;

        private static readonly Column[] GridColumns =
        {
            Column.Ungraded,
            new Column(1), new Column(2), new Column(3), new Column(4), new Column(5),
            new Column(6), new Column(7), new Column(8), new Column(9), new Column(10),
            Column.Graded, Column.Any,
        };

        private bool _open;
        private Rect _rect;
        private bool _placed;
        private int _tab;
        private Vector2 _gridScroll;
        private Vector2 _listScroll;
        private InteractionPlayerController? _uiController;

        private Catalog? _catalog;
        private Collection? _collection;
        private int[] _totals = Array.Empty<int>();
        private float _refreshedAt;
        private float _nextAutoRefresh;
        private bool _shelvesLive;
        private string _status = "";

        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly bool[] _tiers = { true, true, true, true };

        // Card list selection.
        private SetDef? _selSet;
        private string? _selGroup;
        private int _selFoil = -1; // -1 all, 0 non-foil, 1 foil
        private Column _selCol = Column.Ungraded;
        private bool _showOwned;

        // Caches, rebuilt when data or filters change.
        private List<GridRow>? _rows;
        private List<Card>? _list;
        private string _summary = "";

        private sealed class GridRow
        {
            public string Id = "";
            public string Label = "";
            public int Depth;
            public SetDef Set = null!;
            public string? Group;
            public int Foil = -1;
            public bool HasChildren;
            public Agg[] Cells = Array.Empty<Agg>();
        }

        // ---------- Lifecycle ----------

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        private void Open()
        {
            _open = true;
            _uiController = GameReader.EnterUIMode();
            Refresh(full: true);
        }

        private void Close()
        {
            _open = false;
            GameReader.ExitUIMode(_uiController);
            _uiController = null;
        }

        /// <summary>On scene changes: drop state tied to the old scene without touching its objects.</summary>
        public void ForceClose()
        {
            _open = false;
            _uiController = null;
            _catalog = null;
            _collection = null;
            _rows = null;
            _list = null;
        }

        /// <summary>Called every frame: keeps binder counts current while the panel is open.</summary>
        public void Tick()
        {
            if (!_open || Time.realtimeSinceStartup < _nextAutoRefresh) return;
            Refresh(full: false);
        }

        private void Refresh(bool full)
        {
            _nextAutoRefresh = Time.realtimeSinceStartup + 2f;
            if (!GameReader.IsReady())
            {
                _catalog = null;
                _collection = null;
                _status = "Open your shop (load a save) to see your collection.";
                return;
            }
            try
            {
                if (full)
                {
                    _catalog = GameReader.BuildCatalog();
                    _shelvesLive = Plugin.LiveShelves.Value && GameReader.RefreshShelfData();
                }
                if (_catalog == null) return;
                _collection = GameReader.Read(_catalog);
                _refreshedAt = Time.realtimeSinceStartup;
                _status = "";
                Recount();
            }
            catch (Exception e)
            {
                _status = "Could not read your collection: " + e.Message;
                Plugin.Log.LogError(e);
            }
        }

        private void Recount()
        {
            if (_collection == null) return;
            _totals = _collection.Combine(Plugin.IncludedLocations());
            _rows = null;
            _list = null;
        }

        // ---------- Data helpers ----------

        private bool TierOn(Card c) => _tiers[(int)c.Tier];

        private IEnumerable<Card> Filtered(SetDef set, string? group, int foil)
        {
            foreach (var c in _catalog!.CardsOf(set))
            {
                if (!TierOn(c)) continue;
                if (group != null && c.Group != group) continue;
                if (foil >= 0 && c.Foil != (foil == 1)) continue;
                yield return c;
            }
        }

        private Agg[] CellsFor(SetDef set, string? group, int foil)
        {
            var cards = new List<Card>(Filtered(set, group, foil));
            var cells = new Agg[GridColumns.Length];
            for (int i = 0; i < GridColumns.Length; i++) cells[i] = Aggregate.Of(cards, _totals, GridColumns[i]);
            return cells;
        }

        private List<GridRow> Rows()
        {
            if (_rows != null) return _rows;
            var rows = new List<GridRow>();
            foreach (var set in _catalog!.Sets)
            {
                var id = set.Key;
                rows.Add(new GridRow { Id = id, Label = set.Name, Depth = 0, Set = set, HasChildren = true, Cells = CellsFor(set, null, -1) });
                if (!_expanded.Contains(id)) continue;
                foreach (var g in _catalog.GroupsOf(set))
                {
                    var gid = id + "/" + g;
                    rows.Add(new GridRow { Id = gid, Label = g, Depth = 1, Set = set, Group = g, HasChildren = true, Cells = CellsFor(set, g, -1) });
                    if (!_expanded.Contains(gid)) continue;
                    rows.Add(new GridRow { Id = gid + "/0", Label = "Non-foil", Depth = 2, Set = set, Group = g, Foil = 0, Cells = CellsFor(set, g, 0) });
                    rows.Add(new GridRow { Id = gid + "/1", Label = "Foil", Depth = 2, Set = set, Group = g, Foil = 1, Cells = CellsFor(set, g, 1) });
                }
            }

            // Header summary across all shown sets.
            var all = new List<Card>();
            foreach (var set in _catalog.Sets) all.AddRange(Filtered(set, null, -1));
            var any = Aggregate.Of(all, _totals, Column.Any);
            var graded = Aggregate.Of(all, _totals, Column.Graded);
            var gem = Aggregate.Of(all, _totals, new Column(10));
            float ungradedValue = 0, gradedValue = 0;
            foreach (var c in all)
            {
                for (int col = 0; col <= 10; col++)
                {
                    int n = _totals[c.Index * Collection.Cols + col];
                    if (n == 0) continue;
                    var p = GameReader.MarketPrice(c, col);
                    if (p == null) continue;
                    if (col == 0) ungradedValue += p.Value * n;
                    else gradedValue += p.Value * n;
                }
            }
            _summary = $"Collected {any.Owned:N0} / {any.Total:N0} ({Pct(any.Owned, any.Total)})   ·   Graded copies {graded.Copies:N0}   ·   Gem Mint {gem.Copies:N0}   ·   Value {GameReader.PriceString(ungradedValue + gradedValue)} ({GameReader.PriceString(ungradedValue)} ungraded, {GameReader.PriceString(gradedValue)} graded)";
            _rows = rows;
            return rows;
        }

        private List<Card> CardList()
        {
            if (_list != null) return _list;
            var list = new List<Card>();
            if (_selSet != null)
            {
                foreach (var c in Filtered(_selSet, _selGroup, _selFoil))
                {
                    bool owned = Aggregate.Copies(_totals, c, _selCol) > 0;
                    if (owned == _showOwned) list.Add(c);
                }
            }
            _list = list;
            return list;
        }

        private static string Pct(int owned, int total)
        {
            if (total == 0) return "-";
            double p = owned * 100.0 / total;
            if (p > 0 && p < 0.1) return "<0.1%";
            if (p < 100 && p > 99.9) return ">99.9%";
            return p.ToString(p >= 10 || p == 0 ? "0" : "0.0") + "%";
        }

        private string OwnedAs(Card c)
        {
            var sb = new StringBuilder();
            for (int col = 0; col < Collection.Cols; col++)
            {
                int n = _totals[c.Index * Collection.Cols + col];
                if (n == 0) continue;
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(col == 0 ? "Ungraded" : col == Collection.ColOther ? "Other" : "G" + col).Append(" x").Append(n);
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        // ---------- Drawing ----------

        public void Draw()
        {
            if (!_open) return;
            Styles.Ensure();

            float scale = Mathf.Clamp(Plugin.UiScale.Value, 0.75f, 2f);
            var saved = GUI.matrix;
            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                float sw = Screen.width / scale, sh = Screen.height / scale;
                if (!_placed)
                {
                    float w = Mathf.Min(NameWidth + CellWidth * GridColumns.Length + 60f, sw - 40f);
                    float h = Mathf.Min(700f, sh - 40f);
                    _rect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
                    _placed = true;
                }
                _rect = GUILayout.Window(WindowId, _rect, DrawWindow, $"{Plugin.Name}  ·  {Plugin.ToggleKey.Value} to close", Styles.Window);
                _rect.x = Mathf.Clamp(_rect.x, -_rect.width + 80f, sw - 80f);
                _rect.y = Mathf.Clamp(_rect.y, 0f, sh - 40f);
            }
            catch (ExitGUIException)
            {
                throw; // IMGUI control flow, not an error.
            }
            catch (Exception e)
            {
                Plugin.LogOnce("draw", "Tracker window failed to draw: " + e);
            }
            finally
            {
                GUI.matrix = saved;
            }
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_tab == 0, "Overview", Styles.Tab, GUILayout.Width(110))) _tab = 0;
            if (GUILayout.Toggle(_tab == 1, "Card list", Styles.Tab, GUILayout.Width(110))) _tab = 1;
            GUILayout.FlexibleSpace();
            if (_collection != null)
            {
                int day = GameReader.CurrentDay();
                GUILayout.Label((day >= 0 ? $"Day {day}  ·  " : "") + (_shelvesLive ? "live" : "shelves as of last save"), Styles.Muted);
            }
            if (GUILayout.Button("Refresh", GUILayout.Width(80))) Refresh(full: true);
            if (GUILayout.Button("Close", GUILayout.Width(70))) Close();
            GUILayout.EndHorizontal();

            if (_catalog == null || _collection == null)
            {
                GUILayout.Space(20);
                GUILayout.Label(_status.Length > 0 ? _status : "Loading…", Styles.Body);
                GUI.DragWindow();
                return;
            }

            DrawFilters();
            GUILayout.Space(4);
            if (_tab == 0) DrawOverview();
            else DrawCardList();

            if (_collection.Unknown > 0 || _collection.ModdedGrades > 0)
            {
                GUILayout.Label(
                    (_collection.Unknown > 0 ? $"{_collection.Unknown} unrecognised card(s) not counted. " : "") +
                    (_collection.ModdedGrades > 0 ? $"{_collection.ModdedGrades} graded card(s) have a modded grade (counted as Other)." : ""),
                    Styles.Muted);
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        private void DrawFilters()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Count cards in:", Styles.Muted, GUILayout.Width(100));
            bool changed = false;
            foreach (var l in Plugin.Locations)
            {
                var entry = Plugin.CountIn[(int)l.loc];
                bool v = GUILayout.Toggle(entry.Value, new GUIContent(l.label, l.hint), Styles.Chip);
                if (v != entry.Value)
                {
                    entry.Value = v;
                    changed = true;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Rarity:", Styles.Muted, GUILayout.Width(100));
            string[] names = { "Common", "Rare", "Epic", "Legendary" };
            for (int i = 0; i < 4; i++)
            {
                bool v = GUILayout.Toggle(_tiers[i], names[i], Styles.Chip);
                if (v != _tiers[i])
                {
                    _tiers[i] = v;
                    if (!Array.Exists(_tiers, t => t)) for (int k = 0; k < 4; k++) _tiers[k] = true;
                    changed = true;
                }
            }
            GUILayout.Space(16);
            bool copies = GUILayout.Toggle(Plugin.ShowCopies.Value, "Show copies", Styles.Chip);
            if (copies != Plugin.ShowCopies.Value) Plugin.ShowCopies.Value = copies;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (changed) Recount();
        }

        private void DrawOverview()
        {
            var rows = Rows();
            GUILayout.Label(_summary, Styles.Body);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Set", Styles.Header, GUILayout.Width(NameWidth));
            foreach (var col in GridColumns) GUILayout.Label(col.Label, Styles.HeaderCenter, GUILayout.Width(CellWidth));
            GUILayout.EndHorizontal();

            _gridScroll = GUILayout.BeginScrollView(_gridScroll);
            foreach (var row in rows)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
                GUILayout.Space(row.Depth * 18f);
                string arrow = row.HasChildren ? (_expanded.Contains(row.Id) ? "v " : "> ") : "   ";
                var nameStyle = row.Depth == 0 ? Styles.RowStrong : Styles.Row;
                if (GUILayout.Button(arrow + row.Label, nameStyle, GUILayout.Width(NameWidth - row.Depth * 18f), GUILayout.Height(RowHeight)) && row.HasChildren)
                {
                    if (!_expanded.Remove(row.Id)) _expanded.Add(row.Id);
                    _rows = null;
                }
                for (int i = 0; i < GridColumns.Length; i++)
                {
                    var a = row.Cells[i];
                    string text = $"{a.Owned:N0}/{a.Total:N0}" + (Plugin.ShowCopies.Value ? $"\n{(a.Copies > 0 ? a.Copies.ToString("N0") + "x" : "")}" : "");
                    var tip = $"{row.Set.Name} {(row.Depth > 0 ? row.Group + " " : "")}{(row.Foil == 1 ? "Foil " : row.Foil == 0 ? "Non-foil " : "")}· {GridColumns[i].LongLabel}: {a.Owned:N0} of {a.Total:N0} cards ({Pct(a.Owned, a.Total)}), {a.Copies:N0} copies";
                    if (GUILayout.Button(new GUIContent(text, tip), Styles.Cell(a.Owned, a.Total), GUILayout.Width(CellWidth), GUILayout.Height(RowHeight)))
                    {
                        _selSet = row.Set;
                        _selGroup = row.Group;
                        _selFoil = row.Foil;
                        _selCol = GridColumns[i];
                        _showOwned = false;
                        _list = null;
                        _listScroll = Vector2.zero;
                        _tab = 1;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Label(string.IsNullOrEmpty(GUI.tooltip) ? "Each cell: different cards owned / cards in the set, and total copies. Click a cell to list the missing cards." : GUI.tooltip, Styles.Muted);
        }

        private void DrawCardList()
        {
            if (_selSet == null) _selSet = _catalog!.Sets.Count > 0 ? _catalog.Sets[0] : null;
            if (_selSet == null) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Set:", Styles.Muted, GUILayout.Width(60));
            foreach (var s in _catalog!.Sets)
                if (GUILayout.Toggle(_selSet == s, s.Name, Styles.Chip) && _selSet != s)
                {
                    _selSet = s;
                    _selGroup = null;
                    _list = null;
                }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(_selSet.Lists.Length > 1 ? "Variant:" : "Edition:", Styles.Muted, GUILayout.Width(60));
            if (GUILayout.Toggle(_selGroup == null, "All", Styles.Chip) && _selGroup != null)
            {
                _selGroup = null;
                _list = null;
            }
            foreach (var g in _catalog.GroupsOf(_selSet))
                if (GUILayout.Toggle(_selGroup == g, g, Styles.Chip) && _selGroup != g)
                {
                    _selGroup = g;
                    _list = null;
                }
            GUILayout.Space(12);
            string[] foils = { "All", "Non-foil", "Foil" };
            for (int f = -1; f <= 1; f++)
                if (GUILayout.Toggle(_selFoil == f, foils[f + 1], Styles.Chip) && _selFoil != f)
                {
                    _selFoil = f;
                    _list = null;
                }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Condition:", Styles.Muted, GUILayout.Width(60));
            foreach (var col in GridColumns)
                if (GUILayout.Toggle(_selCol.Equals(col), col.Label, Styles.Chip) && !_selCol.Equals(col))
                {
                    _selCol = col;
                    _list = null;
                }
            GUILayout.Space(12);
            if (GUILayout.Toggle(!_showOwned, "Missing", Styles.Chip) && _showOwned)
            {
                _showOwned = false;
                _list = null;
            }
            if (GUILayout.Toggle(_showOwned, "Owned", Styles.Chip) && !_showOwned)
            {
                _showOwned = true;
                _list = null;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            var list = CardList();
            int priceGrade = _selCol.Value >= 0 && _selCol.Value <= 10 ? _selCol.Value : 0;
            GUILayout.Label($"{list.Count:N0} {(_showOwned ? "owned" : "missing")} · {_selSet.Name}{(_selGroup != null ? " · " + _selGroup : "")}{(_selFoil == 1 ? " · Foil" : _selFoil == 0 ? " · Non-foil" : "")} · {_selCol.LongLabel}", Styles.Body);

            GUILayout.BeginHorizontal();
            GUILayout.Label("#", Styles.Header, GUILayout.Width(44));
            GUILayout.Label("Card", Styles.Header, GUILayout.Width(300));
            GUILayout.Label("Rarity", Styles.Header, GUILayout.Width(90));
            GUILayout.Label("Owned as", Styles.Header, GUILayout.Width(260));
            GUILayout.Label(priceGrade > 0 ? $"Price (G{priceGrade})" : "Price", Styles.Header, GUILayout.Width(110));
            GUILayout.EndHorizontal();

            _listScroll = GUILayout.BeginScrollView(_listScroll);
            // Only lay out the rows in view; the rest are spacers.
            float viewH = Mathf.Max(200f, _rect.height - 260f);
            int first = Mathf.Clamp((int)(_listScroll.y / ListRowHeight) - 2, 0, Math.Max(0, list.Count));
            int last = Mathf.Min(list.Count, first + (int)(viewH / ListRowHeight) + 6);
            if (first > 0) GUILayout.Space(first * ListRowHeight);
            for (int i = first; i < last; i++)
            {
                var c = list[i];
                var p = GameReader.MarketPrice(c, priceGrade);
                GUILayout.BeginHorizontal(GUILayout.Height(ListRowHeight));
                GUILayout.Label(c.Num.ToString("000"), Styles.Muted, GUILayout.Width(44));
                GUILayout.Label(c.Label, Styles.Body, GUILayout.Width(300));
                GUILayout.Label(c.Tier.ToString(), Styles.Muted, GUILayout.Width(90));
                GUILayout.Label(OwnedAs(c), Styles.Body, GUILayout.Width(260));
                GUILayout.Label(p.HasValue ? GameReader.PriceString(p.Value) : "-", Styles.Body, GUILayout.Width(110));
                GUILayout.EndHorizontal();
            }
            if (last < list.Count) GUILayout.Space((list.Count - last) * ListRowHeight);
            GUILayout.EndScrollView();
        }

        // ---------- Styles ----------

        private static class Styles
        {
            private static bool _ready;
            public static GUIStyle Window = null!, Tab = null!, Chip = null!, Header = null!, HeaderCenter = null!;
            public static GUIStyle Row = null!, RowStrong = null!, Body = null!, Muted = null!;
            private static GUIStyle[] _cells = Array.Empty<GUIStyle>();

            // Sequential blue ramp: empty, then five steps of completion; the last is "complete".
            private static readonly Color[] Heat =
            {
                new Color32(0x26, 0x26, 0x24, 0xff),
                new Color32(0x17, 0x22, 0x33, 0xff),
                new Color32(0x1a, 0x2d, 0x47, 0xff),
                new Color32(0x1c, 0x3d, 0x66, 0xff),
                new Color32(0x1c, 0x5c, 0xab, 0xff),
                new Color32(0x2a, 0x78, 0xd6, 0xff),
                new Color32(0x0c, 0xa3, 0x0c, 0xff),
            };

            private static Texture2D Tex(Color c)
            {
                var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                t.SetPixel(0, 0, c);
                t.Apply();
                return t;
            }

            public static void Ensure()
            {
                if (_ready) return;
                _ready = true;
                var skin = GUI.skin;
                Window = new GUIStyle(skin.window) { padding = new RectOffset(10, 10, 24, 10) };
                Window.normal.background = Tex(new Color32(0x1a, 0x1a, 0x19, 0xf2));
                Window.onNormal.background = Window.normal.background;
                Window.normal.textColor = Window.onNormal.textColor = Color.white;
                Tab = new GUIStyle(skin.button) { fontStyle = FontStyle.Bold };
                Chip = new GUIStyle(skin.button) { fontSize = 12, padding = new RectOffset(8, 8, 3, 3), margin = new RectOffset(2, 2, 2, 2) };
                Chip.onNormal.background = Tex(new Color32(0x2a, 0x78, 0xd6, 0xff));
                Chip.onNormal.textColor = Chip.onHover.textColor = Color.white;
                Chip.onHover.background = Chip.onNormal.background;
                Header = new GUIStyle(skin.label) { fontStyle = FontStyle.Bold, fontSize = 12 };
                Header.normal.textColor = new Color32(0xc3, 0xc2, 0xb7, 0xff);
                HeaderCenter = new GUIStyle(Header) { alignment = TextAnchor.MiddleCenter };
                Body = new GUIStyle(skin.label) { fontSize = 13, wordWrap = false };
                Body.normal.textColor = Color.white;
                Muted = new GUIStyle(Body) { fontSize = 12 };
                Muted.normal.textColor = new Color32(0x9a, 0x98, 0x90, 0xff);
                Row = new GUIStyle(skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
                Row.normal.textColor = Color.white;
                RowStrong = new GUIStyle(Row) { fontStyle = FontStyle.Bold, fontSize = 14 };

                _cells = new GUIStyle[Heat.Length];
                for (int i = 0; i < Heat.Length; i++)
                {
                    var s = new GUIStyle(skin.button) { fontSize = 11, alignment = TextAnchor.MiddleCenter, margin = new RectOffset(1, 1, 1, 1), padding = new RectOffset(2, 2, 2, 2) };
                    var bg = Tex(Heat[i]);
                    s.normal.background = s.hover.background = s.active.background = bg;
                    s.normal.textColor = s.hover.textColor = s.active.textColor = i == 0 ? new Color32(0x9a, 0x98, 0x90, 0xff) : (Color)Color.white;
                    _cells[i] = s;
                }
            }

            public static GUIStyle Cell(int owned, int total)
            {
                if (total == 0 || owned == 0) return _cells[0];
                if (owned >= total) return _cells[6];
                double p = owned * 100.0 / total;
                return _cells[p < 20 ? 1 : p < 40 ? 2 : p < 60 ? 3 : p < 80 ? 4 : 5];
            }
        }
    }
}
