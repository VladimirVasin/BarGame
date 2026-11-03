using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Surveys the proposed relief against the actual street geometry. This
    /// window owns its draft; it never replaces the playable elevation plan,
    /// changes scene objects or publishes the surveyed player map.
    /// </summary>
    public sealed class CityTerracePlanWindow : EditorWindow
    {
        private static readonly string[] Views = { "Сейчас", "Террасы", "Разрез", "Транспорт", "Профиль дороги" };
        private static readonly string[] Banks = { "Запад", "Восток" };
        private static readonly Color LowColor = new Color(0.25f, 0.52f, 0.63f);
        private static readonly Color HighColor = new Color(0.88f, 0.67f, 0.30f);
        private static readonly Color ConflictColor = new Color(0.93f, 0.34f, 0.31f);

        [SerializeField] private int seed = GameSessionState.DefaultCitySeed;
        [SerializeField] private float summit = 40f;
        [SerializeField] private int view = 3;
        private CityLayout layout;
        private CityTerraceMasterPlan plan;
        private CityTerraceTransportPlan transport;
        private Vector2 scroll;
        private int selectedConflict = -1;
        private int selectedCrossing = -1;
        private int selectedBank;
        private float routeDistance;
        private string failure;
        private GUIContent[] conflictLabels;
        private GUIContent[] crossingLabels;
        private GUIStyle smallLabel;
        private readonly Vector3[] corridorQuad = new Vector3[4];

        [MenuItem("Bar Promenade/City/Multilevel Plan")]
        public static void Open()
        {
            var window = GetWindow<CityTerracePlanWindow>("Террасы города");
            window.minSize = new Vector2(660f, 760f);
            window.Rebuild();
            window.Show();
        }

        [MenuItem("Bar Promenade/City/Play Terrace Courtyard Pilot")]
        public static void PlayPilot()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.playModeStartScene = null;
            var root = new GameObject("Terrace Courtyard Pilot");
            root.AddComponent<CityTerracePilotRoot>();
            Selection.activeGameObject = root;
            EditorApplication.isPlaying = true;
        }

        private void OnDisable()
        {
            // No scene callbacks, objects, textures or persistent cache to own.
            layout = null;
            plan = null;
            transport = null;
            conflictLabels = null;
            crossingLabels = null;
        }

        private void Rebuild()
        {
            failure = null;
            selectedConflict = -1;
            selectedCrossing = -1;
            routeDistance = 0f;
            scroll = Vector2.zero;
            try
            {
                CityLayout candidate = CityLayoutGenerator.Generate(
                    CityBlueprintCatalog.Default, CityGenerationSettings.Default, seed);
                CityTerraceMasterPlan draft = CityTerraceMasterPlan.Create(candidate, summit);
                CityTerraceTransportPlan routeDraft = CityTerraceTransportPlan.Create(candidate, draft);
                var labels = new GUIContent[draft.RoadConflicts.Count];
                for (int i = 0; i < labels.Length; i++)
                {
                    CityTerraceRoadConflict conflict = draft.RoadConflicts[i];
                    labels[i] = new GUIContent(
                        $"{conflict.Edge}: {conflict.ProposedGradePercent:0.0}% / допуск {conflict.AllowedGradePercent:0.0}%",
                        "Выделить улицу на схеме. Это диагностика исходной трассы, а не готовый новый маршрут.");
                }
                layout = candidate;
                plan = draft;
                transport = routeDraft;
                conflictLabels = labels;
                crossingLabels = new GUIContent[transport.RoadCrossings.Count];
                for (int i = 0; i < crossingLabels.Length; i++)
                {
                    CityTerraceRoadCrossing crossing = transport.RoadCrossings[i];
                    crossingLabels[i] = new GUIContent(
                        $"{(crossing.RouteId == transport.Routes[0].Id ? "Запад" : "Восток")} · {crossing.RoadEdge} · " +
                        $"Y {crossing.RouteDatumY:0.0} / {crossing.RoadDatumY:0.0} · {CrossingName(crossing.Kind)}", crossing.Id);
                }
            }
            catch (Exception exception)
            {
                layout = null;
                plan = null;
                transport = null;
                conflictLabels = null;
                crossingLabels = null;
                failure = exception.Message;
            }
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Этап 1 · профиль и перепланировка улиц", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Авторский черновик. Игровой город и его карта не изменяются. Красные улицы требуют новой трассы или отдельной пешей связи.",
                MessageType.Info);
            seed = EditorGUILayout.IntField("Seed", seed);
            summit = EditorGUILayout.Slider("Верхняя отметка, м", summit, 35f, 45f);
            if (GUILayout.Button("Рассчитать по действующей планировке")) Rebuild();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Пройти пилот дворов · 7,68 м")) PlayPilot();
            if (!string.IsNullOrEmpty(failure)) EditorGUILayout.HelpBox(failure, MessageType.Error);
            if (plan == null) return;

            if (seed != plan.Seed || !Mathf.Approximately(summit, plan.TargetSummitElevation))
                EditorGUILayout.HelpBox("Параметры изменены. Схема показывает последний расчёт.", MessageType.Warning);

            EditorGUILayout.LabelField(
                $"Рассчитано: seed {plan.Seed}, верхняя отметка {plan.TargetSummitElevation:0.0} м");
            EditorGUILayout.LabelField(
                $"Отметки улиц: сейчас {plan.CurrentMinimumElevation:0.0}–{plan.CurrentMaximumElevation:0.0} м; " +
                $"предложение {plan.TargetMinimumElevation:0.0}–{plan.TargetMaximumElevation:0.0} м.");
            EditorGUILayout.LabelField(
                $"Улиц с превышением уклона: {plan.RoadConflicts.Count}. Подъём транспорта: ≥ {plan.MinimumTransportClimbLength:0} м.");
            EditorGUILayout.LabelField(
                "Длина — нижняя оценка без ровных остановок, поворотов и проверки габаритов.", EditorStyles.wordWrappedMiniLabel);

            if (view >= 3 && transport != null)
            {
                foreach (CityTerraceTransportRoute route in transport.Routes)
                    EditorGUILayout.LabelField($"{(route.Bank == CityTerraceTransportBank.West ? "Запад" : "Восток")}: {route.TotalHorizontalLength:0} м; уклон {route.MaximumGradePercent:0.0}%; верх ≤ {route.MaximumReachableSummitElevation:0.0} м");
                EditorGUILayout.LabelField($"Полоса {CityTerraceTransportPlan.CorridorWidth:0} м · " +
                    (transport.HasBuildingConflicts ? "есть пересечения с застройкой" : "застройка и защищённые дворы свободны"));
                if (transport.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.GradeLimit))
                    EditorGUILayout.HelpBox("Запрошенная высота превышает допустимый уклон. Уменьшите верхнюю отметку или удлините трассу.", MessageType.Error);
                EditorGUILayout.HelpBox("Не подключено к игровому городу. Нужны дорожные стыки, подходы к входам, габаритный проход машин и опоры на грунт. Разные Y не разрешают мост или тоннель.", MessageType.Warning);
            }

            view = GUILayout.Toolbar(view, Views);
            if (view == 4)
            {
                int bank = GUILayout.Toolbar(selectedBank, Banks);
                if (bank != selectedBank) { selectedBank = bank; selectedCrossing = -1; }
                CityTerraceTransportRoute route = transport.Routes[selectedBank];
                routeDistance = EditorGUILayout.Slider("Путь от берега, м", routeDistance, 0f, route.TotalHorizontalLength);
                CityRoadElevationSample sample = route.ElevationProfile.SampleDistance(routeDistance);
                EditorGUILayout.LabelField($"Y {sample.Height:0.00} м · уклон {sample.SignedGradePercent:0.00}% · " +
                    SegmentName(route.Segments[sample.SegmentIndex].Kind));
            }
            Rect drawing = GUILayoutUtility.GetRect(100f, Mathf.Clamp(position.height * 0.34f, 180f, 320f), GUILayout.ExpandWidth(true));
            if (view == 2) DrawSection(drawing);
            else if (view == 4) DrawRouteProfile(drawing);
            else DrawPlan(drawing);
            EditorGUILayout.LabelField(view == 2
                ? "Синий: запад. Охра: восток. Серый: текущие высоты. Разрез по внешним улицам ядра."
                : view == 4 ? "По горизонтали — длина пути. Красный: две высоты дорожного пересечения. Серый: ровные участки."
                : view == 3 ? "Синий/охра: трассы. Серый: здания. Красный: нерешённые стыки. Белый: выбор."
                : "Синий → охра: низко → высоко. Север и море сверху.", EditorStyles.miniLabel);

            if (selectedConflict >= 0 && selectedConflict < plan.RoadConflicts.Count)
            {
                CityTerraceRoadConflict conflict = plan.RoadConflicts[selectedConflict];
                EditorGUILayout.HelpBox(
                    $"{conflict.Edge}\nТекущий уклон {conflict.CurrentGradePercent:0.0}% → предлагаемый {conflict.ProposedGradePercent:0.0}%. " +
                    $"Нужный горизонтальный путь с ровными концами: ≥ {conflict.RequiredHorizontalRun:0.0} м. " +
                    "Доступность транспорта и пешеходов ещё не подтверждена.", MessageType.Warning);
            }
            EditorGUILayout.LabelField(view >= 3 ? "Дорожные пересечения · выбрать для разреза" : "Улицы для перепланировки", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (view >= 3)
            {
                if (selectedCrossing >= 0 && selectedCrossing < transport.RoadCrossings.Count)
                {
                    CityTerraceRoadCrossing crossing = transport.RoadCrossings[selectedCrossing];
                    EditorGUILayout.HelpBox($"{crossing.Id}\nX {crossing.WorldXZ.x:0.00} · Z {crossing.WorldXZ.y:0.00}; " +
                        $"дорога проекта Y {crossing.RouteDatumY:0.00}, существующая Y {crossing.RoadDatumY:0.00}. " +
                        $"Разница {crossing.SignedSurfaceSeparation:0.00} м — не высота свободного проезда.", MessageType.Info);
                }
                for (int i = 0; i < crossingLabels.Length; i++)
                {
                    CityTerraceRoadCrossing crossing = transport.RoadCrossings[i];
                    if (view == 4 && crossing.RouteId != transport.Routes[selectedBank].Id) continue;
                    if (!GUILayout.Button(crossingLabels[i], i == selectedCrossing ? EditorStyles.miniButton : EditorStyles.label)) continue;
                    selectedCrossing = i;
                    selectedBank = crossing.RouteId == transport.Routes[0].Id ? 0 : 1;
                    routeDistance = CrossingDistance(transport.Routes[selectedBank], crossing);
                    Repaint();
                }
                if (view == 3)
                {
                    EditorGUILayout.LabelField("Занятые участки", EditorStyles.boldLabel);
                    foreach (CityTerraceReservation reservation in transport.Reservations)
                        if (reservation.Kind != CityTerraceReservationKind.ExistingRoad)
                            EditorGUILayout.LabelField($"{reservation.RouteId}: {reservation.Kind} · {reservation.Id}", EditorStyles.miniLabel);
                }
            }
            else for (int i = 0; i < conflictLabels.Length; i++)
            {
                if (GUILayout.Button(conflictLabels[i], i == selectedConflict ? EditorStyles.miniButton : EditorStyles.label))
                {
                    selectedConflict = i;
                    Repaint();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static string CrossingName(CityTerraceRoadCrossingKind kind) => kind == CityTerraceRoadCrossingKind.CoastalAttachment
            ? "береговой стык" : kind == CityTerraceRoadCrossingKind.AtGradeUnresolved ? "одна отметка, стык не решён" : "разные отметки, связь не решена";

        private static string SegmentName(CityTerraceTransportSegmentKind kind) => kind == CityTerraceTransportSegmentKind.Climb
            ? "подъём" : kind == CityTerraceTransportSegmentKind.StopLanding ? "остановка" :
            kind == CityTerraceTransportSegmentKind.TurnLanding ? "ровный поворот" : "ровный подход";

        private static float CrossingDistance(CityTerraceTransportRoute route, CityTerraceRoadCrossing crossing)
        {
            CityTerraceTransportSegment segment = route.Segments[crossing.SegmentIndex];
            Vector2 a = new Vector2(segment.Start.x, segment.Start.z), b = new Vector2(segment.End.x, segment.End.z);
            float t = Mathf.Clamp01(Vector2.Dot(crossing.WorldXZ - a, b - a) / (b - a).sqrMagnitude);
            return route.Samples[crossing.SegmentIndex].HorizontalDistance + segment.HorizontalLength * t;
        }

        private void DrawRouteProfile(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, new Color(.13f, .13f, .13f));
            Rect plot = new Rect(rect.x + 44f, rect.y + 16f, rect.width - 60f, rect.height - 44f);
            CityTerraceTransportRoute route = transport.Routes[selectedBank];
            Vector2 Point(float distance, float height) => new Vector2(
                Mathf.Lerp(plot.xMin, plot.xMax, distance / route.TotalHorizontalLength),
                Mathf.Lerp(plot.yMax, plot.yMin, height / 45f));
            Color previous = Handles.color;
            Handles.BeginGUI();
            try
            {
                for (int height = 0; height <= 40; height += 10)
                {
                    float y = Point(0f, height).y;
                    Handles.color = new Color(.5f, .5f, .5f, .3f);
                    Handles.DrawLine(new Vector2(plot.xMin, y), new Vector2(plot.xMax, y));
                    GUI.Label(new Rect(rect.x + 3f, y - 8f, 42f, 18f), $"{height} м", EditorStyles.miniLabel);
                }
                for (int i = 0; i < route.Segments.Count; i++)
                {
                    Handles.color = route.Segments[i].Kind == CityTerraceTransportSegmentKind.Climb
                        ? (selectedBank == 0 ? LowColor : HighColor) : Color.gray;
                    Handles.DrawLine(Point(route.Samples[i].HorizontalDistance, route.Samples[i].Position.y),
                        Point(route.Samples[i + 1].HorizontalDistance, route.Samples[i + 1].Position.y), 2f);
                }
                if (selectedCrossing >= 0)
                {
                    CityTerraceRoadCrossing crossing = transport.RoadCrossings[selectedCrossing];
                    if (crossing.RouteId == route.Id)
                    {
                        float distance = CrossingDistance(route, crossing);
                        Handles.color = ConflictColor;
                        Handles.DrawLine(Point(distance, crossing.RoadDatumY), Point(distance, crossing.RouteDatumY), 3f);
                    }
                }
                Vector2 marker = Point(routeDistance, route.ElevationProfile.SampleDistance(routeDistance).Height);
                EditorGUI.DrawRect(new Rect(marker.x - 3f, marker.y - 3f, 6f, 6f), Color.white);
                GUI.Label(new Rect(plot.xMin, plot.yMax + 4f, 110f, 18f), "0 м · берег", EditorStyles.miniLabel);
                GUI.Label(new Rect(plot.xMax - 110f, plot.yMax + 4f, 110f, 18f), $"{route.TotalHorizontalLength:0} м · верх", EditorStyles.miniLabel);
            }
            finally { Handles.EndGUI(); Handles.color = previous; }
        }

        private void DrawSection(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.88f, 0.88f, 0.88f));
            Rect plot = new Rect(rect.x + 48f, rect.y + 20f, rect.width - 65f, rect.height - 48f);
            Rect core = plan.CoreWorldXZBounds;
            Vector2 Point(Vector2Int node, float height)
            {
                Vector3 world = layout.GetNodeWorldPosition(node);
                return new Vector2(
                    Mathf.Lerp(plot.xMin, plot.xMax, Mathf.InverseLerp(core.yMax, core.yMin, world.z)),
                    Mathf.Lerp(plot.yMax, plot.yMin, height / 45f));
            }

            int westX = int.MaxValue, eastX = int.MinValue;
            int row = layout.Blueprint.River.CoreMinimumZ;
            foreach (Vector2Int node in layout.Nodes)
            {
                if (node.y != row) continue;
                Vector3 world = layout.GetNodeWorldPosition(node);
                if (world.x < core.xMin - .001f || world.x > core.xMax + .001f) continue;
                westX = Mathf.Min(westX, node.x);
                eastX = Mathf.Max(eastX, node.x);
            }
            Color oldColor = Handles.color;
            Handles.BeginGUI();
            try
            {
                for (int height = 0; height <= 40; height += 10)
                {
                    float y = Mathf.Lerp(plot.yMax, plot.yMin, height / 45f);
                    Handles.color = new Color(.5f, .5f, .5f, .3f);
                    Handles.DrawLine(new Vector2(plot.xMin, y), new Vector2(plot.xMax, y));
                    GUI.Label(new Rect(rect.x + 3f, y - 8f, 42f, 18f), $"{height} м", EditorStyles.miniLabel);
                }
                for (int side = 0; side < 2; side++)
                {
                    int x = side == 0 ? westX : eastX;
                    for (int z = row; z < layout.Blueprint.River.CoreMaximumZExclusive; z++)
                    {
                        var a = new Vector2Int(x, z);
                        var b = new Vector2Int(x, z + 1);
                        if (!plan.NodeElevations.ContainsKey(a) || !plan.NodeElevations.ContainsKey(b)) continue;
                        Handles.color = new Color(.6f, .6f, .6f, .7f);
                        Handles.DrawLine(Point(a, layout.ElevationPlan.GetNodeElevation(a)), Point(b, layout.ElevationPlan.GetNodeElevation(b)));
                        Handles.color = side == 0 ? LowColor : HighColor;
                        Handles.DrawLine(Point(a, plan.GetNodeElevation(a)), Point(b, plan.GetNodeElevation(b)), 2f);
                    }
                }
                GUI.Label(new Rect(plot.xMin, plot.yMax + 6f, 100f, 18f), "Берег · север", EditorStyles.miniLabel);
                GUI.Label(new Rect(plot.xMax - 100f, plot.yMax + 6f, 100f, 18f), "В глубину · юг", EditorStyles.miniLabel);
            }
            finally
            {
                Handles.EndGUI();
                Handles.color = oldColor;
            }
        }

        private void DrawPlan(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (smallLabel == null) smallLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            Color oldColor = Handles.color;
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.88f, 0.88f, 0.88f));
            Rect bounds = layout.WorldXZBounds;
            float scale = Mathf.Min((rect.width - 24f) / bounds.width, (rect.height - 24f) / bounds.height);
            Vector2 center = rect.center;
            Vector2 Map(Vector2 world) => center + new Vector2(world.x - bounds.center.x, bounds.center.y - world.y) * scale;
            Vector2 Node(Vector2Int node)
            {
                Vector3 world = layout.GetNodeWorldPosition(node);
                return Map(new Vector2(world.x, world.z));
            }

            Handles.BeginGUI();
            try
            {
                foreach (RoadEdge edge in layout.RoadEdges)
                {
                    Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.55f);
                    // Sample the real path: the city's bent streets are not
                    // accurately represented by a straight endpoint chord.
                    CityRoadPath path = layout.RoadGeometry.Get(edge);
                    Vector2 previous = Map(path.SampleDistance(0f).Position);
                    const int samples = 12;
                    for (int i = 1; i <= samples; i++)
                    {
                        Vector2 next = Map(path.SampleDistance(path.Length * i / samples).Position);
                        Handles.DrawLine(previous, next);
                        previous = next;
                    }
                }

                if (view == 1)
                {
                    for (int i = 0; i < plan.RoadConflicts.Count; i++)
                    {
                        RoadEdge edge = plan.RoadConflicts[i].Edge;
                        Handles.color = i == selectedConflict ? Color.white : ConflictColor;
                        CityRoadPath path = layout.RoadGeometry.Get(edge);
                        Vector2 previous = Map(path.SampleDistance(0f).Position);
                        for (int sample = 1; sample <= 12; sample++)
                        {
                            Vector2 next = Map(path.SampleDistance(path.Length * sample / 12f).Position);
                            Handles.DrawLine(previous, next, i == selectedConflict ? 3f : 1.5f);
                            previous = next;
                        }
                    }
                }

                foreach (Vector2Int node in layout.Nodes)
                {
                    float height = view != 0 ? plan.GetNodeElevation(node) : layout.ElevationPlan.GetNodeElevation(node);
                    Vector2 point = Node(node);
                    Color color = Color.Lerp(LowColor, HighColor, Mathf.Clamp01(height / plan.TargetSummitElevation));
                    EditorGUI.DrawRect(new Rect(point.x - 2.5f, point.y - 2.5f, 5f, 5f), color);
                }

                if (view == 3 && transport != null)
                {
                    foreach (CityTerraceTransportFootprint footprint in transport.Footprints)
                    {
                        if (footprint.Kind == CityTerraceReservationKind.Coast || footprint.Kind == CityTerraceReservationKind.River) continue;
                        Handles.color = footprint.Kind == CityTerraceReservationKind.Building
                            ? new Color(.66f, .69f, .70f, .65f)
                            : footprint.Kind == CityTerraceReservationKind.ProtectedAccess
                                ? new Color(.5f, .75f, .55f, .65f) : new Color(.8f, .6f, .4f, .7f);
                        for (int i = 0; i < footprint.Polygon.Count; i++)
                            Handles.DrawLine(Map(footprint.Polygon[i]), Map(footprint.Polygon[(i + 1) % footprint.Polygon.Count]));
                    }
                    foreach (CityTerraceTransportRoute route in transport.Routes)
                    {
                        Color bankColor = route.Bank == CityTerraceTransportBank.West ? LowColor : HighColor;
                        Handles.color = new Color(bankColor.r, bankColor.g, bankColor.b, .22f);
                        foreach (IReadOnlyList<Vector2> polygon in route.CorridorPolygons)
                        {
                            for (int i = 0; i < 4; i++) corridorQuad[i] = Map(polygon[i]);
                            Handles.DrawAAConvexPolygon(corridorQuad);
                        }
                    }
                    foreach (CityTerraceReservation reservation in transport.Reservations)
                    {
                        Vector2 topLeft = Map(new Vector2(reservation.Bounds.xMin, reservation.Bounds.yMax));
                        Vector2 bottomRight = Map(new Vector2(reservation.Bounds.xMax, reservation.Bounds.yMin));
                        EditorGUI.DrawRect(Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y),
                            new Color(ConflictColor.r, ConflictColor.g, ConflictColor.b, .08f));
                    }
                    foreach (CityTerraceTransportRoute route in transport.Routes)
                    {
                        Handles.color = route.Bank == CityTerraceTransportBank.West ? LowColor : HighColor;
                        foreach (CityTerraceTransportSegment segment in route.Segments)
                            Handles.DrawLine(Map(new Vector2(segment.Start.x, segment.Start.z)),
                                Map(new Vector2(segment.End.x, segment.End.z)), 2f);
                    }
                    if (selectedCrossing >= 0 && selectedCrossing < transport.RoadCrossings.Count)
                    {
                        Vector2 point = Map(transport.RoadCrossings[selectedCrossing].WorldXZ);
                        EditorGUI.DrawRect(new Rect(point.x - 4f, point.y - 4f, 8f, 8f), Color.white);
                    }

                }

                // Stable survey labels, not additional in-fiction map labels.
                CityRiverDefinition river = layout.Blueprint.River;
                Vector2 upper = Node(new Vector2Int(river.CorridorCellX, river.CoreMaximumZExclusive));
                Vector2 lower = Node(new Vector2Int(river.CorridorCellX, river.CoreMinimumZ));
                GUI.Label(new Rect(upper.x - 38f, upper.y - 19f, 76f, 18f), "Море · север", smallLabel);
                GUI.Label(new Rect(lower.x - 36f, lower.y + 3f, 72f, 18f), "Низкая река", smallLabel);
            }
            finally
            {
                Handles.EndGUI();
                Handles.color = oldColor;
            }
        }
    }
}
