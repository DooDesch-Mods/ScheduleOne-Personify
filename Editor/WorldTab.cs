using System;
using System.Collections.Generic;
using DooDesch.UI;
using Newtonsoft.Json.Linq;
using Personify.Editor.Widgets;
using Personify.Editor.World;
using S1API.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Personify.Editor
{
    /// <summary>
    /// The World tab: where the NPC lives and what it does all day, laid out on the phone map. The map fills the space
    /// between the rail and the form; clicking it places the start point and the stops, the form lists the day plan
    /// and edits the selected stop. Everything is written straight into the NPC's raw <c>spawn</c> / <c>contact</c> /
    /// <c>schedule</c> blocks, so fields this tab has no control for survive an edit.
    /// </summary>
    public static partial class EditorUI
    {
        private const int SelNone = -1;
        private const int SelSpawn = -2;

        private const float MapMaxZoom = 10f;
        private const float BuildingPick = 40f;
        private const float SeatPick = 25f;
        private const float ParkingPick = 40f;

        private static GameObject _bottomBar;
        private static GameObject _mapRoot;
        private static RectTransform _mapViewport;
        private static RectTransform _mapContent;
        private static RectTransform _pinLayer;
        private static Text _mapHover;

        private static float _mapZoom = 1f;
        private static Vector2 _mapPan;
        private static NpcDraft _worldNpc;
        private static int _worldSel = SelNone;

        // How far from a click the place menu looks for something to offer.
        private const float MenuBuildingReach = 30f;
        private const float MenuSeatReach = 15f;
        private const float MenuParkingReach = 30f;
        private const float PinHitRadius = 15f;    // canvas units around a pin centre that grab it
        private const int NoPin = int.MinValue;
        private const int HistoryLimit = 40;

        private static bool _mapPressed, _mapDragging;
        private static Vector2 _mapPressAt, _mapPanAtPress, _lastHoverMouse = new Vector2(-1f, -1f);
        private static Sprite _dot, _ring;

        private static RectTransform _timeline;
        private static Button _undoBtn;
        private static Image _hoverRing, _dragGhost;
        private static Text _hoverLabel;
        private static GameObject _menu;
        private static readonly List<(string Label, Color Color, Action Do)> _menuEntries = new List<(string, Color, Action)>();
        private static readonly List<(int Index, Vector2 Uv)> _pinHits = new List<(int, Vector2)>();
        private static int _dragPin = NoPin;

        // Undo: snapshots of the NPC's spawn / contact / schedule blocks, newest last.
        private static readonly List<string> _history = new List<string>();
        private static NpcDraft _historyNpc;

        // ---- view lifecycle -------------------------------------------------------------------------------------

        /// <summary>Shows or removes the map to match the current tab. Called at the start of every form rebuild.</summary>
        private static void SyncWorldView()
        {
            bool want = _mode == FormMode.World && _selected != null && _screen != null;
            if (_bottomBar != null) _bottomBar.SetActive(!want);
            if (!ReferenceEquals(_worldNpc, _selected)) { _worldNpc = _selected; _worldSel = SelNone; }
            if (!want) { DestroyMapView(); return; }
            TrackHistory();
            CloseMenu();
            if (_mapRoot == null) BuildMapView();
            if (_undoBtn != null) _undoBtn.interactable = _history.Count > 1;
            RefreshPins();
        }

        private static void DestroyMapView()
        {
            if (_mapRoot != null) UnityEngine.Object.Destroy(_mapRoot);
            ForgetMapView();
        }

        // The map hangs under the editor screen, so it dies with it; only the references need dropping.
        private static void ForgetMapView()
        {
            _mapRoot = null; _mapViewport = null; _mapContent = null; _pinLayer = null; _mapHover = null;
            _timeline = null; _undoBtn = null; _hoverRing = null; _hoverLabel = null; _dragGhost = null; _menu = null;
            _menuEntries.Clear(); _pinHits.Clear();
            _mapPressed = false; _mapDragging = false; _dragPin = NoPin;
        }

        private static void BuildMapView()
        {
            _mapRoot = UIFactory.Panel("WorldMap", _screen.transform, Theme.BgPanel);
            var bg = _mapRoot.GetComponent<Image>(); if (bg != null) { bg.sprite = Theme.RoundedSprite(); bg.type = Image.Type.Sliced; }
            var rt = _mapRoot.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(RailW + 12f, 12f); rt.offsetMax = new Vector2(-PanelW - 12f, -12f);

            _mapHover = UIFactory.Text("hover", "", _mapRoot.transform, Theme.Body, TextAnchor.MiddleLeft);
            _mapHover.color = Theme.TextPrimary; _mapHover.raycastTarget = false; _mapHover.horizontalOverflow = HorizontalWrapMode.Wrap;
            var hrt = _mapHover.rectTransform; hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
            hrt.offsetMin = new Vector2(14f, -42f); hrt.offsetMax = new Vector2(-236f, -6f);

            MapButton("Fit", -10f, 52f, () => { _mapZoom = 1f; _mapPan = Vector2.zero; ApplyMapTransform(true); });
            MapButton("+", -70f, 34f, () => ZoomAround(-_mapPan, _mapZoom * 1.5f));
            MapButton("-", -112f, 34f, () => ZoomAround(-_mapPan, _mapZoom / 1.5f));
            _undoBtn = MapButton("Undo", -162f, 64f, Undo);
            _undoBtn.interactable = _history.Count > 1;

            var vp = UIFactory.Panel("viewport", _mapRoot.transform, Theme.SurfaceInput);
            _mapViewport = vp.GetComponent<RectTransform>();
            _mapViewport.anchorMin = Vector2.zero; _mapViewport.anchorMax = Vector2.one;
            _mapViewport.offsetMin = new Vector2(8f, 54f); _mapViewport.offsetMax = new Vector2(-8f, -48f);
            vp.AddComponent<RectMask2D>();

            var tl = UIFactory.Panel("day", _mapRoot.transform, Theme.SurfaceInput);
            _timeline = tl.GetComponent<RectTransform>();
            _timeline.anchorMin = new Vector2(0, 0); _timeline.anchorMax = new Vector2(1, 0); _timeline.pivot = new Vector2(0.5f, 0);
            _timeline.offsetMin = new Vector2(8f, 8f); _timeline.offsetMax = new Vector2(-8f, 46f);

            WorldData w = WorldData.Current;
            if (w == null || w.Map == null)
            {
                var empty = UIFactory.Text("empty", w == null
                        ? "No map yet. Load any save once with Personify installed, then come back."
                        : "The map picture is missing. Load any save once to capture it again.",
                    vp.transform, Theme.Label, TextAnchor.MiddleCenter);
                empty.color = Theme.TextMuted; empty.raycastTarget = false;
                StretchFill(empty.rectTransform);
                Core.Log?.Msg("[world] map view without a capture - showing the empty state.");
                Interactions.PolishButtons(_mapRoot.transform);
                return;
            }

            var mapGO = new GameObject("map");
            mapGO.transform.SetParent(vp.transform, false);
            _mapContent = mapGO.AddComponent<RectTransform>();
            _mapContent.anchorMin = _mapContent.anchorMax = new Vector2(0.5f, 0.5f);
            _mapContent.pivot = new Vector2(0.5f, 0.5f);
            var raw = mapGO.AddComponent<RawImage>();
            raw.texture = w.Map;

            var pins = new GameObject("pins");
            pins.transform.SetParent(mapGO.transform, false);
            _pinLayer = pins.AddComponent<RectTransform>();
            StretchFill(_pinLayer);

            ApplyMapTransform(false);
            Interactions.PolishButtons(_mapRoot.transform);
        }

        // A toolbar button in the map's top-right corner; x is the right edge's offset from the panel edge.
        private static Button MapButton(string label, float x, float width, Action onClick)
        {
            var (go, btn, txt) = UIFactory.ButtonWithLabel("mb_" + label, label, _mapRoot.transform, Theme.Button, 40, 30);
            if (txt != null) txt.fontSize = Theme.Label;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(width, 30f);
            rt.anchoredPosition = new Vector2(x, -9f);
            btn.onClick.AddListener((UnityAction)(() => onClick()));
            return btn;
        }

        private static float MapBaseSize()
        {
            if (_mapViewport == null) return 600f;
            Rect vp = _mapViewport.rect;
            float s = Mathf.Min(vp.width, vp.height);
            return s > 10f ? s : 600f;
        }

        private static void ApplyMapTransform(bool rebuildPins)
        {
            if (_mapContent == null) return;
            float size = MapBaseSize() * _mapZoom;
            Rect vp = _mapViewport.rect;
            // Pan stops a quarter viewport past the map edge, so the picture can never be dragged out of sight.
            float maxX = Mathf.Max(0f, (size - vp.width) / 2f) + vp.width * 0.25f;
            float maxY = Mathf.Max(0f, (size - vp.height) / 2f) + vp.height * 0.25f;
            _mapPan = new Vector2(Mathf.Clamp(_mapPan.x, -maxX, maxX), Mathf.Clamp(_mapPan.y, -maxY, maxY));
            _mapContent.sizeDelta = new Vector2(size, size);
            _mapContent.anchoredPosition = _mapPan;
            if (rebuildPins) RefreshPins();
        }

        /// <summary>Zooms so the content point <paramref name="contentLocal"/> stays where it is on screen.</summary>
        private static void ZoomAround(Vector2 contentLocal, float zoom)
        {
            zoom = Mathf.Clamp(zoom, 1f, MapMaxZoom);
            if (Mathf.Abs(zoom - _mapZoom) < 0.001f) return;
            float k = zoom / _mapZoom;
            _mapPan -= contentLocal * (k - 1f);
            _mapZoom = zoom;
            ApplyMapTransform(true);
        }

        private static void FocusWorld(Vector3 world)
        {
            WorldData w = WorldData.Current;
            if (w == null || _mapContent == null) return;
            Vector2 uv = w.ToMap(world);
            float size = MapBaseSize() * _mapZoom;
            _mapPan = -(uv - new Vector2(0.5f, 0.5f)) * size;
            ApplyMapTransform(false);
        }

        // ---- input ----------------------------------------------------------------------------------------------

        // Polled from Tick: the overlay has no injected pointer handlers, and polling keeps drag, wheel and click in
        // one place. The raycast check keeps a dialog on top of the map from leaking clicks through to it.
        private static void WorldTick()
        {
            if (_mapContent == null || _mapViewport == null || _selected == null) return;
            Vector2 mouse = Input.mousePosition;

            // An open place menu owns the next press: inside it the buttons take it, anywhere else it closes the menu.
            if (_menu != null)
            {
                bool press = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
                if (press && !RectTransformUtility.RectangleContainsScreenPoint(_menu.GetComponent<RectTransform>(), mouse, null))
                {
                    CloseMenu();
                    RefreshPins();
                }
                return;
            }

            bool over = RectTransformUtility.RectangleContainsScreenPoint(_mapViewport, mouse, null) && MapIsTopmost(mouse);

            float wheel = Input.mouseScrollDelta.y;
            if (over && Mathf.Abs(wheel) > 0.01f && RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapContent, mouse, null, out Vector2 at))
                ZoomAround(at, _mapZoom * (wheel > 0f ? 1.25f : 0.8f));

            // Right-click lets go of the selected stop or start point.
            if (over && Input.GetMouseButtonDown(1) && _worldSel != SelNone) { _worldSel = SelNone; RefreshForm(); return; }

            if (over && Input.GetMouseButtonDown(0))
            {
                _mapPressed = true; _mapDragging = false;
                _mapPressAt = mouse; _mapPanAtPress = _mapPan;
                _dragPin = PinAt(mouse);
            }
            if (_mapPressed)
            {
                if (Input.GetMouseButton(0))
                {
                    Vector2 d = mouse - _mapPressAt;
                    if (!_mapDragging && d.magnitude > 5f) _mapDragging = true;
                    if (_mapDragging)
                    {
                        if (_dragPin != NoPin) MoveDragGhost(mouse);
                        else { _mapPan = _mapPanAtPress + d / CanvasScale(); ApplyMapTransform(false); }
                    }
                }
                else
                {
                    _mapPressed = false;
                    bool onMap = TryMouseToWorld(mouse, out Vector3 world);
                    if (_dragPin != NoPin)
                    {
                        int pin = _dragPin;
                        _dragPin = NoPin;
                        HideDragGhost();
                        if (!_mapDragging) SelectPin(pin);
                        else if (onMap) DropPin(pin, world);
                        else RefreshPins();
                    }
                    else if (!_mapDragging && onMap) MapClick(world);
                    _mapDragging = false;
                }
            }

            if (_mapHover == null) return;
            if (!over) { if (_lastHoverMouse.x >= 0f) { _lastHoverMouse = new Vector2(-1f, -1f); _mapHover.text = MapHint(); ShowHoverTarget(null); } return; }
            if ((mouse - _lastHoverMouse).sqrMagnitude < 0.5f) return;
            _lastHoverMouse = mouse;
            if (TryMouseToWorld(mouse, out Vector3 hover))
            {
                _mapHover.text = HoverText(hover);
                int pin = _mapPressed ? NoPin : PinAt(mouse);
                if (pin != NoPin) ShowPinTip(pin);
                else ShowHoverTarget(_mapPressed && _dragPin == NoPin ? null : HoverTarget(hover));
            }
            else { _mapHover.text = MapHint(); ShowHoverTarget(null); }
        }

        // The pin under the mouse: a stop index, SelSpawn, or NoPin.
        private static int PinAt(Vector2 mouse)
        {
            if (_mapContent == null || _pinHits.Count == 0) return NoPin;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapContent, mouse, null, out Vector2 local)) return NoPin;
            float size = _mapContent.rect.width;
            int best = NoPin; float bestD = PinHitRadius;
            foreach (var h in _pinHits)
            {
                float d = Vector2.Distance(local, (h.Uv - new Vector2(0.5f, 0.5f)) * size);
                if (d <= bestD) { bestD = d; best = h.Index; }
            }
            return best;
        }

        private static void SelectPin(int pin)
        {
            _worldSel = _worldSel == pin ? SelNone : pin;
            RefreshForm();
        }

        private static void DropPin(int pin, Vector3 world)
        {
            WorldData w = WorldData.Current;
            NpcDraft n = _selected;
            if (w == null || n == null) { Core.Log?.Warning("[world] pin dropped without a capture or an NPC - ignored."); RefreshPins(); return; }
            if (pin == SelSpawn)
            {
                Vector3 p = SetSpawnPoint(n, w, world);
                Toast.Show("Start point moved.", Severity.Success);
                Core.Log?.Msg($"[world] '{n.Name}' start point dragged to {p.x:0.0}/{p.z:0.0} ({SpawnRegion(n) ?? "no region"}).");
                RefreshForm(); MarkDirty();
                return;
            }
            JObject step = ScheduleEdit.Steps(n).Count > pin && pin >= 0 ? ScheduleEdit.Steps(n)[pin] as JObject : null;
            if (step == null) { Core.Log?.Warning($"[world] dragged stop {pin + 1} no longer exists - ignored."); RefreshPins(); return; }
            if (!PlaceStep(step, w, world)) { RefreshPins(); return; }
            Core.Log?.Msg($"[world] '{n.Name}' stop {pin + 1} dragged: {ScheduleEdit.Describe(step)}");
            RefreshForm(); MarkDirty();
        }

        private static void MoveDragGhost(Vector2 mouse)
        {
            if (_pinLayer == null || !TryMouseToUv(mouse, out Vector2 uv)) return;
            if (_dragGhost == null)
            {
                Color c = _dragPin == SelSpawn ? Theme.Success : KindColor(ScheduleEdit.KindOf(SelectedStepAt(_dragPin)));
                _dragGhost = AddDot(uv, 26f, Theme.WithAlpha(c, 0.75f));
            }
            var rt = _dragGhost.rectTransform; rt.anchorMin = rt.anchorMax = uv;
        }

        private static void HideDragGhost()
        {
            if (_dragGhost != null) UnityEngine.Object.Destroy(_dragGhost.gameObject);
            _dragGhost = null;
        }

        private static JObject SelectedStepAt(int index)
        {
            if (index < 0 || _selected == null || !(_selected.Schedule is JArray a) || index >= a.Count) return null;
            return a[index] as JObject;
        }

        private static float CanvasScale()
        {
            var c = _canvasGO != null ? _canvasGO.GetComponent<Canvas>() : null;
            return c != null && c.scaleFactor > 0.01f ? c.scaleFactor : 1f;
        }

        private static bool MapIsTopmost(Vector2 mouse)
        {
            EventSystem es = EventSystem.current;
            if (es == null) return true;   // nothing can sit on top of the map without an event system to click it
            var ped = new PointerEventData(es) { position = mouse };
            var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(ped, hits);
            if (hits.Count == 0) return false;
            GameObject top = hits[0].gameObject;
            return top != null && (top == _mapContent.gameObject || top == _mapViewport.gameObject);
        }

        private static bool TryMouseToWorld(Vector2 mouse, out Vector3 world)
        {
            world = default;
            WorldData w = WorldData.Current;
            if (w == null || !TryMouseToUv(mouse, out Vector2 uv)) return false;
            world = w.ToWorld(uv);
            return true;
        }

        private static bool TryMouseToUv(Vector2 mouse, out Vector2 uv)
        {
            uv = default;
            if (_mapContent == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapContent, mouse, null, out Vector2 local)) return false;
            Vector2 size = _mapContent.rect.size;
            uv = new Vector2(local.x / size.x + 0.5f, local.y / size.y + 0.5f);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        // Screen position of a world point on the map, for anchoring the place menu to a click made by the self-test.
        private static Vector2 WorldToScreen(Vector3 world)
        {
            WorldData w = WorldData.Current;
            if (w == null || _mapContent == null) return Vector2.zero;
            Vector2 local = (w.ToMap(world) - new Vector2(0.5f, 0.5f)) * _mapContent.rect.width;
            return RectTransformUtility.WorldToScreenPoint(null, _mapContent.TransformPoint(local));
        }

        // ---- clicks ---------------------------------------------------------------------------------------------

        private static void MapClick(Vector3 world)
        {
            WorldData w = WorldData.Current;
            NpcDraft n = _selected;
            if (w == null || n == null) { Core.Log?.Warning("[world] map click without a capture or an NPC - ignored."); return; }

            if (_worldSel == SelSpawn || (_worldSel == SelNone && !HasSpawn(n)))
            {
                Vector3 p = SetSpawnPoint(n, w, world);
                _worldSel = SelNone;
                Toast.Show("Start point set.", Severity.Success);
                Core.Log?.Msg($"[world] '{n.Name}' starts at {p.x:0.0}/{p.y:0.0}/{p.z:0.0} ({SpawnRegion(n) ?? "no region"}).");
                RefreshForm(); MarkDirty();
                return;
            }

            if (_worldSel == SelNone) { OpenMenu(n, w, world); return; }

            JObject sel = SelectedStep(n);
            if (sel == null) { _worldSel = SelNone; RefreshForm(); return; }
            if (!PlaceStep(sel, w, world)) return;
            Core.Log?.Msg($"[world] '{n.Name}' stop {_worldSel + 1} placed: {ScheduleEdit.Describe(sel)}");
            RefreshForm(); MarkDirty();
        }

        // Points a stop at whatever its kind needs near the world position. False (with a toast) when nothing fits.
        private static bool PlaceStep(JObject step, WorldData w, Vector3 world)
        {
            switch (ScheduleEdit.KindOf(step))
            {
                case StepKind.Walk:
                case StepKind.HangOut:
                    ScheduleEdit.SetPoint(step, w.SnapToGround(world, out _));
                    return true;
                case StepKind.Inside:
                {
                    Place b = w.NearestPlace(world, "building", BuildingPick);
                    if (b == null) { Toast.Show("No building door there.", Severity.Warning); return false; }
                    step["building"] = b.Name;
                    return true;
                }
                case StepKind.Sit:
                {
                    Place s = w.NearestPlace(world, "seat", SeatPick);
                    if (s == null) { Toast.Show("No seat there.", Severity.Warning); return false; }
                    SetSeat(step, s);
                    return true;
                }
                case StepKind.Drive:
                {
                    Place l = w.NearestPlace(world, "parking", ParkingPick);
                    if (l == null) { Toast.Show("No car park there.", Severity.Warning); return false; }
                    step["parkingLot"] = l.Name;
                    return true;
                }
                default:
                    Toast.Show("This stop needs no place.", Severity.Info);
                    return false;
            }
        }

        // ---- place menu -----------------------------------------------------------------------------------------

        // A click on the map asks what to do there, with only the choices that fit the spot.
        private static void OpenMenu(NpcDraft n, WorldData w, Vector3 world)
        {
            CloseMenu();
            int minutes = NextStopTime(n);
            Vector3 ground = w.SnapToGround(world, out _);
            Place b = w.NearestPlace(world, "building", MenuBuildingReach);
            Place seat = w.NearestPlace(world, "seat", MenuSeatReach);
            Place lot = w.NearestPlace(world, "parking", MenuParkingReach);

            if (b != null) _menuEntries.Add(("Go inside " + b.Label, KindColor(StepKind.Inside), () => AddStop(n, StepKind.Inside, minutes, s => s["building"] = b.Name)));
            if (seat != null) _menuEntries.Add(("Sit at " + seat.Label, KindColor(StepKind.Sit), () => AddStop(n, StepKind.Sit, minutes, s => SetSeat(s, seat))));
            _menuEntries.Add(("Walk here", KindColor(StepKind.Walk), () => AddStop(n, StepKind.Walk, minutes, s => ScheduleEdit.SetPoint(s, ground))));
            _menuEntries.Add(("Hang out here", KindColor(StepKind.HangOut), () => AddStop(n, StepKind.HangOut, minutes, s => ScheduleEdit.SetPoint(s, ground))));
            if (lot != null) _menuEntries.Add(("Drive to " + lot.Label, KindColor(StepKind.Drive), () => AddStop(n, StepKind.Drive, minutes, s => s["parkingLot"] = lot.Name)));
            _menuEntries.Add(("Start the day here", Theme.Success, () =>
            {
                CloseMenu();
                Vector3 p = SetSpawnPoint(n, w, world);
                Toast.Show("Start point moved.", Severity.Success);
                Core.Log?.Msg($"[world] '{n.Name}' starts at {p.x:0.0}/{p.y:0.0}/{p.z:0.0} ({SpawnRegion(n) ?? "no region"}).");
                RefreshForm(); MarkDirty();
            }));

            const float width = 260f, header = 30f, rowH = 32f, gap = 4f, pad = 6f;
            float height = pad * 2f + header + _menuEntries.Count * (rowH + gap) - gap;
            _menu = UIFactory.Panel("placeMenu", _mapRoot.transform, Theme.BgElevated);
            var bg = _menu.GetComponent<Image>(); if (bg != null) { bg.sprite = Theme.RoundedSprite(); bg.type = Image.Type.Sliced; }
            var mrt = _menu.GetComponent<RectTransform>();
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.5f); mrt.pivot = new Vector2(0, 1);
            mrt.sizeDelta = new Vector2(width, height);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapRoot.GetComponent<RectTransform>(), WorldToScreen(world), null, out Vector2 at);
            Rect root = _mapRoot.GetComponent<RectTransform>().rect;
            // Open beside the click, flipped to the other side when it would run off the panel.
            float x = at.x + 14f, y = at.y - 6f;
            if (x + width > root.xMax - 8f) x = at.x - 14f - width;
            if (y - height < root.yMin + 8f) y = root.yMin + 8f + height;
            mrt.anchoredPosition = new Vector2(Mathf.Max(root.xMin + 8f, x), Mathf.Min(root.yMax - 8f, y));

            var title = UIFactory.Text("title", $"New stop at {ScheduleEdit.FormatTime(minutes)}", _menu.transform, Theme.Label, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.color = Theme.TextMuted; title.raycastTarget = false;
            var trt = title.rectTransform; trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.offsetMin = new Vector2(pad + 6f, -(pad + header)); trt.offsetMax = new Vector2(-pad, -pad);

            for (int i = 0; i < _menuEntries.Count; i++)
            {
                var e = _menuEntries[i];
                var (go, btn, txt) = UIFactory.ButtonWithLabel("opt" + i, e.Label, _menu.transform, Theme.Button, 0, rowH);
                if (txt != null) { txt.fontSize = Theme.Body; txt.alignment = TextAnchor.MiddleLeft; txt.rectTransform.offsetMin = new Vector2(28, 0); }
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
                float top = pad + header + i * (rowH + gap);
                rt.offsetMin = new Vector2(pad, -(top + rowH)); rt.offsetMax = new Vector2(-pad, -top);
                var dot = new GameObject("kind"); dot.transform.SetParent(go.transform, false);
                var di = dot.AddComponent<Image>(); di.sprite = Dot(); di.color = e.Color; di.raycastTarget = false;
                var drt = di.rectTransform; drt.anchorMin = drt.anchorMax = new Vector2(0, 0.5f); drt.pivot = new Vector2(0, 0.5f); drt.sizeDelta = new Vector2(10, 10); drt.anchoredPosition = new Vector2(10, 0);
                Action act = e.Do;
                btn.onClick.AddListener((UnityAction)(() => act()));
            }
            Interactions.PolishButtons(_menu.transform);

            // Mark the clicked spot while the menu is open.
            if (_pinLayer != null) AddDot(w.ToMap(world), 12f, Theme.TextPrimary);
            Core.Log?.Msg($"[world] place menu at {world.x:0}, {world.z:0}: {string.Join(" | ", _menuEntries.ConvertAll(m => m.Label))}");
        }

        private static void CloseMenu()
        {
            if (_menu != null) UnityEngine.Object.Destroy(_menu);
            _menu = null;
            _menuEntries.Clear();
        }

        private static void AddStop(NpcDraft n, StepKind kind, int minutes, Action<JObject> fill)
        {
            CloseMenu();
            JObject step = ScheduleEdit.Add(n, kind, minutes);
            fill(step);
            // The new stop stays unselected so the next click adds the following stop of the route.
            int number = IndexOfStep(n, step) + 1;
            Toast.Show($"Stop {number} added.", Severity.Success);
            Core.Log?.Msg($"[world] '{n.Name}' new stop {number}: {ScheduleEdit.FormatTime(minutes)} {ScheduleEdit.Describe(step)}");
            RefreshForm(); MarkDirty();
        }

        // ---- undo -----------------------------------------------------------------------------------------------

        private static string Snapshot(NpcDraft n) =>
            new JArray(n.Spawn?.DeepClone() ?? JValue.CreateNull(), n.Contact?.DeepClone() ?? JValue.CreateNull(), n.Schedule?.DeepClone() ?? JValue.CreateNull())
                .ToString(Newtonsoft.Json.Formatting.None);

        // Called on every rebuild of the World tab: records the state when it differs from the last one.
        private static void TrackHistory()
        {
            NpcDraft n = _selected;
            if (n == null) return;
            if (!ReferenceEquals(_historyNpc, n)) { _historyNpc = n; _history.Clear(); }
            string now = Snapshot(n);
            if (_history.Count > 0 && _history[_history.Count - 1] == now) return;
            _history.Add(now);
            if (_history.Count > HistoryLimit) _history.RemoveAt(0);
        }

        private static void Undo()
        {
            NpcDraft n = _selected;
            if (n == null) return;
            TrackHistory();   // a switch flip does not rebuild the form, so it may not be recorded yet
            if (_history.Count < 2) { Core.Log?.Msg("[world] undo: nothing to undo."); return; }
            _history.RemoveAt(_history.Count - 1);
            JArray s;
            try { s = JArray.Parse(_history[_history.Count - 1]); }
            catch (Exception e) { Core.Log?.Error("[world] undo failed, the snapshot is unreadable: " + e.Message); _history.Clear(); return; }
            n.Spawn = s[0].Type == JTokenType.Null ? null : s[0];
            n.Contact = s[1].Type == JTokenType.Null ? null : s[1];
            n.Schedule = s[2].Type == JTokenType.Null ? null : s[2];
            _worldSel = SelNone;
            Core.Log?.Msg($"[world] '{n.Name}' undo: {_history.Count - 1} step(s) left.");
            RefreshForm(); MarkDirty();
        }

        private static void SetSeat(JObject step, Place seat)
        {
            step["seatSet"] = seat.Name;
            ScheduleEdit.SetText(step, "seatSetPath", seat.Path);
        }

        private static int NextStopTime(NpcDraft n)
        {
            int last = -1;
            foreach (JToken t in ScheduleEdit.Steps(n))
                if (ScheduleEdit.Minutes(t as JObject) is int m && m > last) last = m;
            return last < 0 ? 8 * 60 : Mathf.Min(last + 60, 23 * 60 + 45);
        }

        private static int IndexOfStep(NpcDraft n, JObject step)
        {
            JArray a = ScheduleEdit.Steps(n);
            for (int i = 0; i < a.Count; i++) if (ReferenceEquals(a[i], step)) return i;
            return SelNone;
        }

        private static JObject SelectedStep(NpcDraft n)
        {
            if (_worldSel < 0 || !(n.Schedule is JArray a) || _worldSel >= a.Count) return null;
            return a[_worldSel] as JObject;
        }

        // ---- spawn / contact ------------------------------------------------------------------------------------

        private static bool HasSpawn(NpcDraft n) => n.Spawn is JObject s && s["x"] != null && s["z"] != null;

        private static Vector3? SpawnPoint(NpcDraft n)
        {
            if (!(n.Spawn is JObject s) || s["x"] == null || s["z"] == null) return null;
            try { return new Vector3((float)s["x"], (float?)s["y"] ?? 0f, (float)s["z"]); }
            catch (Exception e) { Core.Log?.Warning($"[world] '{n.Name}' has an unreadable spawn point: {e.Message}"); return null; }
        }

        private static string SpawnRegion(NpcDraft n) => n.Spawn is JObject s ? (string)s["region"] : null;

        private static bool SpawnsInWorld(NpcDraft n)
        {
            if (!(n.Spawn is JObject s)) return false;
            if ((bool?)s["physical"] == false) return false;
            return (bool?)s["auto"] == true || _project?.AutoRegister == true;
        }

        // A start point placed here always means "lives in town": physical and spawned by Personnel on its own.
        private static Vector3 SetSpawnPoint(NpcDraft n, WorldData w, Vector3 world)
        {
            Vector3 p = w.SnapToGround(world, out _);
            if (!(n.Spawn is JObject s)) { s = new JObject(); n.Spawn = s; }
            s["x"] = Math.Round(p.x, 2); s["y"] = Math.Round(p.y, 2); s["z"] = Math.Round(p.z, 2);
            if (s["rotationY"] == null) s["rotationY"] = 0;
            string region = w.RegionAt(p);
            if (!string.IsNullOrEmpty(region)) s["region"] = region;
            s["physical"] = true;
            s["auto"] = true;
            return p;
        }

        private static bool MapMarkerOn(NpcDraft n) => !(n.Contact is JObject c) || (bool?)c["mapMarker"] != false;

        private static void SetMapMarker(NpcDraft n, bool on)
        {
            if (!(n.Contact is JObject c)) { c = new JObject(); n.Contact = c; }
            if (on) c.Remove("mapMarker"); else c["mapMarker"] = false;
            if (!c.HasValues) n.Contact = null;
        }

        // ---- form -----------------------------------------------------------------------------------------------

        private static void BuildWorldForm(NpcDraft n)
        {
            WorldData w = WorldData.Current;

            Components.SectionHeader(_formContent, "In town");
            Vector3? home = SpawnPoint(n);
            if (home == null)
            {
                Caption(w != null ? $"Click the map where {n.Name} starts the day." : "Load any save once to get the map.");
                if (_worldSel != SelSpawn) WideButton("Place in town", Theme.Accent, () => { _worldSel = SelSpawn; RefreshForm(); });
            }
            else
            {
                string where = w?.NearestPlaceName(home.Value) ?? $"{home.Value.x:0}, {home.Value.z:0}";
                string region = SpawnRegion(n);
                InfoRow("Start point", region != null ? where + " (" + region + ")" : where, _worldSel == SelSpawn);
                if (!SpawnsInWorld(n)) Caption("Only spawns when the pack registers its NPCs.");
                ButtonPair(_worldSel == SelSpawn ? "Click the map" : "Move", _worldSel == SelSpawn ? Theme.Accent : Theme.Button,
                    () => { _worldSel = _worldSel == SelSpawn ? SelNone : SelSpawn; RefreshForm(); },
                    "Take out of town", Theme.Button,
                    () => Components.ConfirmDialog(_canvasGO.transform, "Take out of town?", $"{n.Name} stays a phone contact only and the day plan does nothing.", "Take out",
                        () => { n.Spawn = null; _worldSel = SelNone; RefreshForm(); MarkDirty(); }));
                ToggleRow("Map marker", MapMarkerOn(n), v => SetMapMarker(n, v));
            }

            Components.SectionHeader(_formContent, "Day plan");
            JArray steps = ScheduleEdit.Steps(n);
            if (steps.Count == 0) Caption("No stops yet. Click the map to add one.");
            for (int i = 0; i < steps.Count; i++) StepListRow(n, i, steps[i] as JObject);
            WideButton("+ Add stop", Theme.Button, () =>
            {
                JObject s = ScheduleEdit.Add(n, StepKind.Walk, NextStopTime(n));
                _worldSel = IndexOfStep(n, s);
                RefreshForm(); MarkDirty();
            });

            JObject sel = SelectedStep(n);
            if (sel != null) StepEditor(n, sel);
        }

        private static void StepListRow(NpcDraft n, int index, JObject step)
        {
            if (step == null) return;
            bool selected = index == _worldSel;
            var row = new GameObject("step" + index); row.transform.SetParent(_formContent, false); row.AddComponent<RectTransform>();
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 34; rle.preferredHeight = 34; rle.flexibleWidth = 1;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;

            int? m = ScheduleEdit.Minutes(step);
            string label = (m.HasValue ? ScheduleEdit.FormatTime(m.Value) : "--:--") + "   " + ScheduleEdit.Describe(step);
            var (go, btn, txt) = UIFactory.ButtonWithLabel("pick", label, row.transform, selected ? Theme.Accent : Theme.Button, 0, 34);
            if (txt != null)
            {
                // One line that shrinks to fit, clear of the kind dot on the right.
                txt.fontSize = Theme.Body; txt.alignment = TextAnchor.MiddleLeft;
                txt.rectTransform.offsetMin = new Vector2(10, 0); txt.rectTransform.offsetMax = new Vector2(-26, 0);
                txt.horizontalOverflow = HorizontalWrapMode.Wrap; txt.verticalOverflow = VerticalWrapMode.Truncate;
                txt.resizeTextForBestFit = true; txt.resizeTextMaxSize = Theme.Body; txt.resizeTextMinSize = 10;
            }
            var ble = go.AddComponent<LayoutElement>(); ble.flexibleWidth = 1; ble.minWidth = 0;
            var dot = new GameObject("kind"); dot.transform.SetParent(go.transform, false);
            var di = dot.AddComponent<Image>(); di.sprite = Dot(); di.color = KindColor(ScheduleEdit.KindOf(step)); di.raycastTarget = false;
            var drt = di.rectTransform; drt.anchorMin = drt.anchorMax = new Vector2(1, 0.5f); drt.pivot = new Vector2(1, 0.5f); drt.sizeDelta = new Vector2(10, 10); drt.anchoredPosition = new Vector2(-10, 0);
            btn.onClick.AddListener((UnityAction)(() =>
            {
                _worldSel = selected ? SelNone : index;
                Vector3? at = StepWorld(step, WorldData.Current);
                if (!selected && at.HasValue && _mapZoom > 1.01f) FocusWorld(at.Value);
                RefreshForm();
            }));

            var (xgo, xbtn, xtxt) = UIFactory.ButtonWithLabel("rm", "✕", row.transform, Theme.Button, 34, 34);
            if (xtxt != null) xtxt.fontSize = Theme.Body;
            var xle = xgo.AddComponent<LayoutElement>(); xle.minWidth = 34; xle.preferredWidth = 34;
            xbtn.onClick.AddListener((UnityAction)(() =>
            {
                ScheduleEdit.Remove(n, step);
                if (_worldSel == index) _worldSel = SelNone; else if (_worldSel > index) _worldSel--;
                RefreshForm(); MarkDirty();
            }));
        }

        private static void StepEditor(NpcDraft n, JObject step)
        {
            StepKind kind = ScheduleEdit.KindOf(step);
            Components.SectionHeader(_formContent, "Stop " + (_worldSel + 1));

            if (kind == StepKind.Other)
            {
                Caption("Written by hand (" + ScheduleEdit.Text(step, "type") + "). Edit it in the pack file.");
            }
            else
            {
                var kinds = new List<PathOption>();
                foreach (StepKind k in ScheduleEdit.Offered) kinds.Add(new PathOption("Stop", ScheduleEdit.Label(k), k.ToString()));
                ChoiceRow("Do", ScheduleEdit.Label(kind), "What happens at this stop", kinds, kind.ToString(), v =>
                {
                    if (Enum.TryParse(v, out StepKind k)) { ScheduleEdit.SetKind(step, k); MarkDirty(); }
                });
            }

            int minutes = ScheduleEdit.Minutes(step) ?? 8 * 60;
            NudgeRow("Time", ScheduleEdit.FormatTime(minutes), new (string, int)[] { ("-1h", -60), ("-15m", -15), ("+15m", 15), ("+1h", 60) }, d =>
            {
                ScheduleEdit.SetMinutes(step, minutes + d);
                ScheduleEdit.Sort(n);
                _worldSel = IndexOfStep(n, step);
            });

            if (kind == StepKind.Inside || kind == StepKind.HangOut || kind == StepKind.Sit)
            {
                int dur = ScheduleEdit.Duration(step);
                NudgeRow("Stay", FormatDuration(dur), new (string, int)[] { ("-1h", -60), ("-15m", -15), ("+15m", 15), ("+1h", 60) },
                    d => ScheduleEdit.SetDuration(step, dur + d));
            }

            WorldData w = WorldData.Current;
            switch (kind)
            {
                case StepKind.Walk:
                case StepKind.HangOut:
                {
                    Vector3? p = ScheduleEdit.Point(step);
                    InfoRow("Place", p.HasValue ? (w?.NearestPlaceName(p.Value) ?? $"{p.Value.x:0}, {p.Value.z:0}") : "Click the map", !p.HasValue);
                    if (kind == StepKind.HangOut)
                    {
                        string act = ScheduleEdit.Text(step, "action");
                        int idx = Math.Max(0, Array.IndexOf(ScheduleEdit.HangOutActions, act.Length == 0 ? "none" : act));
                        var row = Components.FormRow(_formContent, "Doing", null, out Transform slot, stacked: false, height: 30);
                        var seg = Components.Segmented(slot, new[] { "Idle", "Smoke", "Drink", "Graffiti" }, idx, i =>
                        {
                            ScheduleEdit.SetText(step, "action", i == 0 ? null : ScheduleEdit.HangOutActions[i]);
                            MarkDirty();
                            RefreshForm();
                        }, out _);
                        StretchFill(seg.GetComponent<RectTransform>());
                    }
                    break;
                }
                case StepKind.Inside:
                    PlaceRow("Building", "building", ScheduleEdit.Text(step, "building"), v => step["building"] = v);
                    break;
                case StepKind.Sit:
                {
                    string cur = ScheduleEdit.Text(step, "seatSetPath");
                    if (cur.Length == 0) cur = ScheduleEdit.Text(step, "seatSet");
                    PlaceRow("Seat", "seat", cur, v =>
                    {
                        Place s = w?.Places.Find(p => p.Kind == "seat" && (p.Path == v || p.Name == v));
                        if (s != null) SetSeat(step, s); else step["seatSet"] = v;
                    });
                    break;
                }
                case StepKind.Drive:
                    PlaceRow("Car park", "parking", ScheduleEdit.Text(step, "parkingLot"), v => step["parkingLot"] = v);
                    Caption("The NPC needs a car of its own for this.");
                    break;
                case StepKind.Atm:
                case StepKind.Vending:
                    Caption("Uses the nearest one.");
                    break;
            }

            ToggleRow("Teleport if late", (bool?)step["warpIfSkipped"] == true, v =>
            {
                if (v) step["warpIfSkipped"] = true; else step.Remove("warpIfSkipped");
            });

            ButtonPair("Done", Theme.Accent, () => { _worldSel = SelNone; RefreshForm(); },
                "Remove stop", Theme.Button, () => { ScheduleEdit.Remove(n, step); _worldSel = SelNone; RefreshForm(); MarkDirty(); });
        }

        private static string FormatDuration(int minutes) => ScheduleEdit.FormatDuration(minutes);

        // A place picker over the captured places of one kind, grouped by region.
        private static void PlaceRow(string label, string kind, string current, Action<string> set)
        {
            WorldData w = WorldData.Current;
            var options = new List<PathOption>();
            string currentKey = current;
            string shown = current.Length > 0 ? current : "Click the map";
            if (w != null)
            {
                foreach (Place p in w.Places)
                {
                    if (p.Kind != kind) continue;
                    string key = kind == "seat" && !string.IsNullOrEmpty(p.Path) ? p.Path : p.Name;
                    string display = kind == "seat" ? p.Label + "  (" + (w.NearestPlace(p.Position, "building", 60f)?.Label ?? $"{p.X:0}, {p.Z:0}") + ")" : p.Label;
                    if (key == current || p.Name == current) { currentKey = key; if (kind == "seat") shown = display; }
                    options.Add(new PathOption(w.RegionAt(p.Position) ?? "Outside town", display, key));
                }
                options.Sort((a, b) => a.Group != b.Group ? string.CompareOrdinal(a.Group, b.Group) : string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));
            }
            ChoiceRow(label, shown, "Choose a " + label.ToLowerInvariant(), options, currentKey, set, current.Length == 0);
        }

        private static void ChoiceRow(string label, string shown, string title, List<PathOption> options, string currentKey, Action<string> set, bool muted = false)
        {
            var row = Components.FormRow(_formContent, label, null, out Transform slot, stacked: false, height: 30);
            var wrap = new GameObject("pick"); wrap.transform.SetParent(slot, false);
            StretchFill(wrap.AddComponent<RectTransform>());
            var img = wrap.AddComponent<Image>(); img.color = Theme.SurfaceInput; img.sprite = Theme.RoundedSprite(); img.type = Image.Type.Sliced;
            var btn = wrap.AddComponent<Button>(); btn.targetGraphic = img;
            var txt = UIFactory.Text("t", shown, wrap.transform, Theme.Body, TextAnchor.MiddleLeft);
            txt.color = muted ? Theme.TextMuted : Theme.TextPrimary; txt.raycastTarget = false;
            var trt = txt.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(10, 0); trt.offsetMax = new Vector2(-26, 0);
            var chevron = UIFactory.Text("chev", "▾", wrap.transform, Theme.Caption, TextAnchor.MiddleRight);
            chevron.color = Theme.TextMuted; chevron.raycastTarget = false;
            var crt = chevron.rectTransform; crt.anchorMin = new Vector2(1, 0); crt.anchorMax = Vector2.one; crt.pivot = new Vector2(1, 0.5f); crt.offsetMin = new Vector2(-22, 0); crt.offsetMax = new Vector2(-8, 0);
            btn.onClick.AddListener((UnityAction)(() =>
                OptionPicker.Show(_canvasGO.transform, title, options, currentKey, false, chosen =>
                {
                    if (string.IsNullOrEmpty(chosen)) return;
                    set(chosen);
                    RefreshForm(); MarkDirty();
                })));
        }

        // "label   [-1h] [-15m]  value  [+15m] [+1h]" - for times and durations, where a slider is too coarse.
        private static void NudgeRow(string label, string value, (string Label, int Delta)[] nudges, Action<int> apply)
        {
            var row = Components.FormRow(_formContent, label, null, out Transform slot, stacked: false, height: 30);
            var wrap = new GameObject("nudge"); wrap.transform.SetParent(slot, false);
            StretchFill(wrap.AddComponent<RectTransform>());
            var h = wrap.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            int half = nudges.Length / 2;
            for (int i = 0; i <= nudges.Length; i++)
            {
                if (i == half)
                {
                    var v = UIFactory.Text("v", value, wrap.transform, Theme.Label, TextAnchor.MiddleCenter, FontStyle.Bold);
                    v.color = Theme.TextPrimary; v.raycastTarget = false;
                    var vle = v.gameObject.AddComponent<LayoutElement>(); vle.minWidth = 44; vle.preferredWidth = 44; vle.flexibleWidth = 1;
                }
                if (i == nudges.Length) break;
                var (l, d) = nudges[i];
                var (go, btn, txt) = UIFactory.ButtonWithLabel("n" + i, l, wrap.transform, Theme.Button, 36, 28);
                if (txt != null) txt.fontSize = Theme.Caption;
                var ble = go.AddComponent<LayoutElement>(); ble.minWidth = 36; ble.preferredWidth = 36; ble.flexibleWidth = 0;
                btn.onClick.AddListener((UnityAction)(() => { apply(d); RefreshForm(); MarkDirty(); }));
            }
        }

        private static void InfoRow(string label, string value, bool highlight)
        {
            var row = Components.FormRow(_formContent, label, null, out Transform slot, stacked: false, height: 30);
            var t = UIFactory.Text("v", value, slot, Theme.Body, TextAnchor.MiddleLeft);
            t.color = highlight ? Theme.AccentBorder : Theme.TextPrimary; t.raycastTarget = false;
            StretchFill(t.rectTransform);
        }

        private static void WideButton(string label, Color color, Action onClick)
        {
            var (go, btn, _) = UIFactory.ButtonWithLabel("wb_" + label, label, _formContent, color, 0, 34);
            go.AddComponent<LayoutElement>().minHeight = 34;
            btn.onClick.AddListener((UnityAction)(() => onClick()));
        }

        private static void ButtonPair(string a, Color ca, Action onA, string b, Color cb, Action onB)
        {
            var row = new GameObject("pair"); row.transform.SetParent(_formContent, false); row.AddComponent<RectTransform>();
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 34; rle.preferredHeight = 34; rle.flexibleWidth = 1;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            var (ga, ba, _) = UIFactory.ButtonWithLabel("a", a, row.transform, ca, 0, 34);
            ga.AddComponent<LayoutElement>().flexibleWidth = 1;
            ba.onClick.AddListener((UnityAction)(() => onA()));
            var (gb, bb, _2) = UIFactory.ButtonWithLabel("b", b, row.transform, cb, 0, 34);
            gb.AddComponent<LayoutElement>().flexibleWidth = 1;
            bb.onClick.AddListener((UnityAction)(() => onB()));
        }

        // ---- pins -----------------------------------------------------------------------------------------------

        private static Color KindColor(StepKind k) => k switch
        {
            StepKind.Walk => Theme.Accent,
            StepKind.Inside => Theme.Info,
            StepKind.HangOut => Theme.Warning,
            StepKind.Sit => new Color(0.66f, 0.42f, 0.86f, 1f),
            StepKind.Drive => new Color(0.30f, 0.72f, 0.78f, 1f),
            StepKind.Atm => Theme.Success,
            StepKind.Vending => Theme.Success,
            _ => Theme.TextMuted
        };

        /// <summary>Where a step happens, for drawing it: its own point, or the place it names.</summary>
        private static Vector3? StepWorld(JObject step, WorldData w)
        {
            if (step == null) return null;
            switch (ScheduleEdit.KindOf(step))
            {
                case StepKind.Inside: return w?.FindPlace("building", ScheduleEdit.Text(step, "building"))?.Position;
                case StepKind.Sit: return w?.FindPlace("seat", ScheduleEdit.Text(step, "seatSet"), ScheduleEdit.Text(step, "seatSetPath"))?.Position;
                case StepKind.Drive: return w?.FindPlace("parking", ScheduleEdit.Text(step, "parkingLot"))?.Position;
                default: return ScheduleEdit.Point(step);
            }
        }

        private static void RefreshPins()
        {
            if (_pinLayer == null || _mapContent == null) return;
            // Destroy runs at the end of the frame; hiding first keeps the old pins out of this frame's picture.
            for (int i = 0; i < _pinLayer.childCount; i++) _pinLayer.GetChild(i).gameObject.SetActive(false);
            UIFactory.ClearChildren(_pinLayer);
            _pinHits.Clear();
            _hoverRing = null; _hoverLabel = null; _dragGhost = null;
            RefreshTimeline();
            WorldData w = WorldData.Current;
            NpcDraft n = _selected;
            if (w == null || n == null) return;
            float size = MapBaseSize() * _mapZoom;

            // Places the selected stop can point at, so the target is visible before the click.
            JObject sel = SelectedStep(n);
            string kind = null;
            bool faint = false;
            if (sel != null)
            {
                StepKind k = ScheduleEdit.KindOf(sel);
                kind = k == StepKind.Inside ? "building" : k == StepKind.Sit ? "seat" : k == StepKind.Drive ? "parking" : null;
            }
            else if (_worldSel == SelNone && HasSpawn(n)) { kind = "building"; faint = true; }
            if (kind != null)
            {
                Color c = faint ? Theme.WithAlpha(Theme.Info, 0.5f) : Theme.WithAlpha(Theme.InfoText, 0.9f);
                foreach (Place p in w.Places)
                    if (p.Kind == kind) AddDot(w.ToMap(p.Position), faint ? 7f : 10f, c);
            }

            // Route: start point, then every stop in time order.
            var route = new List<Vector2>();
            Vector3? home = SpawnPoint(n);
            if (home.HasValue) route.Add(w.ToMap(home.Value));
            JArray steps = n.Schedule as JArray;
            var stepAt = new List<(int Index, Vector2 Uv, StepKind Kind)>();
            if (steps != null)
                for (int i = 0; i < steps.Count; i++)
                {
                    Vector3? p = StepWorld(steps[i] as JObject, w);
                    if (!p.HasValue) continue;
                    Vector2 uv = w.ToMap(p.Value);
                    route.Add(uv);
                    stepAt.Add((i, uv, ScheduleEdit.KindOf(steps[i] as JObject)));
                }
            for (int i = 1; i < route.Count; i++) AddLine(route[i - 1], route[i], size, Theme.WithAlpha(Theme.TextPrimary, 0.55f));

            if (home.HasValue) { AddPin(w.ToMap(home.Value), "S", Theme.Success, _worldSel == SelSpawn); _pinHits.Add((SelSpawn, w.ToMap(home.Value))); }
            foreach (var s in stepAt) { AddPin(s.Uv, (s.Index + 1).ToString(), KindColor(s.Kind), s.Index == _worldSel); _pinHits.Add((s.Index, s.Uv)); }

            // Hover feedback sits on top of everything: a ring on the place a click would use, with its name.
            _hoverRing = AddImage(Vector2.zero, 34f, Ring(), Theme.TextPrimary);
            var tip = UIFactory.Panel("tip", _hoverRing.transform, Theme.WithAlpha(Theme.BgDeep, 0.88f));
            var tipBg = tip.GetComponent<Image>(); if (tipBg != null) { tipBg.sprite = Theme.RoundedSprite(); tipBg.type = Image.Type.Sliced; tipBg.raycastTarget = false; }
            var tipRt = tip.GetComponent<RectTransform>();
            tipRt.anchorMin = tipRt.anchorMax = new Vector2(1f, 0.5f); tipRt.pivot = new Vector2(0f, 0.5f);
            tipRt.anchoredPosition = new Vector2(6f, 0f); tipRt.sizeDelta = new Vector2(120f, 24f);
            _hoverLabel = UIFactory.Text("t", "", tip.transform, Theme.Label, TextAnchor.MiddleCenter, FontStyle.Bold);
            _hoverLabel.color = Theme.TextPrimary; _hoverLabel.raycastTarget = false;
            StretchFill(_hoverLabel.rectTransform);
            _hoverRing.gameObject.SetActive(false);

            if (_mapHover != null && _lastHoverMouse.x < 0f) _mapHover.text = MapHint();
        }

        // ---- hover target ---------------------------------------------------------------------------------------

        /// <summary>The place a click (or a drop) at this point would use, or null for a plain point.</summary>
        private static Place HoverTarget(Vector3 world)
        {
            WorldData w = WorldData.Current;
            NpcDraft n = _selected;
            if (w == null || n == null) return null;
            JObject step = _dragPin >= 0 ? SelectedStepAt(_dragPin) : _dragPin == NoPin ? SelectedStep(n) : null;
            if (step != null)
            {
                switch (ScheduleEdit.KindOf(step))
                {
                    case StepKind.Inside: return w.NearestPlace(world, "building", BuildingPick);
                    case StepKind.Sit: return w.NearestPlace(world, "seat", SeatPick);
                    case StepKind.Drive: return w.NearestPlace(world, "parking", ParkingPick);
                    default: return null;
                }
            }
            if (_dragPin != NoPin || _worldSel != SelNone || !HasSpawn(n)) return null;
            // Place menu: the closest thing it would offer to go into or sit at.
            Place b = w.NearestPlace(world, "building", MenuBuildingReach);
            Place s = w.NearestPlace(world, "seat", MenuSeatReach);
            if (b == null) return s;
            if (s == null) return b;
            return Vector3.Distance(s.Position, world) < Vector3.Distance(b.Position, world) ? s : b;
        }

        private static void ShowHoverTarget(Place p)
        {
            WorldData w = WorldData.Current;
            if (p == null || w == null) ShowHover(null, null);
            else ShowHover(w.ToMap(p.Position), p.Label);
        }

        // Hovering a pin names what happens there.
        private static void ShowPinTip(int pin)
        {
            foreach (var h in _pinHits)
            {
                if (h.Index != pin) continue;
                if (pin == SelSpawn) { ShowHover(h.Uv, "Start of the day"); return; }
                JObject step = SelectedStepAt(pin);
                int? m = ScheduleEdit.Minutes(step);
                ShowHover(h.Uv, $"{pin + 1}.  {(m.HasValue ? ScheduleEdit.FormatTime(m.Value) + "  " : "")}{ScheduleEdit.Describe(step)}");
                return;
            }
            ShowHover(null, null);
        }

        private static void ShowHover(Vector2? uv, string label)
        {
            if (_hoverRing == null) return;
            if (!uv.HasValue) { if (_hoverRing.gameObject.activeSelf) _hoverRing.gameObject.SetActive(false); return; }
            var rt = _hoverRing.rectTransform; rt.anchorMin = rt.anchorMax = uv.Value;
            _hoverRing.gameObject.SetActive(true);
            if (_hoverLabel != null && _hoverLabel.text != label)
            {
                _hoverLabel.text = label;
                var tipRt = _hoverLabel.transform.parent.GetComponent<RectTransform>();
                tipRt.sizeDelta = new Vector2(_hoverLabel.preferredWidth + 18f, 24f);
            }
        }

        // ---- day timeline ---------------------------------------------------------------------------------------

        // A 24 hour strip under the map: one block per stop, from its start until it ends or the next stop begins.
        private static void RefreshTimeline()
        {
            if (_timeline == null) return;
            for (int i = 0; i < _timeline.childCount; i++) _timeline.GetChild(i).gameObject.SetActive(false);
            UIFactory.ClearChildren(_timeline);
            NpcDraft n = _selected;
            if (n == null) return;
            const float day = 24f * 60f, padX = 8f, labelH = 12f;

            float trackW = Mathf.Max(1f, _timeline.rect.width - padX * 2f);

            // Ticks first so the track and its blocks draw over them.
            for (int h = 0; h <= 24; h += 3)
            {
                var tick = new GameObject("h" + h); tick.transform.SetParent(_timeline, false);
                var ti = tick.AddComponent<Image>(); ti.color = Theme.Hairline; ti.raycastTarget = false;
                var tirt = ti.rectTransform; tirt.anchorMin = new Vector2(0, 0); tirt.anchorMax = new Vector2(0, 1); tirt.pivot = new Vector2(0.5f, 0);
                tirt.sizeDelta = new Vector2(1f, 0f); tirt.anchoredPosition = new Vector2(padX + trackW * h / 24f, 0f);
                if (h % 6 != 0 || h == 24) continue;
                var label = UIFactory.Text("l" + h, h.ToString("00") + ":00", _timeline, Theme.Caption, TextAnchor.LowerLeft);
                label.color = Theme.TextMuted; label.raycastTarget = false;
                var lrt = label.rectTransform; lrt.anchorMin = lrt.anchorMax = new Vector2(0, 0); lrt.pivot = new Vector2(0, 0);
                lrt.sizeDelta = new Vector2(60f, labelH + 2f); lrt.anchoredPosition = new Vector2(padX + trackW * h / 24f + 3f, 0f);
            }

            var track = new GameObject("track"); track.transform.SetParent(_timeline, false);
            var trt = track.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(padX, labelH + 2f); trt.offsetMax = new Vector2(-padX, -4f);

            JArray steps = n.Schedule as JArray;
            if (steps == null || steps.Count == 0)
            {
                // Backed so the hour ticks do not run through the words.
                var back = UIFactory.Panel("none", track.transform, Theme.SurfaceInput);
                back.GetComponent<Image>().raycastTarget = false;
                var none = UIFactory.Text("t", "Stops show up here across the day.", back.transform, Theme.Caption, TextAnchor.MiddleCenter);
                none.color = Theme.TextMuted; none.raycastTarget = false; StretchFill(none.rectTransform);
                var brt = back.GetComponent<RectTransform>(); brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = new Vector2(none.preferredWidth + 20f, 18f);
                return;
            }
            for (int i = 0; i < steps.Count; i++)
            {
                JObject step = steps[i] as JObject;
                int? start = ScheduleEdit.Minutes(step);
                if (!start.HasValue) continue;
                int next = -1;
                for (int j = i + 1; j < steps.Count; j++)
                    if (ScheduleEdit.Minutes(steps[j] as JObject) is int m && m > start.Value) { next = m; break; }
                StepKind kind = ScheduleEdit.KindOf(step);
                bool stays = kind == StepKind.Inside || kind == StepKind.HangOut || kind == StepKind.Sit;
                float end = stays ? start.Value + ScheduleEdit.Duration(step) : next > 0 ? next : start.Value + 30f;
                if (next > 0) end = Mathf.Min(end, next);
                end = Mathf.Min(Mathf.Max(end, start.Value + 15f), day);

                int index = i;
                bool selected = index == _worldSel;
                var (go, btn, txt) = UIFactory.ButtonWithLabel("s" + i, (i + 1).ToString(), track.transform, KindColor(kind), 0, 0);
                if (txt != null) { txt.fontSize = Theme.Caption; txt.color = Color.white; }
                var brt = go.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(start.Value / day, 0f); brt.anchorMax = new Vector2(end / day, 1f);
                brt.offsetMin = new Vector2(1f, selected ? -2f : 1f); brt.offsetMax = new Vector2(-1f, selected ? 2f : -1f);
                if (txt != null && (end - start.Value) / day * trackW < 14f) txt.text = "";
                if (selected) { var ol = go.AddComponent<Outline>(); ol.effectColor = Theme.TextPrimary; ol.effectDistance = new Vector2(2f, 2f); }
                btn.onClick.AddListener((UnityAction)(() =>
                {
                    _worldSel = _worldSel == index ? SelNone : index;
                    Vector3? at = StepWorld(SelectedStepAt(index), WorldData.Current);
                    if (_worldSel == index && at.HasValue && _mapZoom > 1.01f) FocusWorld(at.Value);
                    RefreshForm();
                }));
            }
            Interactions.PolishButtons(track.transform);
        }

        private static Sprite Dot()
        {
            if (_dot != null) return _dot;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            float r = size / 2f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
                }
            tex.Apply();
            _dot = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _dot.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _dot;
        }

        private static Sprite Ring()
        {
            if (_ring != null) return _ring;
            const int size = 64;
            const float thickness = 5f;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            float r = size / 2f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                    float a = Mathf.Clamp01(r - d + 0.5f) * Mathf.Clamp01(d - (r - thickness) + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            _ring = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _ring.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _ring;
        }

        private static Image AddDot(Vector2 uv, float diameter, Color color) => AddImage(uv, diameter, Dot(), color);

        private static Image AddImage(Vector2 uv, float diameter, Sprite sprite, Color color)
        {
            var go = new GameObject("dot");
            go.transform.SetParent(_pinLayer, false);
            var img = go.AddComponent<Image>(); img.sprite = sprite; img.color = color; img.raycastTarget = false;
            var rt = img.rectTransform; rt.anchorMin = rt.anchorMax = uv; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameter, diameter); rt.anchoredPosition = Vector2.zero;
            return img;
        }

        private static void AddPin(Vector2 uv, string label, Color color, bool selected)
        {
            float d = selected ? 30f : 22f;
            AddDot(uv, d + 4f, selected ? Theme.TextPrimary : new Color(0f, 0f, 0f, 0.6f));
            Image pin = AddDot(uv, d, color);
            var t = UIFactory.Text("n", label, pin.transform, selected ? Theme.Label : Theme.Caption, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.color = Color.white; t.raycastTarget = false;
            StretchFill(t.rectTransform);
        }

        private static void AddLine(Vector2 a, Vector2 b, float size, Color color)
        {
            Vector2 pa = (a - new Vector2(0.5f, 0.5f)) * size, pb = (b - new Vector2(0.5f, 0.5f)) * size;
            Vector2 d = pb - pa;
            if (d.sqrMagnitude < 1f) return;
            var go = new GameObject("route");
            go.transform.SetParent(_pinLayer, false);
            var img = go.AddComponent<Image>(); img.color = color; img.raycastTarget = false;
            var rt = img.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(d.magnitude, 3f);
            rt.anchoredPosition = (pa + pb) / 2f;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        // ---- hover / hint ---------------------------------------------------------------------------------------

        private static string MapHint()
        {
            NpcDraft n = _selected;
            if (n == null) return "";
            if (WorldData.Current == null) return "No map yet.";
            if (_worldSel == SelSpawn || (_worldSel == SelNone && !HasSpawn(n))) return $"Click where {n.Name} starts the day.";
            if (_worldSel == SelNone) return "Click the map to add a stop. Drag a pin to move it.";
            JObject sel = SelectedStep(n);
            string done = " Right-click when done.";
            switch (sel != null ? ScheduleEdit.KindOf(sel) : StepKind.Other)
            {
                case StepKind.Walk:
                case StepKind.HangOut: return $"Click to move stop {_worldSel + 1}." + done;
                case StepKind.Inside: return $"Click a building for stop {_worldSel + 1}." + done;
                case StepKind.Sit: return $"Click a seat for stop {_worldSel + 1}." + done;
                case StepKind.Drive: return $"Click a car park for stop {_worldSel + 1}." + done;
                case StepKind.Atm:
                case StepKind.Vending: return "This stop uses the nearest machine.";
                default: return "This stop is edited in the pack file.";
            }
        }

        // The status line while hovering: the region under the mouse, then what a click does.
        private static string HoverText(Vector3 world)
        {
            WorldData w = WorldData.Current;
            if (w == null) return "";
            string region = w.RegionAt(world) ?? "Outside town";
            return region + "  ·  " + MapHint();
        }
    }
}
