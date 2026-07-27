using UnityEngine;

namespace BarPromenade
{
    public sealed class BarInteriorRoot : MonoBehaviour
    {
        private sealed class InteriorWalkableArea : IWalkableArea
        {
            private readonly Rect bounds;

            public InteriorWalkableArea(Rect boundsToUse)
            {
                bounds = boundsToUse;
            }

            public bool Contains(Vector3 position, float radius = 0f)
            {
                return position.x >= bounds.xMin + radius &&
                       position.x <= bounds.xMax - radius &&
                       position.z >= bounds.yMin + radius &&
                       position.z <= bounds.yMax - radius;
            }

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius)
            {
                float minX = bounds.xMin + radius;
                float maxX = bounds.xMax - radius;
                float minZ = bounds.yMin + radius;
                float maxZ = bounds.yMax - radius;
                desiredPosition.x = Mathf.Clamp(desiredPosition.x, minX, maxX);
                desiredPosition.z = Mathf.Clamp(desiredPosition.z, minZ, maxZ);
                return desiredPosition;
            }
        }

        public bool IsInitialized { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public BarMusicPlayer Music { get; private set; }
        public BarAmbiencePlayer Ambience { get; private set; }
        public BarCounterStation CounterStation { get; private set; }
        public CocktailMinigameController CocktailMinigame
        {
            get;
            private set;
        }

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            Camera camera = RuntimeSceneSetup.EnsureBarInterior();
            Audio = RetroAudioService.EnsureInstalled();
            BuildRoom();

            GameObject musicObject = new GameObject("Bar Music");
            musicObject.transform.SetParent(transform, false);
            Music = musicObject.AddComponent<BarMusicPlayer>();
            GameObject ambienceObject =
                new GameObject("Bar Ambience");
            ambienceObject.transform.SetParent(transform, false);
            Ambience =
                ambienceObject.AddComponent<BarAmbiencePlayer>();

            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPromptView prompt = ui.AddComponent<InteractionPromptView>();
            IntoxicationHudView intoxicationHud =
                ui.AddComponent<IntoxicationHudView>();

            IWalkableArea walkableArea = new InteriorWalkableArea(
                new Rect(-5.25f, -4.25f, 10.5f, 8.5f));
            Player = PlayerFactory.Create(
                transform,
                new Vector3(0f, 0.12f, -2.6f),
                camera,
                walkableArea,
                prompt);

            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow == null)
            {
                follow = camera.gameObject.AddComponent<PlayerCameraFollow>();
            }

            follow.Initialize(camera, Player.GameObject.transform, true);
            CocktailMinigameView minigameView =
                ui.AddComponent<CocktailMinigameView>();
            CocktailMinigame =
                ui.AddComponent<CocktailMinigameController>();
            CocktailMinigame.Initialize(
                minigameView,
                intoxicationHud,
                Player,
                follow);

            IntoxicationStatusController intoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            intoxicationStatus.Initialize(Player.Motor, Player.Visual);

            BuildCounterStation();
            BuildExit();
            IsInitialized = true;
        }

        private void BuildRoom()
        {
            Transform room = new GameObject(
                $"Interior {GameSessionState.ActiveBarId}").transform;
            room.SetParent(transform, false);

            Color floor = new Color(0.22f, 0.11f, 0.07f);
            Color wall = new Color(0.50f, 0.22f, 0.16f);
            Color trim = new Color(0.92f, 0.60f, 0.22f);
            Color furniture = new Color(0.12f, 0.055f, 0.035f);
            RuntimePrimitiveFactory.CreateBox(
                "Floor", room, new Vector3(0f, -0.12f, 0f),
                new Vector3(12f, 0.24f, 10f), floor);
            RuntimePrimitiveFactory.CreateBox(
                "Back Wall", room, new Vector3(0f, 1.6f, 5f),
                new Vector3(12f, 3.2f, 0.3f), wall);
            RuntimePrimitiveFactory.CreateBox(
                "Left Wall", room, new Vector3(-6f, 1.6f, 0f),
                new Vector3(0.3f, 3.2f, 10f), wall);
            RuntimePrimitiveFactory.CreateBox(
                "Right Wall", room, new Vector3(6f, 1.6f, 0f),
                new Vector3(0.3f, 3.2f, 10f), wall);
            RuntimePrimitiveFactory.CreateBox(
                "Front Wall Left", room, new Vector3(-3.7f, 1.6f, -5f),
                new Vector3(4.6f, 3.2f, 0.3f), wall);
            RuntimePrimitiveFactory.CreateBox(
                "Front Wall Right", room, new Vector3(3.7f, 1.6f, -5f),
                new Vector3(4.6f, 3.2f, 0.3f), wall);

            RuntimePrimitiveFactory.CreateBox(
                "Bar Counter", room, new Vector3(0f, 0.65f, 3.35f),
                new Vector3(6.6f, 1.3f, 0.8f), furniture);
            RuntimePrimitiveFactory.CreateBox(
                "Counter Trim", room, new Vector3(0f, 1.34f, 3.35f),
                new Vector3(6.9f, 0.12f, 1f), trim, false);

            BuildTable(room, new Vector3(-2.7f, 0f, 0.2f), furniture, trim);
            BuildTable(room, new Vector3(2.7f, 0f, 0.2f), furniture, trim);
            BuildTable(room, new Vector3(-2.4f, 0f, -2f), furniture, trim);
            BuildTable(room, new Vector3(2.4f, 0f, -2f), furniture, trim);
        }

        private static void BuildTable(
            Transform parent,
            Vector3 position,
            Color baseColor,
            Color topColor)
        {
            RuntimePrimitiveFactory.CreateCylinder(
                "Table Leg", parent, position + (Vector3.up * 0.45f),
                new Vector3(0.16f, 0.45f, 0.16f), baseColor);
            RuntimePrimitiveFactory.CreateCylinder(
                "Table Top", parent, position + (Vector3.up * 0.92f),
                new Vector3(0.78f, 0.08f, 0.78f), topColor);
        }

        private void BuildExit()
        {
            GameObject exit = new GameObject("Bar Exit");
            exit.transform.SetParent(transform, false);
            exit.transform.localPosition = new Vector3(0f, 0.9f, -4.25f);
            BoxCollider trigger = exit.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.8f, 1.8f, 1.3f);
            exit.AddComponent<BarExit>();

            RuntimePrimitiveFactory.CreateBox(
                "Exit Header", transform, new Vector3(0f, 2.35f, -4.82f),
                new Vector3(2.8f, 0.35f, 0.35f),
                new Color(0.92f, 0.60f, 0.22f),
                false);
        }

        private void BuildCounterStation()
        {
            GameObject station = new GameObject(
                "Cocktail Minigame Station");
            station.transform.SetParent(transform, false);
            station.transform.localPosition = new Vector3(-3.85f, 0.9f, 3.35f);
            BoxCollider trigger = station.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.2f, 1.8f, 1.2f);
            CounterStation = station.AddComponent<BarCounterStation>();
            CounterStation.Configure(CocktailMinigame);

            Color markerColor = new Color(0.96f, 0.67f, 0.18f);
            RuntimePrimitiveFactory.CreateBox(
                "Order Point",
                transform,
                new Vector3(-3.85f, 0.07f, 3.35f),
                new Vector3(0.72f, 0.10f, 0.72f),
                markerColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Order Point Sign",
                transform,
                new Vector3(-3.85f, 1.75f, 3.72f),
                new Vector3(0.82f, 0.48f, 0.10f),
                markerColor,
                false);
        }
    }
}
