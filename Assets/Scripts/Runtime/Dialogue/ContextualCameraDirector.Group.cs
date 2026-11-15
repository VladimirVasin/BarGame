using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    public partial class ContextualCameraDirector
    {
        private Vector3 GroupTableFocus => groupTable.position - axis * .16f + Vector3.up * .24f;

        internal bool BeginGroup(PlayerCameraFollow cameraFollow, Transform table,
            in DialogueParticipant first, in DialogueParticipant second, in DialogueParticipant hero,
            bool startOnTable)
        {
            if (active || activeOwner != null || cameraFollow == null || !cameraFollow.isActiveAndEnabled ||
                cameraFollow.Camera == null || !table || !first.IsPresent || !second.IsPresent || !hero.IsPresent ||
                CinematicDepthOfField.IsActive) return false;

            Vector3 approach = Vector3.ProjectOnPlane(hero.Root.position - table.position, Vector3.up);
            if (approach.sqrMagnitude < .25f) return false;
            follow = cameraFollow;
            camera = follow.Camera;
            npcRoot = first.Root;
            npcHead = first.Head;
            secondNpcRoot = second.Root;
            secondNpcHead = second.Head;
            heroRoot = hero.Root;
            heroHead = hero.Head;
            groupTable = table;
            axis = approach.normalized;
            side = Vector3.Cross(Vector3.up, axis);
            objectShot = false;
            groupSpeaker = startOnTable ? null : DialogueSpeaker.Npc;
            if (!TryResolveGroupShot(DialogueSpeaker.Npc, out _, out _) ||
                !TryResolveGroupShot(DialogueSpeaker.SecondNpc, out _, out _) ||
                !TryResolveGroupShot(DialogueSpeaker.Hero, out _, out _) ||
                startOnTable && !TryResolveGroupShot(null, out _, out _))
            {
                ClearReferences();
                return false;
            }
            return AcquireCamera();
        }

        internal bool FocusGroup(DialogueSpeaker speaker)
        {
            if (!active || returning || !ParticipantsExist() || groupTable == null) return false;
            if (groupSpeaker == speaker) return CurrentShotIsClear;
            if (!TryResolveGroupShot(speaker, out Pose pose, out float fov)) return false;
            groupSpeaker = speaker;
            CurrentSpeakerIsHero = speaker == DialogueSpeaker.Hero;
            CurrentShotIsClear = true;
            entering = false;
            Apply(pose, fov);
            UpdateFocusDistance();
            return true;
        }

        private bool TryResolveGroupShot(DialogueSpeaker? speaker, out Pose pose, out float fov)
        {
            bool onHero = speaker == DialogueSpeaker.Hero;
            Transform root = onHero ? heroRoot : speaker == DialogueSpeaker.SecondNpc ? secondNpcRoot : npcRoot;
            Transform head = onHero ? heroHead : speaker == DialogueSpeaker.SecondNpc ? secondNpcHead : npcHead;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int width = attempt / 2;
                float sign = (attempt & 1) == 0 ? 1f : -1f;
                Vector3 position, aim, focus;
                if (!speaker.HasValue)
                {
                    // Look across both men from the side opposite the hero so neither throw is hidden by him.
                    position = groupTable.position - axis * (2.96f + .18f * width) -
                        side * sign * (.32f + .16f * width) + Vector3.up * 1.12f;
                    focus = GroupTableFocus;
                    aim = groupTable.position + Vector3.up * .17f;
                    fov = 62f;
                }
                else
                {
                    focus = head.position;
                    aim = focus - Vector3.up * .15f;
                    Vector3 offset = onHero
                        ? -axis * (1.65f + .13f * width) + side * sign * (.63f + .16f * width)
                        : Vector3.ProjectOnPlane(groupTable.position - root.position, Vector3.up).normalized *
                            (1.70f + .19f * width) + axis * sign * (.91f + .15f * width);
                    position = root.position + offset;
                    position.y = focus.y + .03f;
                    fov = Mathf.Clamp(2f * Mathf.Atan(.64f / Vector3.Distance(position, aim)) *
                        Mathf.Rad2Deg, 32f, 47f);
                }
                Transform subject = speaker.HasValue ? root : null;
                if (!CameraPositionClear(position) || !SightClear(focus, position, subject) ||
                    speaker.HasValue && !SightClear(focus - Vector3.up * .24f, position, subject)) continue;
                pose = new Pose(position, Quaternion.LookRotation(aim - position, Vector3.up));
                return true;
            }
            pose = default;
            fov = 0f;
            return false;
        }
    }
}
