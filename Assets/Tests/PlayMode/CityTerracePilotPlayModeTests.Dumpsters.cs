using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_InfillDumpstersKeepPublicApproaches()
        {
            bool focused = GameCursor.ApplicationFocused;
            var input = new InputTestFixture();
            input.Setup();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                GameCursor.SetApplicationFocused(true);
                GameSessionState.TryStartGameTimeAt(12 * 60);
                yield return LoadProductionCity();
                var player = city.Player;
                bool capture = Environment.GetEnvironmentVariable("BARGAME_DUMPSTER_INFILLS") == "1";
                var blocked = new System.Collections.Generic.List<string>();
                var lowerBins = LiveBins(ridge: false);
                var lower = lowerBins.Where(IsInfill).ToArray();
                Assert.That(lower.Length, Is.InRange(5, CityStreetUtilitySites.MaximumInfillDumpsters));
                var lowerIds = lowerBins.Select(utility => utility.Dock.Id).ToArray();
                yield return CheckApproaches(lower, lower[0], lower[lower.Length - 1]);

                var ridgePlan = CityHillsideFurniturePlan.ForLayout(city.Layout);
                var planned = ridgePlan.UtilityDocks.Where(dock => dock.DecorationId.EndsWith("-infill", StringComparison.Ordinal)).ToArray();
                Assert.That(planned, Is.Not.Empty, "The Ridge plan must include roadside infill.");
                var ridgeBins = LiveBins(ridge: true);
                var additions = ridgeBins.Where(IsInfill).ToArray();
                var east = additions.Where(utility => utility.Dock.DecorationId.StartsWith("ridge-East-", StringComparison.Ordinal)).ToArray();
                var west = additions.Where(utility => utility.Dock.DecorationId.StartsWith("ridge-West-", StringComparison.Ordinal)).ToArray();
                Debug.Log($"Dumpster infill: {lower.Length} lower City, {east.Length} east Ridge, {west.Length} west Ridge, {lowerBins.Length + ridgeBins.Length} total.");
                Assert.That(east.Length, Is.InRange(2, CityHillsideFurniturePlan.InfillDumpstersPerBank));
                Assert.That(west.Length, Is.InRange(2, CityHillsideFurniturePlan.InfillDumpstersPerBank));
                Assert.That(lowerIds.Concat(ridgeBins.Select(utility => utility.Dock.Id)).Distinct().Count(),
                    Is.EqualTo(lowerBins.Length + ridgeBins.Length));
                Assert.That(additions.Select(utility => utility.Dock.Id), Is.EquivalentTo(planned.Select(dock => dock.Id)));
                yield return CheckApproaches(additions, east[0], west[0]);

                const string gapPrefix = "ridge-house-gap-";
                var housing = CityHillsideNeighborhoodPlan.ForLayout(city.Layout);
                var gaps = ridgeBins.Where(utility => utility.Dock.DecorationId.StartsWith(gapPrefix, StringComparison.Ordinal)).ToArray();
                var binPlacements = ridgePlan.Placements.Where(item => item.Kind == CityMiscKind.RoadsideDumpsterAndUtility).ToArray();
                var covered = new System.Collections.Generic.HashSet<string>();
                var atFacades = new System.Collections.Generic.HashSet<string>();
                var alongWalls = new System.Collections.Generic.HashSet<string>();
                var streetSetbacks = new[] { 1.32f, 1.49f, 1.79f };
                int movedInward = 0;
                var uncovered = new System.Collections.Generic.List<string>();
                foreach (var (left, right) in CityHillsideFurniturePlan.HouseNeighbours(housing))
                {
                    var atGap = binPlacements.Where(bin => CityHillsideFurniturePlan.IsBetweenHouses(bin, left, right)).ToArray();
                    Assert.That(atGap.Length, Is.LessThanOrEqualTo(1), $"{left.Lot.Cell} / {right.Lot.Cell}");
                    if (atGap.Length == 0)
                    {
                        uncovered.Add($"{left.Lot.Cell}/{right.Lot.Cell} ({right.FoundationBounds.xMin - left.FoundationBounds.xMax:F1}m)");
                        continue;
                    }
                    var bin = atGap[0];
                    covered.Add(bin.Id);
                    float setback = Mathf.Abs(bin.Position.z - bin.ViewTarget.z);
                    if (Mathf.Abs(bin.Forward.x) > .9f)
                    {
                        alongWalls.Add(bin.Id);
                        float clear = Mathf.Max(bin.Bounds.xMin - left.FoundationBounds.xMax,
                            right.FoundationBounds.xMin - bin.Bounds.xMax);
                        Assert.That(clear, Is.GreaterThanOrEqualTo(1.05f), bin.Id);
                    }
                    if (alongWalls.Contains(bin.Id) || !streetSetbacks.Any(value => Mathf.Abs(setback - value) < .003f)) movedInward++;
                    float front = bin.Position.z + bin.Forward.z * .48f;
                    bool atHouseLine = Mathf.Abs(front - CityHillsideFurniturePlan.HouseLineZ(left)) <= .41f ||
                        Mathf.Abs(front - CityHillsideFurniturePlan.HouseLineZ(right)) <= .41f;
                    if (atHouseLine && Mathf.Abs(bin.Position.z - bin.ViewTarget.z) > 2.4f) atFacades.Add(bin.Id);
                }
                Debug.Log($"Ridge house gaps: {covered.Count} covered, {gaps.Length} new. Open gaps: {string.Join(", ", uncovered)}");
                Debug.Log($"Ridge dumpsters at the house line: {atFacades.Count}. {string.Join(", ", atFacades)}");
                Debug.Log($"Ridge dumpsters farther from the sidewalk: {movedInward}, along walls: {alongWalls.Count}. {string.Join(", ", alongWalls)}");
                Assert.That(uncovered.Count, Is.LessThanOrEqualTo(1), string.Join(", ", uncovered));
                Assert.That(gaps.All(utility => covered.Contains(utility.Dock.DecorationId)), Is.True);
                Assert.That(alongWalls, Is.Not.Empty);
                Assert.That(movedInward, Is.GreaterThan(9));
                const string upperWestGap = "ridge-house-gap--22--8--24-bin";
                Assert.That(atFacades, Does.Contain(upperWestGap), "The upper west bin should sit between the fronts of the houses.");
                var eastGap = gaps.First(utility => utility.Dock.StandPosition.x > 0f && atFacades.Contains(utility.Dock.DecorationId));
                var westGap = gaps.Single(utility => utility.Dock.DecorationId == upperWestGap);
                var wallGap = gaps.First(utility => alongWalls.Contains(utility.Dock.DecorationId));
                yield return CheckApproaches(gaps, eastGap, westGap, wallGap);
                Assert.That(blocked, Is.Empty, string.Join(", ", blocked));

                IEnumerator CheckApproaches(CityStreetUtilityInteraction[] bins,
                    params CityStreetUtilityInteraction[] samples)
                {
                    foreach (var utility in bins)
                    {
                        var dock = utility.Dock;
                        player.Motor.Teleport(dock.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                        Physics.SyncTransforms();
                        yield return null;
                        Vector3 stand = dock.StandPosition + Vector3.up * PlayerFactory.GroundedRootOffset;
                        Quaternion facing = Quaternion.LookRotation(dock.Facing);
                        bool reached = false;
                        int limit = 105 + Mathf.CeilToInt(Vector3.Distance(dock.Approach, dock.StandPosition) * 30f);
                        for (int frame = 0; frame < limit && !reached; frame++)
                        {
                            reached = player.Motor.MoveTowardsSupportedInteractionPose(stand, facing, Time.deltaTime);
                            if (player.Motor.InteractionPoseMoveStalled) break;
                            yield return null;
                        }
                        if (!reached || !utility.CanInteract(player.Interactor))
                        {
                            blocked.Add(dock.Id);
                            continue;
                        }
                        if (!samples.Contains(utility)) continue;

                        if (capture)
                        {
                            Transform camera = city.Camera.transform;
                            Transform model = utility.Model.transform;
                            var saved = new Pose(camera.position, camera.rotation);
                            bool nearHouses = dock.DecorationId.StartsWith("ridge-house-gap-", StringComparison.Ordinal);
                            Vector3 inland = Vector3.ProjectOnPlane(model.position - dock.Approach, Vector3.up).normalized;
                            Vector3 eye = nearHouses ? dock.Approach - inland * .9f + Vector3.up * 1.65f
                                : model.TransformPoint(new Vector3(-1.05f, 1.60f, 3.8f));
                            camera.SetPositionAndRotation(eye, Quaternion.LookRotation(model.position + Vector3.up * .72f - eye));
                            try
                            {
                                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "DumpsterInfill", dock.DecorationId, 960, 540);
                            }
                            finally { camera.SetPositionAndRotation(saved.position, saved.rotation); }
                        }

                        keyboard.MakeCurrent();
                        input.Press(keyboard.eKey, queueEventOnly: true);
                        yield return null;
                        input.Release(keyboard.eKey, queueEventOnly: true);
                        yield return null;
                        var action = player.GameObject.GetComponent<CityStreetUtilitySession>();
                        for (int frame = 0; frame < 120 && action && action.Phase != CityStreetUtilitySession.Step.Searching; frame++)
                            yield return null;
                        Assert.That(action && action.Phase == CityStreetUtilitySession.Step.Searching, Is.True, dock.Id);
                        player.GameObject.GetComponent<PlayerAnimatedInteractionController>().CancelActiveInteraction();
                    }
                }

                static CityStreetUtilityInteraction[] LiveBins(bool ridge) => Object.FindObjectsByType<CityStreetUtilityInteraction>()
                    .Where(utility => utility.Dock.Kind == CityStreetUtilityKind.Dumpster &&
                        utility.Dock.DecorationId.StartsWith("ridge-", StringComparison.Ordinal) == ridge)
                    .OrderBy(utility => utility.Dock.Id, StringComparer.Ordinal).ToArray();
                static bool IsInfill(CityStreetUtilityInteraction utility) =>
                    utility.Dock.DecorationId.EndsWith("-infill", StringComparison.Ordinal);
            }
            finally
            {
                input.TearDown();
                GameCursor.SetApplicationFocused(focused);
            }
        }

        [UnityTest]
        public IEnumerator LiveCity_DumpsterSearchKeepsTheFindAndCreditsOneSip()
        {
            bool focused = GameCursor.ApplicationFocused;
            var input = new InputTestFixture();
            input.Setup();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                GameCursor.SetApplicationFocused(true);
                GameSessionState.TryStartGameTimeAt(12 * 60);
                yield return LoadProductionCity();
                var player = city.Player;
                var renderers = ((Player3DCharacterPresentation)player.Visual).Renderers;
                var screen = WorldItemFoundScreen.For(player.Interactor);
                int day = GameSessionState.GameDayIndex;
                var bins = Object.FindObjectsByType<CityStreetUtilityInteraction>()
                    .Where(utility => utility.Dock.Kind == CityStreetUtilityKind.Dumpster)
                    .OrderBy(utility => utility.Dock.Id, StringComparer.Ordinal).ToArray();
                var bin = bins.First(utility => DrinkContainerCatalog.IsLastSip(GameSessionState.DumpsterLoot(utility.Dock.Id, day)));
                var waste = bins.First(utility => !utility.Dock.Id.StartsWith("ridge-", StringComparison.Ordinal) &&
                    GameSessionState.DumpsterLoot(utility.Dock.Id, day) == InventoryItemId.None);
                string id = bin.Dock.Id;
                var item = GameSessionState.DumpsterLoot(id, day);
                bool capture = Environment.GetEnvironmentVariable("BARGAME_DUMPSTER_FRAMES") == "1";
                CityStreetUtilitySession action = null;

                yield return Open(bin);
                yield return Until(() => action.Phase == CityStreetUtilitySession.Step.Searching, "search after opening");
                AssertSearchVisibility();
                yield return Press(keyboard.escapeKey);
                yield return Until(() => action.Phase == CityStreetUtilitySession.Step.Idle, "cancel closes lid");
                Assert.That(player.PresentationVisibility.IsHidden, Is.False);
                Assert.That(renderers.Any(renderer => renderer && renderer.enabled), Is.True);
                Assert.That(GameSessionState.GetDumpsterSearchState(id, day), Is.EqualTo(GameSessionState.DumpsterSearchState.Unsearched));

                var beforeSearch = GameSessionState.CaptureCheckpoint();
                yield return Open(bin);
                yield return Until(() => action.Phase == CityStreetUtilitySession.Step.Searching, "search starts again");
                var start = Time.time;
                yield return Until(() => Time.time - start >= 1.20f, "mid-search");
                AssertSearchVisibility();
                var searchCamera = new Pose(city.Camera.transform.position, city.Camera.transform.rotation);
                Vector3 localLens = bin.Model.transform.InverseTransformPoint(searchCamera.position);
                Assert.That(localLens.z, Is.InRange(.29f, .40f), "lens is just inside the .45 m rim");
                Assert.That(localLens.y, Is.GreaterThan(1.15f), "lens clears the lip");
                Capture("day-paper");
                yield return Until(() => Time.time - start >= 2.93f, "reveal");
                AssertSearchCamera(searchCamera);
                Capture("day-find");
                yield return Until(() => screen.IsShowing, "shared find screen");
                AssertSearchCamera(searchCamera);
                Assert.That(Time.time - start, Is.GreaterThanOrEqualTo(CityStreetUtilitySession.DumpsterSearchSeconds));
                Assert.That(screen.ActiveItemId, Is.EqualTo(item));
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.Zero);
                AssertSearchVisibility();
                Capture("day-inspection");
                yield return Press(keyboard.escapeKey);
                yield return Until(() => action.Phase == CityStreetUtilitySession.Step.Idle, "dismissed find");
                Assert.That(GameSessionState.GetDumpsterSearchState(id, day), Is.EqualTo(GameSessionState.DumpsterSearchState.Found));

                int capacity = InventoryItemCatalog.Get(item).MaximumStack;
                Assert.That(GameSessionState.TryAddInventoryItem(item, capacity), Is.True);
                GameSessionState.AdvanceGameTime(9f * 60f * 4f);
                yield return Open(bin);
                yield return Until(() => action.Phase == CityStreetUtilitySession.Step.Searching, "revisit starts");
                start = Time.time;
                yield return Until(() => Time.time - start >= .74f, "night reveal");
                Capture("night-find");
                yield return Until(() => screen.IsPresenting, "revisit presents");
                Assert.That(Time.time - start, Is.LessThan(1.2f));
                yield return Until(() => screen.IsShowing, "modal ready");
                yield return Press(keyboard.eKey);
                Assert.That(screen.IsShowing, Is.True, "full inventory retains the find");
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(capacity));
                Assert.That(GameSessionState.TryRemoveInventoryItem(item, capacity), Is.True);
                yield return Press(keyboard.eKey);
                yield return Until(() => action.Phase == CityStreetUtilitySession.Step.Idle, "successful take closes lid");
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(1));
                Assert.That(GameSessionState.GetDumpsterSearchState(id, day), Is.EqualTo(GameSessionState.DumpsterSearchState.Depleted));
                Assert.That(GameSessionState.GetDumpsterSearchState(id, day + 1), Is.EqualTo(GameSessionState.DumpsterSearchState.Unsearched));
                Assert.That(bin.Model.OpenAmount, Is.Zero);
                Assert.That(player.PresentationVisibility.IsHidden, Is.False);
                Assert.That(player.Motor.InputEnabled && player.Interactor.InputEnabled, Is.True);

                var fullItem = DrinkContainerCatalog.ModelItem(item);
                var emptyItem = DrinkContainerCatalog.EmptyItem(item);
                Assert.That(GameSessionState.TryAddInventoryItem(fullItem), Is.True);
                Assert.That(GameSessionState.TryAddInventoryItem(emptyItem, 2), Is.True);
                Assert.That(GameSessionState.TryAddInventoryItem(item), Is.True);
                var vessel = player.GameObject.GetComponent<PlayerVodkaBottleController>();
                int previousSips = vessel.CompletedSips;
                for (int remaining = 1; remaining >= 0; remaining--)
                {
                    yield return Press(keyboard.iKey);
                    yield return Until(() => city.Inventory.IsOpen, "inventory opens with the find");
                    int findIndex = GameSessionState.InventoryItems.ToList().FindIndex(stack => stack.ItemId == item);
                    city.Inventory.SelectItem(findIndex);
                    Assert.That(city.Inventory.SelectedStack.ItemId, Is.EqualTo(item));
                    Assert.That(city.Inventory.SelectedStack.Count, Is.EqualTo(remaining + 1));
                    Assert.That(city.Inventory.SelectedUseActionLabel, Is.EqualTo("ДОПИТЬ"));
                    if (remaining == 1 && !Application.isBatchMode && Environment.GetEnvironmentVariable("BARGAME_LAST_SIP_INVENTORY") == "1")
                    {
#if UNITY_EDITOR
                        var gameView = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
                        var window = UnityEditor.EditorWindow.GetWindow(gameView);
                        window.Show();
                        window.Focus();
#endif
                        yield return null;
                        var folder = System.IO.Path.Combine("Captures", "LastSipInventory");
                        System.IO.Directory.CreateDirectory(folder);
                        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, "last-sip.png"));
                        ScreenCapture.CaptureScreenshot(path);
                        yield return Until(() => System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 1000,
                            "inventory rendered in the Game view");
                    }
                    yield return Press(keyboard.uKey);
                    Assert.That(city.Inventory.IsOpen, Is.False);
                    Assert.That(GameSessionState.DrinkOpened(item), Is.True);
                    Assert.That(GameSessionState.DrinkRemainingMl(item), Is.EqualTo(50));
                    yield return Until(() => GameSessionState.GetInventoryItemCount(item) == remaining, "last swallow");
                    yield return Until(() => !vessel.OccupiesHand, "empty vessel stowed");
                    Assert.That(GameSessionState.GetInventoryItemCount(emptyItem), Is.EqualTo(4 - remaining));
                    Assert.That(GameSessionState.InventoryItems.Count(stack => stack.ItemId == emptyItem), Is.EqualTo(1));
                }
                Assert.That(vessel.CompletedSips - previousSips, Is.EqualTo(2));
                Assert.That(GameSessionState.GetInventoryItemCount(fullItem), Is.EqualTo(1));

                GameSessionState.RestoreCheckpoint(beforeSearch);
                Assert.That(GameSessionState.DumpsterLoot(id, day), Is.EqualTo(item));
                Assert.That(GameSessionState.GetDumpsterSearchState(id, day), Is.EqualTo(GameSessionState.DumpsterSearchState.Unsearched));
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.Zero);

                yield return Open(waste);
                yield return Until(() => action.Phase == CityStreetUtilitySession.Step.Idle, "empty result closes lid");
                Assert.That(screen.IsPresenting, Is.False);
                Assert.That(GameSessionState.GetDumpsterSearchState(waste.Dock.Id, day), Is.EqualTo(GameSessionState.DumpsterSearchState.Depleted));

                void AssertSearchCamera(Pose search)
                {
                    Transform camera = city.Camera.transform;
                    Assert.That(Quaternion.Angle(search.rotation, camera.rotation), Is.LessThan(.1f));
                    Assert.That(Vector3.Distance(search.position, camera.position), Is.LessThan(.003f));
                }

                void AssertSearchVisibility()
                {
                    Assert.That(player.PresentationVisibility.IsHiddenBy(action), Is.True);
                    Assert.That(player.PresentationVisibility.ShadowsHidden, Is.True);
                    Assert.That(renderers.Any(renderer => renderer && renderer.enabled), Is.False);
                }

                IEnumerator Open(CityStreetUtilityInteraction utility)
                {
                    var dock = utility.Dock;
                    player.Motor.Teleport(dock.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                    Physics.SyncTransforms();
                    for (int frame = 0; frame < 5; frame++) yield return null;
                    // Some Ridge public approaches sit outside the prompt radius. Walk to
                    // the same supported stand the normal interaction ultimately uses.
                    bool reached = false;
                    var stand = dock.StandPosition + Vector3.up * PlayerFactory.GroundedRootOffset;
                    var facing = Quaternion.LookRotation(dock.Facing);
                    int limit = 105 + Mathf.CeilToInt(Vector3.Distance(dock.Approach, dock.StandPosition) * 30f);
                    for (int frame = 0; frame < limit && !reached; frame++)
                    {
                        reached = player.Motor.MoveTowardsSupportedInteractionPose(stand, facing, Time.deltaTime);
                        yield return null;
                    }
                    Assert.That(reached && utility.CanInteract(player.Interactor), Is.True, dock.Id);
                    utility.Interact(player.Interactor);
                    action = player.GameObject.GetComponent<CityStreetUtilitySession>();
                    Assert.That(action.Phase, Is.Not.EqualTo(CityStreetUtilitySession.Step.Idle), dock.Id);
                }

                IEnumerator Press(ButtonControl button)
                {
                    input.Press(button, queueEventOnly: true);
                    yield return null;
                    input.Release(button, queueEventOnly: true);
                    yield return null;
                }

                static IEnumerator Until(Func<bool> ready, string label)
                {
                    for (int frame = 0; frame < 420 && !ready(); frame++) yield return null;
                    Assert.That(ready(), Is.True, label);
                }

                void Capture(string frame)
                {
                    if (capture) AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "DumpsterSearch", frame, 960, 540);
                }
            }
            finally
            {
                input.TearDown();
                GameCursor.SetApplicationFocused(focused);
            }
        }
    }
}
