using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class BarNpcFactory
    {
        public const string RuntimeRootName = "Bar NPC Runtime";

        public static BarNpcDirector Create(
            Transform parent,
            Camera camera,
            BarNpcPlan plan,
            BarNpcSpriteLibrary spriteLibrary)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (spriteLibrary == null)
            {
                throw new ArgumentNullException(
                    nameof(spriteLibrary));
            }

            GameObject runtimeRoot =
                new GameObject(RuntimeRootName);
            runtimeRoot.transform.SetParent(parent, false);
            var actors = new List<BarNpcActor>(plan.Count);
            try
            {
                for (int index = 0;
                     index < plan.Definitions.Count;
                     index++)
                {
                    BarNpcDefinition definition =
                        plan.Definitions[index];
                    GameObject actorObject = new GameObject(
                        $"NPC {definition.Id}");
                    actorObject.transform.SetParent(
                        runtimeRoot.transform,
                        false);
                    BarNpcActor actor =
                        actorObject.AddComponent<BarNpcActor>();
                    actor.Initialize(
                        definition,
                        camera,
                        spriteLibrary);
                    actors.Add(actor);
                }

                BarNpcDirector director =
                    runtimeRoot.AddComponent<BarNpcDirector>();
                director.Initialize(plan, actors);
                return director;
            }
            catch
            {
                DestroyObject(runtimeRoot);
                throw;
            }
        }

        public static BarNpcDirector CreateWithDefaultLibrary(
            Transform parent,
            Camera camera,
            BarNpcPlan plan)
        {
            return Create(
                parent,
                camera,
                plan,
                BarNpcSpriteLibrary.LoadDefault());
        }

        private static void DestroyObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(gameObject);
            }
            else
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
