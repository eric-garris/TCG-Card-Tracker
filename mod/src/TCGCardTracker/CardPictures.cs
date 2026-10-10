using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TCGCardTracker.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TCGCardTracker
{
    /// <summary>
    /// Pictures of cards drawn by the game's own card renderer (the 3D card the binder uses), so
    /// editions, rarity and foil look as they do in game.
    ///
    /// A few private copies of the game's card sit far below the shop on a spare layer. A disabled
    /// camera that sees only that layer renders them into textures on request. Thumbnails are
    /// rendered once and cached. The large previews re-render every frame while they are shown,
    /// so foil effects that move over time keep moving.
    /// </summary>
    internal sealed class CardPictures
    {
        /// <summary>Card width / height (63 x 88 mm).</summary>
        public const float Aspect = 63f / 88f;

        private const int ThumbTexHeight = 120;
        private const int LargeTexHeight = 840;
        private const int ThumbStageCount = 3;
        private const int LargeStageCount = 2;
        private const int MaxThumbs = 400;
        /// <summary>How long to wait for streamed card art before taking the picture anyway.</summary>
        private const float ArtTimeout = 5f;
        /// <summary>Thumbnail requests not repeated for this long (scrolled away) are dropped.</summary>
        private const float RequestTtl = 1.5f;
        private const float StageSpacing = 100f;
        private static readonly Vector3 StageOrigin = new Vector3(0f, -1000f, 0f);
        /// <summary>The window background, so card corners blend in when drawn opaque.</summary>
        private static readonly Color Background = new Color32(0x1a, 0x1a, 0x19, 0xff);

        private sealed class Stage
        {
            public GameObject Root = null!;
            public Card3dUIGroup Group = null!;
            public Image[] Images = Array.Empty<Image>();
            public int CreatedFrame;
            public long Key = -1;
            public int SetFrame;
            public float SetTime;
            /// <summary>Large stages: frame of the last render of the current card, or -1.</summary>
            public int RenderedFrame = -1;

            public bool Alive => Root != null && Group != null && Group.m_CardUI != null;
        }

        private sealed class Thumb
        {
            public RenderTexture Tex = null!;
            public float LastUsed;
            /// <summary>Taken while the game was still streaming card art: taken again once that settles.</summary>
            public bool Provisional;
        }

        private sealed class Request
        {
            public CardData Data = null!;
            public float Wanted;
        }

        private readonly Dictionary<long, Thumb> _thumbs = new Dictionary<long, Thumb>();
        private readonly Dictionary<long, Request> _requests = new Dictionary<long, Request>();
        private readonly Stage?[] _thumbStages = new Stage?[ThumbStageCount];
        private readonly Stage?[] _largeStages = new Stage?[LargeStageCount];
        private readonly RenderTexture?[] _largeTex = new RenderTexture?[LargeStageCount];
        private RenderTexture? _work;
        private Camera? _camera;
        private int _layer = -1;
        private bool _awake;
        private bool _disabled;
        private int _failures;
        private int _renderFailures;
        private readonly List<Transform> _transforms = new List<Transform>();
        private bool _loggedFrame;
        private LoadStreamTexture? _loader;

        /// <summary>False once the game's card renderer could not be used; the list then shows no pictures.</summary>
        public bool Available => !_disabled;

        /// <summary>Why pictures are off, for the panel.</summary>
        public string Status { get; private set; } = "";

        /// <summary>The first few pictures of a session are checked, saved and logged, so a broken picture can be diagnosed.</summary>
        private const int PictureChecks = 3;
        private int _checks;

        // ---------- Requests from the window (OnGUI) ----------

        /// <summary>The cached thumbnail, or null while it is being made (it is queued).</summary>
        public Texture? Thumbnail(Card card)
        {
            if (_disabled) return null;
            long key = Key(card);
            float now = Time.realtimeSinceStartup;
            if (_thumbs.TryGetValue(key, out var t))
            {
                if (t.Tex != null && t.Tex.IsCreated())
                {
                    t.LastUsed = now;
                    if (t.Provisional && !ArtLoading()) Want(key, card, now);
                    return t.Tex;
                }
                Release(t.Tex);
                _thumbs.Remove(key);
            }
            Want(key, card, now);
            return null;
        }

        /// <summary>The live large picture for slot 0 (hover) or 1 (pinned), once it shows this card.</summary>
        public Texture? Large(int slot, Card card)
        {
            if (_disabled) return null;
            var stage = _largeStages[slot];
            var tex = _largeTex[slot];
            if (stage == null || tex == null || !tex.IsCreated()) return null;
            return stage.Key == Key(card) && stage.RenderedFrame >= 0 ? tex : null;
        }

        private void Want(long key, Card card, float now)
        {
            if (_requests.TryGetValue(key, out var r)) r.Wanted = now;
            else _requests[key] = new Request { Data = ToCardData(card), Wanted = now };
        }

        // ---------- Frame update (Update) ----------

        /// <summary>Makes queued thumbnails and renders the large pictures. Call every frame while the list is shown.</summary>
        public void Tick(Card? hover, Card? pinned)
        {
            if (_disabled) return;
            try
            {
                if (!EnsureSetUp()) return;
                Wake();
                bool canvasesUpdated = false;
                ThumbTick(ref canvasesUpdated);
                LargeTick(0, hover);
                LargeTick(1, pinned);
                _failures = 0;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pictures-tick:" + e.GetType().Name, "Card pictures failed: " + e);
                if (++_failures >= 3) Disable("it kept failing");
            }
        }

        private void ThumbTick(ref bool canvasesUpdated)
        {
            float now = Time.realtimeSinceStartup;
            foreach (var stage in _thumbStages)
            {
                if (stage == null || Time.frameCount <= stage.CreatedFrame) continue;
                if (stage.Key >= 0 && Ready(stage, now))
                {
                    if (!canvasesUpdated)
                    {
                        Canvas.ForceUpdateCanvases();
                        canvasesUpdated = true;
                    }
                    StoreThumb(stage, provisional: ArtLoading());
                    if (_disabled) return;
                    stage.Key = -1;
                }
                if (stage.Key < 0) NextThumb(stage, now);
            }

            if (_thumbs.Count > MaxThumbs) Evict();
        }

        private void NextThumb(Stage stage, float now)
        {
            long best = -1;
            float bestWanted = float.MinValue;
            List<long>? stale = null;
            foreach (var kv in _requests)
            {
                if (now - kv.Value.Wanted > RequestTtl)
                {
                    (stale ??= new List<long>()).Add(kv.Key);
                    continue;
                }
                if (kv.Value.Wanted > bestWanted && !InThumbStage(kv.Key))
                {
                    best = kv.Key;
                    bestWanted = kv.Value.Wanted;
                }
            }
            if (stale != null)
                foreach (var k in stale) _requests.Remove(k);
            if (best < 0) return;
            var data = _requests[best].Data;
            _requests.Remove(best);
            SetCard(stage, best, data);
        }

        private bool InThumbStage(long key)
        {
            foreach (var s in _thumbStages)
                if (s != null && s.Key == key) return true;
            return false;
        }

        private void StoreThumb(Stage stage, bool provisional)
        {
            if (_work == null || !Render(stage, _work)) return;
            if (!_thumbs.TryGetValue(stage.Key, out var t))
            {
                t = new Thumb { Tex = NewTexture(ThumbTexHeight, 0, "TCGCardTracker thumbnail") };
                _thumbs[stage.Key] = t;
            }
            else if (t.Tex == null || !t.Tex.IsCreated())
            {
                Release(t.Tex);
                t.Tex = NewTexture(ThumbTexHeight, 0, "TCGCardTracker thumbnail");
            }
            // Rendered at twice the size and scaled down, so small text stays smooth.
            var active = RenderTexture.active;
            Graphics.Blit(_work, t.Tex);
            RenderTexture.active = active;
            t.LastUsed = Time.realtimeSinceStartup;
            t.Provisional = provisional;
        }

        private void Evict()
        {
            var entries = new List<KeyValuePair<long, Thumb>>(_thumbs);
            entries.Sort((a, b) => a.Value.LastUsed.CompareTo(b.Value.LastUsed));
            for (int i = 0; i < entries.Count - MaxThumbs * 3 / 4; i++)
            {
                Release(entries[i].Value.Tex);
                _thumbs.Remove(entries[i].Key);
            }
        }

        private void LargeTick(int slot, Card? card)
        {
            if (_disabled) return;
            var stage = _largeStages[slot];
            var tex = _largeTex[slot];
            if (stage == null || tex == null || card == null || Time.frameCount <= stage.CreatedFrame) return;
            if (!tex.IsCreated()) tex.Create();
            long key = Key(card);
            if (stage.Key != key)
            {
                SetCard(stage, key, ToCardData(card));
                return;
            }
            // One frame after the card is set its UI has been rebuilt; from then on render live.
            if (Time.frameCount <= stage.SetFrame) return;
            if (Render(stage, tex)) stage.RenderedFrame = Time.frameCount;
        }

        // ---------- Staging and rendering ----------

        private static void SetCard(Stage stage, long key, CardData data)
        {
            stage.Key = key;
            stage.SetFrame = Time.frameCount;
            stage.SetTime = Time.realtimeSinceStartup;
            stage.RenderedFrame = -1;
            var group = stage.Group;
            var ui = group.m_CardUI;

            // The copy's own start-up can sign it up with the game's card manager again, which
            // hides or simplifies cards far from the player (ours are far below the shop).
            Card3dUISpawner.RemoveCardFromManager(group);
            group.SetSimplifyCardDistanceCull(false);
            group.SetVisibility(true);

            ui.SetCardUI(data);
            group.EvaluateCardGrade(data);
            if (ui.m_IsFarDistanceCulled) ui.ResetFarDistanceCull();
            HideExtras(stage);
        }

        /// <summary>Only the card face: no back, card count or "new" badge.</summary>
        private static void HideExtras(Stage stage)
        {
            var group = stage.Group;
            var ui = group.m_CardUI;
            group.SetCardCountTextVisibility(false);
            if (group.m_NewCardIndicator != null && group.m_NewCardIndicator.activeSelf) group.m_NewCardIndicator.SetActive(false);
            if (group.m_CardBackMesh != null && group.m_CardBackMesh.activeSelf) group.m_CardBackMesh.SetActive(false);
            if (ui.m_CardBack != null && ui.m_CardBack.activeSelf) ui.m_CardBack.SetActive(false);
        }

        /// <summary>Undoes anything that switched the card face off since it was set (culling, pooling).</summary>
        private static void EnsureShown(Stage stage)
        {
            var group = stage.Group;
            var ui = group.m_CardUI;
            if (!group.gameObject.activeSelf)
            {
                Plugin.LogOnce("pictures-reactivate", "Card pictures: the card copy had been switched off; switching it back on.");
                group.gameObject.SetActive(true);
            }
            if (!ui.gameObject.activeSelf) ui.gameObject.SetActive(true);
            if (ui.m_CardFront != null && !ui.m_CardFront.activeSelf) ui.m_CardFront.SetActive(true);
            if (ui.m_IsFarDistanceCulled) ui.ResetFarDistanceCull();
            HideExtras(stage);
        }

        /// <summary>The card's UI has been rebuilt (one frame on) and its art has streamed in, or we gave up waiting.</summary>
        private bool Ready(Stage stage, float now)
        {
            if (Time.frameCount <= stage.SetFrame) return false;
            return !ShowsLoadingArt(stage) || now - stage.SetTime > ArtTimeout;
        }

        private bool ShowsLoadingArt(Stage stage)
        {
            var loading = _loader != null ? _loader.m_LoadingSprite : null;
            if (loading == null) return false;
            foreach (var img in stage.Images)
                if (img != null && img.isActiveAndEnabled && img.sprite == loading) return true;
            return false;
        }

        private bool ArtLoading()
        {
            try
            {
                var list = _loader != null ? _loader.m_CurrentLoadingFileNameList : null;
                return list != null && list.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Points the camera square at the card face and renders it into the texture.</summary>
        private bool Render(Stage stage, RenderTexture target)
        {
            if (_camera == null || !stage.Alive) return false;
            var face = FaceRect(stage.Group.m_CardUI);
            if (face == null) return RenderFailed();

            var c = new Vector3[4];
            face.GetWorldCorners(c);
            Vector3 right = c[3] - c[0], up = c[1] - c[0];
            float w = right.magnitude, h = up.magnitude;
            if (w <= 0f || h <= 0f) return RenderFailed();
            _renderFailures = 0;
            EnsureShown(stage);
            SetLayer(stage.Group.gameObject);
            // UI faces the camera that looks along the canvas's forward; working it out from the
            // corners keeps the picture upright and unmirrored however the prefab is rotated.
            var forward = Vector3.Cross(right, up).normalized;
            var center = (c[0] + c[2]) * 0.5f;
            float dist = Mathf.Max(w, h) * 2f;

            if (!_loggedFrame)
            {
                _loggedFrame = true;
                Plugin.Log.LogInfo($"Card pictures: framing '{face.name}' ({w:0.###} x {h:0.###}) at {center}, facing {forward}.");
            }

            _camera.transform.SetPositionAndRotation(center - forward * dist, Quaternion.LookRotation(forward, up));
            _camera.nearClipPlane = dist * 0.5f;
            _camera.farClipPlane = dist * 1.5f;
            _camera.targetTexture = target;
            _camera.aspect = Aspect;
            _camera.orthographicSize = Mathf.Max(h, w / Aspect) * 0.5f * 1.02f;
            var active = RenderTexture.active;
            try
            {
                _camera.Render();
            }
            finally
            {
                _camera.targetTexture = null;
                RenderTexture.active = active;
            }
            if (_checks < PictureChecks) CheckPicture(stage, target, face);
            return true;
        }

        private bool RenderFailed()
        {
            if (++_renderFailures >= 20) Disable("the card face could not be found to frame it");
            return false;
        }

        /// <summary>The rectangle of the card face: the first card-shaped one of the card's UI rects.</summary>
        private static RectTransform? FaceRect(CardUI ui)
        {
            var candidates = new[]
            {
                ui.m_CardFront != null ? ui.m_CardFront.transform as RectTransform : null,
                ui.transform as RectTransform,
                ui.m_CardBGImage != null ? ui.m_CardBGImage.rectTransform : null,
                ui.m_CardBorderImage != null ? ui.m_CardBorderImage.rectTransform : null,
            };
            foreach (var rt in candidates)
            {
                if (rt == null) continue;
                var size = Vector2.Scale(rt.rect.size, rt.lossyScale);
                if (size.x <= 0f || size.y <= 0f) continue;
                float aspect = size.x / size.y;
                if (aspect > 0.55f && aspect < 0.9f) return rt;
            }
            Plugin.LogOnce("pictures-frame", "Card pictures: could not find the card face to frame.");
            return null;
        }

        // ---------- Setup and teardown ----------

        private bool EnsureSetUp()
        {
            if (_camera != null && AllAlive()) return true;
            Teardown(keepThumbs: true);

            var prefab = FindCardPrefab(out string source);
            if (prefab == null)
            {
                Disable("the game's 3D card was not found");
                return false;
            }
            _loader = LoadStreamTexture.m_Instance != null ? LoadStreamTexture.m_Instance : UnityEngine.Object.FindObjectOfType<LoadStreamTexture>();
            _layer = SpareLayer();
            Plugin.Log.LogInfo($"Card pictures: using '{prefab.name}' from {source}, layer {_layer}, art loader {(_loader != null ? "found" : "not found")}.");

            var camGo = new GameObject("TCGCardTracker.PictureCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Background;
            _camera.cullingMask = 1 << _layer;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.useOcclusionCulling = false;
            _camera.depthTextureMode = DepthTextureMode.None;

            int n = 0;
            for (int i = 0; i < ThumbStageCount; i++) _thumbStages[i] = NewStage(prefab, n++);
            for (int i = 0; i < LargeStageCount; i++)
            {
                _largeStages[i] = NewStage(prefab, n++);
                _largeTex[i] = NewTexture(LargeTexHeight, 16, "TCGCardTracker picture");
            }
            _work = NewTexture(ThumbTexHeight * 2, 16, "TCGCardTracker work");
            _awake = true;
            return true;
        }

        /// <summary>The game's 3D card: the card spawner's prefab, else any card the game has loaded.</summary>
        private static Card3dUIGroup? FindCardPrefab(out string source)
        {
            var spawner = Card3dUISpawner.m_Instance != null ? Card3dUISpawner.m_Instance : UnityEngine.Object.FindObjectOfType<Card3dUISpawner>();
            if (spawner != null && spawner.m_Card3dUIPrefab != null)
            {
                source = "the card spawner";
                return spawner.m_Card3dUIPrefab;
            }
            Card3dUIGroup? fallback = null;
            foreach (var g in Resources.FindObjectsOfTypeAll<Card3dUIGroup>())
            {
                if (g == null || g.m_CardUI == null || g.name.StartsWith("TCGCardTracker", StringComparison.Ordinal)) continue;
                if (g.transform.root.name.StartsWith("TCGCardTracker", StringComparison.Ordinal)) continue;
                // Prefer an asset (not placed in a scene), whose state the game hasn't changed.
                if (!g.gameObject.scene.IsValid())
                {
                    source = "loaded assets";
                    return g;
                }
                fallback ??= g;
            }
            source = "a card in the shop";
            return fallback;
        }

        // ---------- Diagnostics ----------

        /// <summary>Saves the picture next to the mod and logs how much of it shows a card, plus the card's make-up.</summary>
        private void CheckPicture(Stage stage, RenderTexture rt, RectTransform face)
        {
            int n = ++_checks;
            Texture2D? tex = null;
            var active = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = active;

                Color32 bg = Background;
                var px = tex.GetPixels32();
                int drawn = 0;
                foreach (var p in px)
                    if (Math.Abs(p.r - bg.r) + Math.Abs(p.g - bg.g) + Math.Abs(p.b - bg.b) > 24) drawn++;
                float pct = px.Length == 0 ? 0f : drawn * 100f / px.Length;

                string path = "";
                try
                {
                    string dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
                    if (dir.Length == 0) dir = Application.persistentDataPath;
                    path = Path.Combine(dir, $"card-picture-check-{n}.png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                }
                catch (Exception e)
                {
                    path = "(could not save: " + e.Message + ")";
                }

                string msg = $"Card pictures: check {n}: {pct:0.#}% of the picture shows the card. Saved to {path}.";
                if (pct < 2f) Plugin.Log.LogWarning(msg + " The picture is empty.");
                else Plugin.Log.LogInfo(msg);
                if (n == 1 || pct < 2f) Plugin.LogOnce("pictures-describe:" + (pct < 2f), Describe(stage, face));
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pictures-check", "Card pictures: could not check a picture: " + e.Message);
            }
            finally
            {
                RenderTexture.active = active;
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        private string Describe(Stage stage, RectTransform face)
        {
            var sb = new StringBuilder();
            var cam = _camera;
            sb.Append("Card pictures: diagnostics. Color space ").Append(QualitySettings.activeColorSpace)
              .Append(", picture layer ").Append(_layer).Append(", face '").Append(face.name).Append("'.\n");
            if (cam != null)
                sb.Append("  camera at ").Append(cam.transform.position).Append(" looking ").Append(cam.transform.forward)
                  .Append(", ortho size ").Append(cam.orthographicSize.ToString("0.####"))
                  .Append(", clip ").Append(cam.nearClipPlane.ToString("0.####")).Append("-").Append(cam.farClipPlane.ToString("0.####")).Append("\n");
            int lines = 0;
            Walk(stage.Root.transform, 1, sb, ref lines);
            return sb.ToString();
        }

        private static void Walk(Transform t, int depth, StringBuilder sb, ref int lines)
        {
            if (++lines > 400)
            {
                if (lines == 401) sb.Append("  ...\n");
                return;
            }
            sb.Append(' ', depth * 2).Append(t.name);
            if (!t.gameObject.activeSelf) sb.Append(" [off]");
            sb.Append(" L").Append(t.gameObject.layer);
            if (t is RectTransform rt)
            {
                var size = Vector2.Scale(rt.rect.size, rt.lossyScale);
                sb.Append(" size ").Append(size.x.ToString("0.####")).Append('x').Append(size.y.ToString("0.####"));
            }
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                sb.Append(" | ").Append(c.GetType().Name);
                switch (c)
                {
                    case Canvas cv:
                        sb.Append('(').Append(cv.renderMode).Append(cv.enabled ? "" : ", off").Append(", order ").Append(cv.sortingOrder)
                          .Append(", camera ").Append(cv.worldCamera != null ? cv.worldCamera.name : "none").Append(')');
                        break;
                    case Image img:
                        sb.Append('(').Append(img.enabled ? "" : "off, ").Append(img.sprite != null ? img.sprite.name : "no sprite")
                          .Append(", a ").Append(img.color.a.ToString("0.##")).Append(", ").Append(img.material != null && img.material.shader != null ? img.material.shader.name : "no shader").Append(')');
                        break;
                    case Graphic g:
                        sb.Append('(').Append(g.enabled ? "" : "off, ").Append("a ").Append(g.color.a.ToString("0.##")).Append(')');
                        break;
                    case Renderer r:
                        sb.Append('(').Append(r.enabled ? "" : "off, ").Append(r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "no material").Append(')');
                        break;
                    case Behaviour b when !b.enabled:
                        sb.Append("(off)");
                        break;
                }
            }
            sb.Append('\n');
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb, ref lines);
        }

        private bool AllAlive()
        {
            foreach (var s in _thumbStages)
                if (s == null || !s.Alive) return false;
            foreach (var s in _largeStages)
                if (s == null || !s.Alive) return false;
            return true;
        }

        private Stage NewStage(Card3dUIGroup prefab, int index)
        {
            // Built under an inactive root so the copy's Awake runs only after it is marked to
            // skip the game's distance culling.
            var root = new GameObject("TCGCardTracker.CardPicture" + index);
            root.SetActive(false);
            root.transform.position = StageOrigin + new Vector3(index * StageSpacing, 0f, 0f);
            var group = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            group.m_IgnoreCulling = true;
            group.gameObject.SetActive(true);
            SetLayer(group.gameObject);
            root.SetActive(true);

            // Keep the game's card manager (culling, brightness) away from our copies.
            Card3dUISpawner.RemoveCardFromManager(group);

            return new Stage
            {
                Root = root,
                Group = group,
                Images = group.GetComponentsInChildren<Image>(true),
                CreatedFrame = Time.frameCount,
            };
        }

        /// <summary>Moves the card and everything under it (text adds pieces as it changes) to the picture layer.</summary>
        private void SetLayer(GameObject go)
        {
            go.GetComponentsInChildren(true, _transforms);
            foreach (var t in _transforms)
                if (t.gameObject.layer != _layer) t.gameObject.layer = _layer;
            _transforms.Clear();
        }

        /// <summary>The highest unnamed layer, so the picture camera sees nothing of the shop.</summary>
        private static int SpareLayer()
        {
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return 31;
        }

        private static RenderTexture NewTexture(int height, int depth, string name)
        {
            var tex = new RenderTexture(Mathf.RoundToInt(height * Aspect), height, depth, RenderTextureFormat.ARGB32)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.Create();
            return tex;
        }

        private static void Release(RenderTexture? tex)
        {
            if (tex == null) return;
            tex.Release();
            UnityEngine.Object.Destroy(tex);
        }

        /// <summary>Panel closed: park the card copies so they cost nothing. Thumbnails stay cached.</summary>
        public void Sleep()
        {
            if (!_awake) return;
            _awake = false;
            _requests.Clear();
            try
            {
                foreach (var s in AllStages())
                {
                    if (s == null) continue;
                    s.Key = -1;
                    s.RenderedFrame = -1;
                    if (s.Root != null) s.Root.SetActive(false);
                }
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pictures-sleep", "Could not park card pictures: " + e.Message);
            }
        }

        private void Wake()
        {
            if (_awake) return;
            _awake = true;
            foreach (var s in AllStages())
                if (s != null && s.Root != null && !s.Root.activeSelf)
                {
                    s.Root.SetActive(true);
                    s.CreatedFrame = Time.frameCount;
                }
        }

        /// <summary>Scene change: drop everything (the card copies go with the old scene).</summary>
        public void Reset()
        {
            Teardown(keepThumbs: false);
            _disabled = false;
            _failures = 0;
            _renderFailures = 0;
            Status = "";
        }

        private void Teardown(bool keepThumbs)
        {
            foreach (var s in AllStages())
                if (s != null && s.Root != null) UnityEngine.Object.Destroy(s.Root);
            Array.Clear(_thumbStages, 0, _thumbStages.Length);
            Array.Clear(_largeStages, 0, _largeStages.Length);
            for (int i = 0; i < _largeTex.Length; i++)
            {
                Release(_largeTex[i]);
                _largeTex[i] = null;
            }
            Release(_work);
            _work = null;
            if (_camera != null) UnityEngine.Object.Destroy(_camera.gameObject);
            _camera = null;
            _loader = null;
            _awake = false;
            _requests.Clear();
            if (keepThumbs) return;
            foreach (var t in _thumbs.Values) Release(t.Tex);
            _thumbs.Clear();
        }

        private IEnumerable<Stage?> AllStages()
        {
            foreach (var s in _thumbStages) yield return s;
            foreach (var s in _largeStages) yield return s;
        }

        private void Disable(string why)
        {
            if (_disabled) return;
            _disabled = true;
            Status = $"Card pictures are off: {why}. Details are in BepInEx\\LogOutput.log.";
            Plugin.Log.LogWarning($"Card pictures are off: {why}. The card list still works without them.");
            try
            {
                Teardown(keepThumbs: false);
            }
            catch
            {
                // Best effort; the scene change cleans up the rest.
            }
        }

        // ---------- Card identity ----------

        private static CardData ToCardData(Card card) => new CardData
        {
            expansionType = (ECardExpansionType)card.Set.ExpansionType,
            monsterType = (EMonsterType)card.MonsterId,
            // Ghost cards have a single slot, which the game names after the Full Art border.
            borderType = (ECardBorderType)(card.Set.PerNoFoil == 1 ? (int)ECardBorderType.FullArt : card.Edition),
            isFoil = card.Foil,
            isDestiny = card.Set.Lists[card.List].IsDestiny,
            cardGrade = 0,
        };

        private static long Key(Card card) =>
            ((long)card.Set.ExpansionType << 48) | ((long)card.List << 44) | (uint)card.SaveIndex;
    }
}
