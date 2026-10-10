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
    /// opens the card list for it (missing or owned), with a picture of each card: hover one to
    /// enlarge it, click to pin it in its own window.
    /// </summary>
    internal sealed class TrackerWindow
    {
        private const int WindowId = 0x54434754; // "TCGT"
        private const int PinnedWindowId = WindowId + 1;
        private const int HoverWindowId = WindowId + 2;
        private const float NameWidth = 210f;
        private const float CellWidth = 64f;
        private const float RowHeight = 36f;
        private const float TextRowHeight = 24f;
        private const float ThumbHeight = 60f;
        private const float ThumbWidth = ThumbHeight * CardPictures.Aspect;
        private const float LargeHeight = 420f;
        private const float LargeWidth = LargeHeight * CardPictures.Aspect;

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

        // Card pictures. Hover is found while painting the list and used from the next frame,
        // so every IMGUI pass of a frame lays out the same windows.
        private readonly CardPictures _pictures = new CardPictures();
        private bool _showPictures;
        private Card? _hover;
        private Card? _hoverNext;
        private Card? _pinned;
        private Rect _pinnedRect;
        private bool _pinnedPlaced;
        private float _listViewHeight;

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
            /// <summary>Cell text and tooltip, built once per data change instead of on every OnGUI event.</summary>
            public GUIContent[] Contents = Array.Empty<GUIContent>();
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
            // Keep a hand-back still pending from closing while paused; otherwise take UI mode now.
            if (_uiController == null) _uiController = GameReader.EnterUIMode();
            Refresh(full: true);
        }

        private void Close()
        {
            _open = false;
            _hover = _hoverNext = _pinned = null;
            // If the game was paused while the panel was open, hand the cursor back only once it
            // resumes (the pause menu still needs it, and the game's UI-mode reset waits on game time).
            if (Time.timeScale == 0f) return;
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
            _hover = _hoverNext = _pinned = null;
            _showPictures = false;
            _pictures.Reset();
        }

        /// <summary>Called every frame: keeps binder counts current and card pictures drawn while the panel is open.</summary>
        public void Tick()
        {
            if (!_open)
            {
                if (_uiController != null && Time.timeScale > 0f)
                {
                    GameReader.ExitUIMode(_uiController);
                    _uiController = null;
                }
                _showPictures = false;
                _pictures.Sleep();
                return;
            }
            if (Time.realtimeSinceStartup >= _nextAutoRefresh) Refresh(full: false);

            _showPictures = Plugin.ShowPictures.Value && _catalog != null && _pictures.Available;
            if (_showPictures)
            {
                _pictures.Tick(_tab == 1 ? _hover : null, _pinned);
            }
            else
            {
                _hover = _hoverNext = _pinned = null;
                _pictures.Sleep();
            }
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
                // Also build it when the panel was opened during a loading screen.
                if (full || _catalog == null)
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
            foreach (var row in rows)
            {
                row.Contents = new GUIContent[GridColumns.Length];
                for (int i = 0; i < GridColumns.Length; i++)
                {
                    var a = row.Cells[i];
                    string text = $"{a.Owned:N0}/{a.Total:N0}" + (Plugin.ShowCopies.Value ? $"\n{(a.Copies > 0 ? a.Copies.ToString("N0") + "x" : "")}" : "");
                    string where = row.Depth > 0 ? row.Group + " " : "";
                    string foil = row.Foil == 1 ? "Foil " : row.Foil == 0 ? "Non-foil " : "";
                    string tip = $"{row.Set.Name} {where}{foil}· {GridColumns[i].LongLabel}: {a.Owned:N0} of {a.Total:N0} cards ({Pct(a.Owned, a.Total)}), {a.Copies:N0} copies";
                    row.Contents[i] = new GUIContent(text, tip);
                }
            }
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
                if (Event.current.type == EventType.Layout) _hover = _showPictures && _tab == 1 ? _hoverNext : null;
                _rect = GUILayout.Window(WindowId, _rect, DrawWindow, $"{Plugin.Name}  ·  {Plugin.ToggleKey.Value} to close", Styles.Window);
                _rect.x = Mathf.Clamp(_rect.x, -_rect.width + 80f, sw - 80f);
                _rect.y = Mathf.Clamp(_rect.y, 0f, sh - 40f);
                DrawPictureWindows(sw, sh);
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
            if (Event.current.type == EventType.Repaint) _hoverNext = null;
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
            if (copies != Plugin.ShowCopies.Value)
            {
                Plugin.ShowCopies.Value = copies;
                _rows = null;
            }
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
                    if (GUILayout.Button(row.Contents[i], Styles.Cell(a.Owned, a.Total), GUILayout.Width(CellWidth), GUILayout.Height(RowHeight)))
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

            bool pictures = _showPictures;
            float rowHeight = pictures ? ThumbHeight + 4f : TextRowHeight;

            GUILayout.BeginHorizontal();
            if (pictures) GUILayout.Space(ThumbWidth + 8f);
            GUILayout.Label("#", Styles.Header, GUILayout.Width(44));
            GUILayout.Label("Card", Styles.Header, GUILayout.Width(300));
            GUILayout.Label("Rarity", Styles.Header, GUILayout.Width(90));
            GUILayout.Label("Owned as", Styles.Header, GUILayout.Width(260));
            GUILayout.Label(priceGrade > 0 ? $"Price (G{priceGrade})" : "Price", Styles.Header, GUILayout.Width(110));
            GUILayout.EndHorizontal();

            _listScroll = GUILayout.BeginScrollView(_listScroll);
            // Only lay out the rows in view; the rest are spacers.
            float viewH = Mathf.Max(200f, _rect.height - 260f);
            int first = Mathf.Clamp((int)(_listScroll.y / rowHeight) - 2, 0, Math.Max(0, list.Count));
            int last = Mathf.Min(list.Count, first + (int)(viewH / rowHeight) + 6);
            if (first > 0) GUILayout.Space(first * rowHeight);
            var height = GUILayout.Height(rowHeight);
            for (int i = first; i < last; i++)
            {
                var c = list[i];
                var p = GameReader.MarketPrice(c, priceGrade);
                GUILayout.BeginHorizontal(height);
                if (pictures) DrawThumbnail(c);
                GUILayout.Label(c.Num.ToString("000"), Styles.RowMuted, GUILayout.Width(44), height);
                GUILayout.Label(c.Label, Styles.RowBody, GUILayout.Width(300), height);
                GUILayout.Label(c.Tier.ToString(), Styles.RowMuted, GUILayout.Width(90), height);
                GUILayout.Label(OwnedAs(c), Styles.RowBody, GUILayout.Width(260), height);
                GUILayout.Label(p.HasValue ? GameReader.PriceString(p.Value) : "-", Styles.RowBody, GUILayout.Width(110), height);
                GUILayout.EndHorizontal();
            }
            if (last < list.Count) GUILayout.Space((list.Count - last) * rowHeight);
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _listViewHeight = GUILayoutUtility.GetLastRect().height;
            if (pictures) GUILayout.Label("Hover a picture to enlarge it, click it to pin it.", Styles.Muted);
        }

        // ---------- Card pictures ----------

        private static bool Same(Card? a, Card? b) =>
            a != null && b != null && a.Set.ExpansionType == b.Set.ExpansionType && a.List == b.List && a.SaveIndex == b.SaveIndex;

        /// <summary>One row's thumbnail (inside the list's scroll view): hover to enlarge, click to pin.</summary>
        private void DrawThumbnail(Card c)
        {
            var r = GUILayoutUtility.GetRect(ThumbWidth, ThumbHeight, GUILayout.Width(ThumbWidth), GUILayout.Height(ThumbHeight));
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                var tex = _pictures.Thumbnail(c);
                if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, false);
                else GUI.Box(r, GUIContent.none, Styles.Placeholder);
                if (Same(c, _pinned)) GUI.Box(r, GUIContent.none, Styles.Outline);
                // Rows just outside the view are laid out too; only count the mouse over the visible part.
                var m = e.mousePosition;
                if (r.Contains(m) && m.y >= _listScroll.y && m.y <= _listScroll.y + _listViewHeight) _hoverNext = c;
            }
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                _pinned = Same(c, _pinned) ? null : c;
                _pinnedPlaced &= _pinned != null;
            }
        }

        private Texture? LargePicture(int slot, Card c) => _pictures.Large(slot, c) ?? _pictures.Thumbnail(c);

        /// <summary>The pinned picture window and the hover popup, drawn above the main window.</summary>
        private void DrawPictureWindows(float sw, float sh)
        {
            if (!_showPictures) return;
            float w = LargeWidth + 20f, h = LargeHeight + 44f;

            if (_pinned != null)
            {
                if (!_pinnedPlaced)
                {
                    // Beside the main window where there is room, else over its right edge.
                    float x = _rect.xMax + 8f;
                    if (x + w > sw) x = _rect.x - w - 8f;
                    if (x < 0f) x = Mathf.Max(0f, Mathf.Min(sw, _rect.xMax) - w);
                    _pinnedRect = new Rect(x, Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, sh - h)), w, h);
                    _pinnedPlaced = true;
                }
                _pinnedRect = GUI.Window(PinnedWindowId, _pinnedRect, DrawPinned, GUIContent.none, Styles.Window);
                _pinnedRect.x = Mathf.Clamp(_pinnedRect.x, -w + 80f, sw - 80f);
                _pinnedRect.y = Mathf.Clamp(_pinnedRect.y, 0f, sh - 40f);
            }

            if (_hover != null && !Same(_hover, _pinned))
            {
                float pw = LargeWidth + 12f, ph = LargeHeight + 12f;
                var m = Event.current.mousePosition;
                float x = m.x + 24f;
                if (x + pw > sw) x = m.x - 24f - pw;
                float y = Mathf.Clamp(m.y - ph / 2f, 0f, Mathf.Max(0f, sh - ph));
                GUI.Window(HoverWindowId, new Rect(x, y, pw, ph), DrawHover, GUIContent.none, Styles.Popup);
                GUI.BringWindowToFront(HoverWindowId);
            }
        }

        private void DrawPinned(int id)
        {
            var c = _pinned;
            if (c == null) return;
            float w = _pinnedRect.width;
            GUI.Label(new Rect(10f, 4f, w - 50f, 22f), c.Label, Styles.Body);
            if (GUI.Button(new Rect(w - 34f, 4f, 24f, 20f), "x"))
            {
                _pinned = null;
                _pinnedPlaced = false;
                return;
            }
            var r = new Rect(10f, 32f, LargeWidth, LargeHeight);
            if (Event.current.type == EventType.Repaint)
            {
                var tex = LargePicture(1, c);
                if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, false);
                else GUI.Box(r, GUIContent.none, Styles.Placeholder);
            }
            GUI.DragWindow();
        }

        private void DrawHover(int id)
        {
            var c = _hover;
            if (c == null || Event.current.type != EventType.Repaint) return;
            var r = new Rect(6f, 6f, LargeWidth, LargeHeight);
            var tex = LargePicture(0, c);
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, false);
            else GUI.Box(r, GUIContent.none, Styles.Placeholder);
        }

        // ---------- Styles ----------

        private static class Styles
        {
            private static bool _ready;
            public static GUIStyle Window = null!, Tab = null!, Chip = null!, Header = null!, HeaderCenter = null!;
            public static GUIStyle Row = null!, RowStrong = null!, Body = null!, Muted = null!;
            public static GUIStyle RowBody = null!, RowMuted = null!, Popup = null!, Placeholder = null!, Outline = null!;
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

            /// <summary>A 6x6 frame (2 px border, clear middle), stretched as a 9-slice.</summary>
            private static Texture2D OutlineTex(Color c)
            {
                var t = new Texture2D(6, 6) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                for (int y = 0; y < 6; y++)
                    for (int x = 0; x < 6; x++)
                        t.SetPixel(x, y, x < 2 || x > 3 || y < 2 || y > 3 ? c : Color.clear);
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
                RowBody = new GUIStyle(Body) { alignment = TextAnchor.MiddleLeft };
                RowMuted = new GUIStyle(Muted) { alignment = TextAnchor.MiddleLeft };
                Popup = new GUIStyle(GUIStyle.none) { padding = new RectOffset(6, 6, 6, 6) };
                Popup.normal.background = Tex(new Color32(0x1a, 0x1a, 0x19, 0xff));
                Placeholder = new GUIStyle(GUIStyle.none);
                Placeholder.normal.background = Tex(new Color32(0x26, 0x26, 0x24, 0xff));
                Outline = new GUIStyle(GUIStyle.none) { border = new RectOffset(2, 2, 2, 2) };
                Outline.normal.background = OutlineTex(new Color32(0x2a, 0x78, 0xd6, 0xff));

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
