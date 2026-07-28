using UnityEngine;

namespace BarPromenade
{
    public sealed class BarInteriorRoot : MonoBehaviour
    {
        private string activeBarId = string.Empty;

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
        public BarActivityKind ActiveActivity { get; private set; }
        public BarActivityStation ActivityStation { get; private set; }
        public IBarMinigame ActiveMinigame { get; private set; }
        public MinigameDebugWindow DebugWindow { get; private set; }
        public CocktailMinigameController CocktailMinigame
        {
            get;
            private set;
        }
        public BeerPongMinigameController BeerPongMinigame
        {
            get;
            private set;
        }
        public SplitTheGMinigameController SplitTheGMinigame
        {
            get;
            private set;
        }
        public TinctureMatchMinigameController TinctureMatchMinigame
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
            activeBarId = GameSessionState.ActiveBarId;
            ActiveActivity = ResolveActivity(
                GameSessionState.ActiveBarActivity);
            BuildRoom(ActiveActivity);

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
            BuildMinigame(ui, intoxicationHud, follow);

            IntoxicationStatusController intoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            intoxicationStatus.Initialize(Player.Motor, Player.Visual);
            DebugWindow = ui.AddComponent<MinigameDebugWindow>();
            DebugWindow.Initialize(
                Player,
                follow,
                intoxicationHud,
                null,
                ActiveMinigame);

            BuildActivityStation();
            BuildExit();
            IsInitialized = true;
        }

        private void OnDestroy()
        {
            if (ActiveMinigame != null)
            {
                ActiveMinigame.Completed -= HandleMinigameCompleted;
            }
        }

        private void BuildRoom(BarActivityKind activity)
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

            if (activity == BarActivityKind.BeerPong)
            {
                BuildBeerPongTable(room, furniture, trim);
            }
            else
            {
                BuildTable(
                    room,
                    new Vector3(-2.7f, 0f, 0.2f),
                    furniture,
                    trim);
                BuildTable(
                    room,
                    new Vector3(2.7f, 0f, 0.2f),
                    furniture,
                    trim);
                BuildTable(
                    room,
                    new Vector3(-2.4f, 0f, -2f),
                    furniture,
                    trim);
                BuildTable(
                    room,
                    new Vector3(2.4f, 0f, -2f),
                    furniture,
                    trim);
                if (activity == BarActivityKind.SplitTheG)
                {
                    BuildSplitTheGDisplay(room, furniture, trim);
                }
                else if (activity == BarActivityKind.TinctureMatch)
                {
                    BuildTinctureMatchDisplay(room, furniture, trim);
                }
            }
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

        private static void BuildBeerPongTable(
            Transform parent,
            Color baseColor,
            Color trimColor)
        {
            Color tableColor = new Color(0.08f, 0.23f, 0.25f);
            Vector3 tableCenter = new Vector3(0f, 0.92f, 0.45f);
            RuntimePrimitiveFactory.CreateBox(
                "Beer Pong Table",
                parent,
                tableCenter,
                new Vector3(2.35f, 0.14f, 4.25f),
                tableColor);

            Vector3[] legPositions =
            {
                new Vector3(-0.9f, 0.43f, -1.25f),
                new Vector3(0.9f, 0.43f, -1.25f),
                new Vector3(-0.9f, 0.43f, 2.15f),
                new Vector3(0.9f, 0.43f, 2.15f)
            };
            for (int index = 0; index < legPositions.Length; index++)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Beer Pong Table Leg {index + 1}",
                    parent,
                    legPositions[index],
                    new Vector3(0.16f, 0.86f, 0.16f),
                    baseColor);
            }

            RuntimePrimitiveFactory.CreateBox(
                "Beer Pong Center Line",
                parent,
                tableCenter + (Vector3.up * 0.08f),
                new Vector3(2.1f, 0.025f, 0.06f),
                trimColor,
                false);

            Vector3[] cupPositions =
            {
                new Vector3(0f, 1.15f, 1.45f),
                new Vector3(-0.27f, 1.15f, 1.75f),
                new Vector3(0.27f, 1.15f, 1.75f),
                new Vector3(-0.54f, 1.15f, 2.05f),
                new Vector3(0f, 1.15f, 2.05f),
                new Vector3(0.54f, 1.15f, 2.05f)
            };
            Color cupColor = new Color(0.78f, 0.17f, 0.12f);
            for (int index = 0; index < cupPositions.Length; index++)
            {
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Beer Pong Cup {index + 1}",
                    parent,
                    cupPositions[index],
                    new Vector3(0.22f, 0.16f, 0.22f),
                    cupColor,
                    false);
            }
        }

        private static void BuildSplitTheGDisplay(
            Transform parent,
            Color baseColor,
            Color trimColor)
        {
            Vector3 displayPosition =
                new Vector3(2.15f, 1.42f, 3.34f);
            RuntimePrimitiveFactory.CreateCylinder(
                "Split the G Coaster",
                parent,
                displayPosition,
                new Vector3(0.38f, 0.035f, 0.38f),
                baseColor,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Split the G Pint",
                parent,
                displayPosition + (Vector3.up * 0.30f),
                new Vector3(0.25f, 0.30f, 0.25f),
                new Color(0.36f, 0.16f, 0.055f),
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Split the G Foam",
                parent,
                displayPosition + (Vector3.up * 0.61f),
                new Vector3(0.26f, 0.045f, 0.26f),
                new Color(0.94f, 0.83f, 0.61f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Split the G Target",
                parent,
                displayPosition +
                new Vector3(0f, 0.32f, -0.26f),
                new Vector3(0.31f, 0.045f, 0.025f),
                trimColor,
                false);
        }

        private static void BuildTinctureMatchDisplay(
            Transform parent,
            Color baseColor,
            Color trimColor)
        {
            Vector3 trayPosition = new Vector3(0f, 1.43f, 3.34f);
            RuntimePrimitiveFactory.CreateBox(
                "Tincture Match Tray",
                parent,
                trayPosition,
                new Vector3(2.15f, 0.08f, 0.62f),
                baseColor,
                false);

            Color[] tinctureColors =
            {
                new Color(0.66f, 0.08f, 0.10f),
                new Color(0.94f, 0.44f, 0.08f),
                new Color(0.20f, 0.12f, 0.48f),
                new Color(0.13f, 0.48f, 0.24f),
                new Color(0.74f, 0.57f, 0.20f)
            };
            for (int index = 0; index < tinctureColors.Length; index++)
            {
                float x = -0.76f + (index * 0.38f);
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Tincture Shot {index + 1}",
                    parent,
                    trayPosition + new Vector3(x, 0.18f, 0f),
                    new Vector3(0.22f, 0.16f, 0.22f),
                    tinctureColors[index],
                    false);
            }

            Vector3 bottlePosition = new Vector3(1.55f, 1.72f, 3.34f);
            RuntimePrimitiveFactory.CreateCylinder(
                "Tincture XXX Bottle",
                parent,
                bottlePosition,
                new Vector3(0.34f, 0.34f, 0.34f),
                new Color(0.70f, 0.82f, 0.78f),
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Tincture XXX Bottle Neck",
                parent,
                bottlePosition + (Vector3.up * 0.42f),
                new Vector3(0.16f, 0.12f, 0.16f),
                new Color(0.70f, 0.82f, 0.78f),
                false);

            Vector3 signPosition = new Vector3(1.55f, 1.74f, 3.13f);
            RuntimePrimitiveFactory.CreateBox(
                "Tincture XXX Sign",
                parent,
                signPosition,
                new Vector3(0.74f, 0.38f, 0.035f),
                trimColor,
                false);
            Color ink = new Color(0.16f, 0.08f, 0.04f);
            for (int xIndex = 0; xIndex < 3; xIndex++)
            {
                float x = signPosition.x - 0.22f + (xIndex * 0.22f);
                for (int stroke = 0; stroke < 2; stroke++)
                {
                    GameObject mark = RuntimePrimitiveFactory.CreateBox(
                        $"Tincture XXX Mark {xIndex + 1}-{stroke + 1}",
                        parent,
                        new Vector3(x, signPosition.y, 3.105f),
                        new Vector3(0.055f, 0.29f, 0.025f),
                        ink,
                        false);
                    mark.transform.localRotation = Quaternion.Euler(
                        0f,
                        0f,
                        stroke == 0 ? 38f : -38f);
                }
            }
        }

        private void BuildMinigame(
            GameObject ui,
            IntoxicationHudView intoxicationHud,
            PlayerCameraFollow follow)
        {
            if (!BarMinigameCatalog.TryGet(
                    ActiveActivity,
                    out BarMinigameDefinition definition))
            {
                throw new System.InvalidOperationException(
                    $"No minigame is registered for '{ActiveActivity}'.");
            }

            var context = new BarMinigameFactoryContext(
                ui,
                intoxicationHud,
                Player,
                follow,
                true);
            ActiveMinigame = definition.Create(context);
            CocktailMinigame =
                ActiveMinigame as CocktailMinigameController;
            BeerPongMinigame =
                ActiveMinigame as BeerPongMinigameController;
            SplitTheGMinigame =
                ActiveMinigame as SplitTheGMinigameController;
            TinctureMatchMinigame =
                ActiveMinigame as TinctureMatchMinigameController;

            ActiveMinigame.Completed += HandleMinigameCompleted;
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

        private void BuildActivityStation()
        {
            bool isBeerPong =
                ActiveActivity == BarActivityKind.BeerPong;
            bool isSplitTheG =
                ActiveActivity == BarActivityKind.SplitTheG;
            bool isTinctureMatch =
                ActiveActivity == BarActivityKind.TinctureMatch;
            Vector3 stationPosition;
            Vector3 triggerSize;
            string stationName;
            string pointName;
            string signName;
            Vector3 signPosition;
            if (isBeerPong)
            {
                stationPosition = new Vector3(0f, 0.9f, -1.95f);
                triggerSize = new Vector3(1.8f, 1.8f, 0.9f);
                stationName = "Beer Pong Minigame Station";
                pointName = "Play Point";
                signName = "Beer Pong Point Sign";
                signPosition = new Vector3(
                    stationPosition.x,
                    1.38f,
                    stationPosition.z + 0.35f);
            }
            else if (isSplitTheG)
            {
                stationPosition = new Vector3(3.85f, 0.9f, 3.35f);
                triggerSize = new Vector3(1.2f, 1.8f, 1.2f);
                stationName = "Split the G Minigame Station";
                pointName = "Split the G Point";
                signName = "Split the G Point Sign";
                signPosition = new Vector3(3.85f, 1.75f, 3.72f);
            }
            else if (isTinctureMatch)
            {
                stationPosition = new Vector3(0f, 0.9f, 3.35f);
                triggerSize = new Vector3(1.4f, 1.8f, 1.2f);
                stationName = "Tincture Match Minigame Station";
                pointName = "Tincture Match Point";
                signName = "Tincture Match Point Sign";
                signPosition = new Vector3(0f, 1.75f, 3.72f);
            }
            else
            {
                stationPosition = new Vector3(-3.85f, 0.9f, 3.35f);
                triggerSize = new Vector3(1.2f, 1.8f, 1.2f);
                stationName = "Cocktail Minigame Station";
                pointName = "Order Point";
                signName = "Order Point Sign";
                signPosition = new Vector3(-3.85f, 1.75f, 3.72f);
            }

            string promptKey = BarMinigameCatalog.TryGet(
                ActiveActivity,
                out BarMinigameDefinition definition)
                ? definition.PromptKey
                : BarActivityStation.DefaultPromptKey;

            GameObject station = new GameObject(stationName);
            station.transform.SetParent(transform, false);
            station.transform.localPosition = stationPosition;
            BoxCollider trigger = station.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = triggerSize;
            ActivityStation =
                station.AddComponent<BarActivityStation>();
            ActivityStation.Configure(ActiveMinigame, promptKey);

            Color markerColor = new Color(0.96f, 0.67f, 0.18f);
            RuntimePrimitiveFactory.CreateBox(
                pointName,
                transform,
                new Vector3(
                    stationPosition.x,
                    0.07f,
                    stationPosition.z),
                new Vector3(0.72f, 0.10f, 0.72f),
                markerColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                signName,
                transform,
                signPosition,
                new Vector3(0.82f, 0.48f, 0.10f),
                markerColor,
                false);
        }

        private void HandleMinigameCompleted()
        {
            GameSessionState.MarkBarVisited(activeBarId);
        }

        private static BarActivityKind ResolveActivity(
            BarActivityKind activity)
        {
            return BarMinigameCatalog.NormalizeActivity(activity);
        }
    }
}
