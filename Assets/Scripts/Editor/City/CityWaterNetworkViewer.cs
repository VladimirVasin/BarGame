using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Editor
{
    public sealed class CityWaterNetworkViewer : EditorWindow
    {
        private CityLayout preview;
        private CityWaterNetwork network;
        private string[] labels;
        private int selected;
        private readonly List<Vector3> supply = new(), sewer = new();

        [MenuItem("Bar Promenade/City/Water And Sewer")]
        private static void Open() => GetWindow<CityWaterNetworkViewer>("Water and sewer");

        private void OnEnable() => SceneView.duringSceneGui += Draw;
        private void OnDisable() => SceneView.duringSceneGui -= Draw;

        private void OnGUI()
        {
            var city = Application.isPlaying ? FindAnyObjectByType<CityGameRoot>() : null;
            if (Application.isPlaying && (city == null || !city.IsInitialized))
            {
                EditorGUILayout.HelpBox("Сеть доступна после загрузки города.", MessageType.Info);
                return;
            }
            if (!Application.isPlaying && preview == null)
            {
                if (!GUILayout.Button("Построить план города")) return;
                preview = CityLayoutGenerator.Generate(CityBlueprintCatalog.Resolve(CityBlueprintCatalog.DefaultBlueprintId),
                    CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            }
            var current = CityWaterNetwork.ForLayout(city == null ? preview : city.Layout);
            if (!current.Enabled) return;
            if (current != network)
            {
                network = current;
                selected = 0;
                labels = new string[network.Services.Count];
                for (int i = 0; i < labels.Length; i++) labels[i] = network.Services[i].Id;
                Trace();
            }
            int choice = EditorGUILayout.Popup("Здание", selected, labels);
            if (choice != selected)
            {
                selected = choice;
                Trace();
            }
            GUILayout.Label("Синий — подача воды     Коричневый — сток до моря");
            if (GUILayout.Button("К вводу здания") && SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(network.Services[selected].Inlet, Vector3.one * 12f), false);
        }

        private void Trace()
        {
            network.Trace(network.Services[selected], supply, sewer);
            SceneView.RepaintAll();
        }

        private void Draw(SceneView view)
        {
            if (network == null) return;
            CompareFunction previous = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always;
                using (new Handles.DrawingScope(new Color(.20f, .70f, 1f))) Line(supply);
                using (new Handles.DrawingScope(new Color(.93f, .51f, .22f))) Line(sewer);
            }
            finally { Handles.zTest = previous; }
        }

        private static void Line(List<Vector3> points)
        {
            for (int i = 1; i < points.Count; i++) Handles.DrawLine(points[i-1], points[i], 3f);
        }
    }
}
