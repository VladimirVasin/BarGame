using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct BarMinigameFactoryContext
    {
        public BarMinigameFactoryContext(
            GameObject host,
            IntoxicationHudView intoxicationHud,
            PlayerRuntime player,
            PlayerCameraFollow cameraFollow,
            bool persistSessionProgress)
        {
            Host = host;
            IntoxicationHud = intoxicationHud;
            Player = player;
            CameraFollow = cameraFollow;
            PersistSessionProgress = persistSessionProgress;
        }

        public GameObject Host { get; }
        public IntoxicationHudView IntoxicationHud { get; }
        public PlayerRuntime Player { get; }
        public PlayerCameraFollow CameraFollow { get; }
        public bool PersistSessionProgress { get; }
    }

    public sealed class BarMinigameDefinition
    {
        private readonly Func<BarMinigameFactoryContext, IBarMinigame> factory;

        public BarMinigameDefinition(
            string id,
            BarActivityKind activity,
            string labelKey,
            string promptKey,
            int sortOrder,
            Func<BarMinigameFactoryContext, IBarMinigame> minigameFactory)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A minigame ID is required.",
                    nameof(id));
            }

            if (activity == BarActivityKind.None)
            {
                throw new ArgumentException(
                    "A playable bar activity is required.",
                    nameof(activity));
            }

            if (string.IsNullOrWhiteSpace(labelKey))
            {
                throw new ArgumentException(
                    "A localized debug label key is required.",
                    nameof(labelKey));
            }

            factory = minigameFactory ??
                throw new ArgumentNullException(nameof(minigameFactory));
            Id = id;
            Activity = activity;
            LabelKey = labelKey;
            PromptKey = promptKey ?? string.Empty;
            SortOrder = sortOrder;
        }

        public string Id { get; }
        public BarActivityKind Activity { get; }
        public string LabelKey { get; }
        public string PromptKey { get; }
        public int SortOrder { get; }

        public IBarMinigame Create(BarMinigameFactoryContext context)
        {
            if (context.Host == null)
            {
                throw new ArgumentException(
                    "A live GameObject host is required.",
                    nameof(context));
            }

            return factory(context);
        }
    }

    public static class BarMinigameCatalog
    {
        public const string CocktailId = "cocktail";
        public const string BeerPongId = "beer-pong";
        public const string SplitTheGId = "split-the-g";

        private static readonly List<BarMinigameDefinition> definitions =
            new List<BarMinigameDefinition>();
        private static readonly ReadOnlyCollection<BarMinigameDefinition>
            definitionsView = definitions.AsReadOnly();
        private static bool initialized;

        public static IReadOnlyList<BarMinigameDefinition> Definitions
        {
            get
            {
                EnsureInitialized();
                return definitionsView;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCatalog()
        {
            definitions.Clear();
            initialized = false;
            EnsureInitialized();
        }

        public static bool Register(BarMinigameDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            EnsureInitialized();
            for (int index = 0; index < definitions.Count; index++)
            {
                BarMinigameDefinition existing = definitions[index];
                if (string.Equals(
                        existing.Id,
                        definition.Id,
                        StringComparison.Ordinal) ||
                    existing.Activity == definition.Activity)
                {
                    return false;
                }
            }

            definitions.Add(definition);
            definitions.Sort(CompareDefinitions);
            return true;
        }

        public static bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            EnsureInitialized();
            for (int index = 0; index < definitions.Count; index++)
            {
                if (!string.Equals(
                        definitions[index].Id,
                        id,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                definitions.RemoveAt(index);
                return true;
            }

            return false;
        }

        public static bool TryGet(
            string id,
            out BarMinigameDefinition definition)
        {
            EnsureInitialized();
            for (int index = 0; index < definitions.Count; index++)
            {
                BarMinigameDefinition candidate = definitions[index];
                if (string.Equals(
                        candidate.Id,
                        id,
                        StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public static bool TryGet(
            BarActivityKind activity,
            out BarMinigameDefinition definition)
        {
            EnsureInitialized();
            for (int index = 0; index < definitions.Count; index++)
            {
                BarMinigameDefinition candidate = definitions[index];
                if (candidate.Activity == activity)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public static BarActivityKind NormalizeActivity(
            BarActivityKind activity)
        {
            if (TryGet(activity, out BarMinigameDefinition definition))
            {
                return definition.Activity;
            }

            return BarActivityKind.Cocktail;
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            definitions.Add(
                new BarMinigameDefinition(
                    CocktailId,
                    BarActivityKind.Cocktail,
                    "debug.minigame.cocktail",
                    "interaction.order_drinks",
                    10,
                    CreateCocktail));
            definitions.Add(
                new BarMinigameDefinition(
                    BeerPongId,
                    BarActivityKind.BeerPong,
                    "debug.minigame.beer_pong",
                    "interaction.play_beer_pong",
                    20,
                    CreateBeerPong));
            definitions.Add(
                new BarMinigameDefinition(
                    SplitTheGId,
                    BarActivityKind.SplitTheG,
                    "debug.minigame.split_the_g",
                    "interaction.play_split_the_g",
                    30,
                    CreateSplitTheG));
            definitions.Sort(CompareDefinitions);
        }

        private static IBarMinigame CreateCocktail(
            BarMinigameFactoryContext context)
        {
            CocktailMinigameView view =
                context.Host.AddComponent<CocktailMinigameView>();
            CocktailMinigameController controller =
                context.Host.AddComponent<CocktailMinigameController>();
            controller.Initialize(
                view,
                context.IntoxicationHud,
                context.Player,
                context.CameraFollow,
                context.PersistSessionProgress);
            return controller;
        }

        private static IBarMinigame CreateBeerPong(
            BarMinigameFactoryContext context)
        {
            BeerPongMinigameView view =
                context.Host.AddComponent<BeerPongMinigameView>();
            BeerPongMinigameController controller =
                context.Host.AddComponent<BeerPongMinigameController>();
            controller.Initialize(
                view,
                context.IntoxicationHud,
                context.Player,
                context.CameraFollow,
                context.PersistSessionProgress);
            return controller;
        }

        private static IBarMinigame CreateSplitTheG(
            BarMinigameFactoryContext context)
        {
            SplitTheGMinigameView view =
                context.Host.AddComponent<SplitTheGMinigameView>();
            SplitTheGMinigameController controller =
                context.Host.AddComponent<SplitTheGMinigameController>();
            controller.Initialize(
                view,
                context.IntoxicationHud,
                context.Player,
                context.CameraFollow,
                context.PersistSessionProgress);
            return controller;
        }

        private static int CompareDefinitions(
            BarMinigameDefinition left,
            BarMinigameDefinition right)
        {
            int orderComparison =
                left.SortOrder.CompareTo(right.SortOrder);
            return orderComparison != 0
                ? orderComparison
                : string.CompareOrdinal(left.Id, right.Id);
        }
    }
}
