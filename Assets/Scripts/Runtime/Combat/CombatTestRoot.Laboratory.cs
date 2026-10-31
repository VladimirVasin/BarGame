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

        private void DrawLaboratoryControls(RetroUiCanvas canvas, CombatHudLayout layout)
        {
            Rect panel = layout.Laboratory;
            RetroUiTheme.DrawPanel(panel, RetroUiTheme.PanelInset,
                RetroUiTheme.BorderMuted, false, 0f, 1f, .82f);
            GUI.Label(CombatHudLayout.Row(panel, 6f, 3f, 82f), LocalizationService.Get("combat.mode.laboratory"), small);
            if (DrawToolbarButton(canvas, CombatHudLayout.Row(panel, 90f, 3f, 208f), "laboratory-enemy", LaboratoryEnemyCaption()))
                laboratoryChoice = LaboratoryEnemyKinds[((int)laboratoryChoice + 1) % LaboratoryEnemyKinds.Length];
            if (DrawToolbarButton(canvas, CombatHudLayout.Row(panel, 6f, 25f, 114f), "laboratory-create",
                LocalizationService.Get("combat.laboratory.create"))) SpawnLaboratoryEnemy(laboratoryChoice);
            if (DrawToolbarButton(canvas, CombatHudLayout.Row(panel, 124f, 25f, 64f), "laboratory-reset",
                LocalizationService.Get("combat.reset"))) ResetRound();
            DrawHudHelp(layout.LaboratoryHelp, LocalizationService.Get("combat.laboratory.help"),
                ref measuredLaboratoryHelp, ref laboratoryHelpWidth);
        }
    }
}
