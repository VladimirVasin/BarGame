using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private static readonly CombatEnemyKind[] LaboratoryEnemyKinds =
        {
            CombatEnemyKind.Zombie, CombatEnemyKind.Docker, CombatEnemyKind.Crane,
            CombatEnemyKind.Room, CombatEnemyKind.Imprint, CombatEnemyKind.LongExposure
        };
        private CombatEnemyKind laboratoryChoice;
        private string laboratoryCaptionSource, laboratoryCaptionName, laboratoryCaption;
        public bool IsLaboratory => Mode == CombatTestMode.Laboratory;
        public CombatEnemyKind LaboratoryEnemy { get; private set; }

        /// <summary>Replace the single laboratory target with its prepared body and reset the round.</summary>
        public bool SpawnLaboratoryEnemy(CombatEnemyKind kind)
        {
            if (!isActiveAndEnabled || !IsInitialized || !IsLaboratory ||
                (uint)kind > (uint)CombatEnemyKind.LongExposure || !GameInput.CanRead(GameInputContext.Gameplay) ||
                (Taunt != null && Taunt.IsActive)) return false;
            LaboratoryEnemy = laboratoryChoice = kind;
            PlaceRound(focusOpponent: !Hero.IsFirearm);
            return true;
        }

        private string LaboratoryEnemyCaption()
        {
            string source = LocalizationService.Get("combat.laboratory.enemy");
            string name = LocalizationService.Get(StartMenuModel.LaboratoryEnemyLabelKey(laboratoryChoice));
            if (laboratoryCaption == null || laboratoryCaptionSource != source || laboratoryCaptionName != name)
            {
                laboratoryCaptionSource = source; laboratoryCaptionName = name;
                laboratoryCaption = string.Format(source, name);
            }
            return laboratoryCaption;
        }

        private void DrawLaboratoryControls(RetroUiCanvas canvas)
        {
            float y = DebugHudVisible ? 38f : 10f;
            RetroUiTheme.DrawPanel(new Rect(14f, y, 612f, 50f), RetroUiTheme.PanelInset,
                RetroUiTheme.BorderMuted, false, 0f, 1f, .82f);
            GUI.Label(new Rect(20f, y + 5f, 100f, 18f), LocalizationService.Get("combat.mode.laboratory"), small);
            if (DrawToolbarButton(canvas, new Rect(124f, y + 3f, 244f, 20f), "laboratory-enemy", LaboratoryEnemyCaption()))
                laboratoryChoice = LaboratoryEnemyKinds[((int)laboratoryChoice + 1) % LaboratoryEnemyKinds.Length];
            if (DrawToolbarButton(canvas, new Rect(374f, y + 3f, 132f, 20f), "laboratory-create",
                LocalizationService.Get("combat.laboratory.create"))) SpawnLaboratoryEnemy(laboratoryChoice);
            if (DrawToolbarButton(canvas, new Rect(512f, y + 3f, 108f, 20f), "laboratory-reset",
                LocalizationService.Get("combat.reset"))) ResetRound();
            GUI.Label(new Rect(20f, y + 29f, 600f, 15f), LocalizationService.Get("combat.laboratory.help"), controls);
        }
    }
}
