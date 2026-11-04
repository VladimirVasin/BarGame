using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>One data-only, process-local checkpoint at the latest successful scene arrival.</summary>
    public static class SessionCheckpointService
    {
        private sealed class Entry
        {
            internal string Scene;
            internal HomeArrivalKind Home;
            internal StairwellArrivalKind Stairwell;
            internal AlpineVillageArrivalKind Village;
            internal CityReturnKind CityReturn;
            internal AreaTravelRequest? Area;
            internal ulong NpcVisits;
        }

        private sealed class Checkpoint
        {
            internal Entry Arrival;
            internal GameSessionState.CheckpointSnapshot Progress;
            internal CheckpointWorldState World;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal CheckpointCombatState Combat;
            internal bool HasVitals;
            internal float Health, Stamina;
            internal bool StoryWarehouse;
            internal int WarehouseSeed;
            internal CombatTestSelection CombatSelection;
        }

        private static Checkpoint saved;
        private static Entry pending;
        public static bool HasCheckpoint => saved != null;
        public static bool IsRestoring { get; private set; }
        internal static string CheckpointScene => saved?.Arrival.Scene;
        internal static bool IsRestoringScene(string scene) => IsRestoring && CheckpointScene == scene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Clear()
        {
            saved = null;
            pending = null;
            IsRestoring = false;
        }

        internal static void PrepareDestination(string scene, AreaTravelRequest? request = null)
        {
            if (IsRestoring || !IsGameplay(scene)) return;
            // Bootstrap can observe the same destination after its load was reserved.
            if (pending != null && pending.Scene == scene) return;
            pending = new Entry
            {
                Scene = scene,
                Home = GameSessionState.HomeArrival,
                Stairwell = GameSessionState.StairwellArrival,
                Village = GameSessionState.AlpineVillageArrival,
                CityReturn = GameSessionState.ReturnKind,
                Area = request,
                NpcVisits = DefaultNpcPopulation.CaptureCheckpointVisits()
            };
        }

        internal static void DestinationReady(string scene)
        {
            if (!IsGameplay(scene)) return;
            PlayerMotor motor = FindPlayer(scene);
            if (motor == null) return;
            if (IsRestoringScene(scene))
            {
                ApplyPlayer(motor);
                IsRestoring = false;
                pending = null;
                GameLog.Info("checkpoint", "restored", GameLog.Field("scene", scene));
                return;
            }
            PrepareDestination(scene);
            CombatActor actor = motor.GetComponent<CombatActor>();
            CombatTestRoot combat = motor.GetComponentInParent<CombatTestRoot>();
            saved = new Checkpoint
            {
                Arrival = pending,
                Progress = GameSessionState.CaptureCheckpoint(),
                World = CheckpointWorldState.Capture(),
                Position = motor.transform.position,
                Rotation = motor.transform.rotation,
                Combat = CheckpointCombatState.Capture(actor),
                HasVitals = actor?.State != null && !actor.State.IsDefeated,
                Health = actor?.State.Health ?? 0f,
                Stamina = actor?.State.Stamina ?? 0f,
                StoryWarehouse = combat != null && combat.IsStoryContinuation,
                WarehouseSeed = combat != null ? combat.CheckpointWarehouseSeed : 0,
                CombatSelection = combat != null ? new CombatTestSelection(combat.LoadoutSelection,
                    combat.Mode, combat.EncounterSettings, combat.LaboratoryEnemy) : CombatTestSelection.Default
            };
            pending = null;
            GameLog.Info("checkpoint", "captured", GameLog.Field("scene", scene));
        }

        public static bool RequestRestore()
        {
            if (!HasCheckpoint || IsRestoring || SceneTransitionService.IsTransitioning ||
                !Application.CanStreamedLevelBeLoaded(CheckpointScene) ||
                !Application.CanStreamedLevelBeLoaded(SceneIds.AreaLoading)) return false;
            IsRestoring = true;
            if (SceneTransitionService.RequestCheckpointLoad(CheckpointScene, RestoreAfterUnload)) return true;
            IsRestoring = false;
            return false;
        }

        // Called only after Single loading the neutral staging scene. No outgoing
        // OnDisable/OnDestroy writer can now overwrite the restored progress.
        private static void RestoreAfterUnload()
        {
            saved.World.Restore();
            GameSessionState.RestoreCheckpoint(saved.Progress);
            Entry entry = saved.Arrival;
            GameSessionState.RestoreCheckpointArrival(entry.Home, entry.Stairwell, entry.Village, entry.CityReturn);
            DefaultNpcPopulation.RestoreCheckpointVisits(entry.NpcVisits);
            if (entry.Area.HasValue) AreaTravelService.PrepareCheckpointArrival(entry.Area.Value);
            GameTimeScaleRuntime.SetIntoxicationLevel(GameSessionState.IntoxicationLevel);
        }

        internal static bool TryGetCombatSelection(out CombatTestSelection selection)
        {
            selection = saved != null ? saved.CombatSelection : CombatTestSelection.Default;
            return IsRestoringScene(SceneIds.CombatTest);
        }

        internal static bool TryGetWarehouseSeed(out int seed)
        {
            seed = saved?.WarehouseSeed ?? 0;
            return IsRestoringScene(SceneIds.CombatTest);
        }

        internal static bool TryGetStoryWarehouse(out int seed)
        {
            seed = saved?.WarehouseSeed ?? 0;
            return IsRestoringScene(SceneIds.CombatTest) && saved.StoryWarehouse;
        }

        internal static Vector3 ResolvePlayerSpawn(string scene, Vector3 ordinary)
            => IsRestoringScene(scene) && !HasContextualArrival() ? saved.Position : ordinary;

        private static bool HasContextualArrival()
        {
            AreaArrivalToken? token = saved.Arrival.Area?.ArrivalToken;
            return token == AreaArrivalToken.Ferryman || token == AreaArrivalToken.FerrymanReturn ||
                token == AreaArrivalToken.Cableway || saved.Arrival.Home != HomeArrivalKind.Normal &&
                saved.Arrival.Scene == SceneIds.HomeInterior;
        }

        private static void ApplyPlayer(PlayerMotor motor)
        {
            if (!HasContextualArrival())
            {
                motor.Teleport(saved.Position);
                motor.transform.rotation = saved.Rotation;
                foreach (PlayerCameraFollow follow in Object.FindObjectsByType<PlayerCameraFollow>())
                    if (follow.gameObject.scene == motor.gameObject.scene)
                        follow.Snap();
            }
            CombatActor actor = motor.GetComponent<CombatActor>();
            if (saved.HasVitals && actor != null) actor.State.RestoreVitals(saved.Health, saved.Stamina);
            saved.Combat?.Restore(actor);
        }

        internal static void TransitionFailed()
        {
            pending = null;
            IsRestoring = false; // Keep the last valid snapshot available for another attempt.
        }

        private static PlayerMotor FindPlayer(string scene)
        {
            foreach (PlayerMotor motor in Object.FindObjectsByType<PlayerMotor>())
                if (motor.gameObject.scene.name == scene) return motor;
            return null;
        }

        private static bool IsGameplay(string scene) => scene == SceneIds.City || scene == SceneIds.BarInterior ||
            scene == SceneIds.SupermarketInterior || scene == SceneIds.StairwellInterior || scene == SceneIds.HomeInterior ||
            scene == SceneIds.MountainRoad || scene == SceneIds.ChurchInterior || scene == SceneIds.AlpineVillage ||
            scene == SceneIds.MothersHouseInterior || scene == SceneIds.CombatTest;
    }
}
