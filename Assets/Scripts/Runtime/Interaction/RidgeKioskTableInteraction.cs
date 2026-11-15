using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    internal sealed class RidgeKioskTableInteraction : MonoBehaviour, IInteractable
    {
        private RidgeKioskCourtLife court;
        internal int Table { get; private set; }
        public string PromptKey => court.TableRound(Table) < 0 ? "kiosk.dialogue.treat" : "kiosk.dialogue.talk";
        public Vector3 InteractionPosition => transform.position;
        internal static void Install(RidgeKioskCourtLife court, RidgeKioskCourtPlan plan, int table)
        {
            var host = new GameObject("Kiosk table interaction " + table);
            host.transform.SetParent(court.transform, true);
            host.transform.position = plan.ConversationGround(table) + Vector3.up * .85f;
            var trigger = host.AddComponent<SphereCollider>();
            trigger.radius = .16f;
            trigger.isTrigger = true;
            var interaction = host.AddComponent<RidgeKioskTableInteraction>();
            interaction.court = court;
            interaction.Table = table;
        }

        public bool CanInteract(PlayerInteractor player) => court && court.CanInteractAt(Table, player);
        public void Interact(PlayerInteractor player) => court.InteractAt(Table, player);
    }
}
