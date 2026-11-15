using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bus contacts on a permanent actor from the shared NPC catalogue.</summary>
    [DisallowMultipleComponent]
    public sealed class CityBusDriverRig : MonoBehaviour
    {
        public VillageResidentPresentation Actor { get; private set; }
        public Animator Animator => Actor.Animator;
        public Transform ModelRoot => Actor.ModelRoot;
        public Transform Head => Actor.Head;
        public Transform LeftGripSocket => Actor.LeftGrip;
        public Transform RightGripSocket => Actor.RightGrip;
        public DefaultNpcAppearance Appearance { get; private set; }
        public NpcHandPose Hands { get; private set; }
        public IReadOnlyList<Renderer> Renderers { get; private set; }
        public Transform Pelvis { get; private set; }
        public Transform Spine { get; private set; }
        public Transform Chest { get; private set; }
        public Transform Neck { get; private set; }
        public Transform FaceEyeLeft { get; private set; }
        public Transform FaceEyeRight { get; private set; }
        public Transform LeftUpperArm { get; private set; }
        public Transform LeftForearm { get; private set; }
        public Transform LeftHand { get; private set; }
        public Transform RightUpperArm { get; private set; }
        public Transform RightForearm { get; private set; }
        public Transform RightHand { get; private set; }
        public Transform LeftThigh { get; private set; }
        public Transform LeftShin { get; private set; }
        public Transform LeftFoot { get; private set; }
        public Transform RightThigh { get; private set; }
        public Transform RightShin { get; private set; }
        public Transform RightFoot { get; private set; }
        public Transform LeftSole { get; private set; }
        public Transform RightSole { get; private set; }
        public Transform RightIndexTip { get; private set; }
        public Renderer FaceSurface { get; private set; }

        public static CityBusDriverRig Create(Transform parent, int routeNumber = 1, int busSlot = 0)
        {
            var actor = DefaultNpcFactory.CreateForCharacter(parent,
                DefaultNpcPopulation.BusDriverId(routeNumber, busSlot), initializeLocomotion: false);
            try
            {
                actor.name = "Bus Driver";
                var rig = actor.gameObject.AddComponent<CityBusDriverRig>();
                rig.Bind(actor);
                return rig;
            }
            catch
            {
                CityBusResources.DestroyObject(actor.gameObject);
                throw;
            }
        }

        private void Bind(VillageResidentPresentation actor)
        {
            Actor = actor;
            if (actor.GetComponentInChildren<Collider>(true) != null ||
                actor.GetComponentInChildren<Rigidbody>(true) != null ||
                actor.GetComponentInChildren<Light>(true) != null)
                throw new InvalidOperationException("A seated driver must remain a passive NPC.");

            Appearance = GetComponent<DefaultNpcAppearance>();
            Hands = GetComponent<NpcHandPose>();
            if (Appearance == null || Hands == null || Actor.Animator == null || LeftGripSocket == null ||
                RightGripSocket == null || Head == null)
                throw new InvalidOperationException("The default driver needs its shared face, rig and hand contacts.");

            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in ModelRoot.GetComponentsInChildren<Transform>(true))
                bones.TryAdd(bone.name, bone);
            Transform Bone(string name) => bones.TryGetValue(name, out var transform)
                ? transform : throw new InvalidOperationException("The default driver's rig is missing " + name);

            Pelvis = Bone("pelvis"); Spine = Bone("spine"); Chest = Bone("chest"); Neck = Bone("neck");
            FaceEyeLeft = Bone("face.eye.L"); FaceEyeRight = Bone("face.eye.R");
            LeftUpperArm = Bone("upper_arm.L"); LeftForearm = Bone("forearm.L"); LeftHand = Bone("hand.L");
            RightUpperArm = Bone("upper_arm.R"); RightForearm = Bone("forearm.R"); RightHand = Bone("hand.R");
            LeftThigh = Bone("thigh.L"); LeftShin = Bone("shin.L"); LeftFoot = Bone("foot.L");
            RightThigh = Bone("thigh.R"); RightShin = Bone("shin.R"); RightFoot = Bone("foot.R");
            LeftSole = Bone("ANCHOR_WorkSole.L"); RightSole = Bone("ANCHOR_WorkSole.R");
            RightIndexTip = Bone("ANCHOR_IndexOpenTip.R");

            var visible = new List<Renderer>(48);
            foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                visible.Add(renderer);
                if (renderer.name == "GEO_FaceSurface") FaceSurface = renderer;
            }
            if (FaceSurface == null)
                throw new InvalidOperationException("The default driver's painted face surface is missing.");
            Renderers = visible.ToArray();
        }
    }
}
