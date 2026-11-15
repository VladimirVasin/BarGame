using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Vehicle contacts on the same production rig as the other default NPCs.</summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteFerrymanRigAnchors : MonoBehaviour
    {
        public const string DriverBank = "City/LastRouteFerrymanActions";
        public const string PassengerBank = "Player/LastRoutePassengerActions";
        public const string VoiceId = NpcVoiceCatalog.FerrymanVoiceId;
        private static readonly string[] DriverClips =
            { "FerrymanWait", "FerrymanDismount", "FerrymanTrudge", "FerrymanBoard", "FerrymanDrive" };
        private static AnimationClip[] clips;
        private static ContactManifest contactManifest;

        public Animator Animator { get; private set; }
        public Transform HeadAnchor { get; private set; }
        public Transform Pelvis { get; private set; }
        public Transform Spine { get; private set; }
        public Transform Chest { get; private set; }
        public Transform LeftFoot { get; private set; }
        public Transform RightFoot { get; private set; }
        public Transform CoinRestAnchor { get; private set; }
        public Transform LeftUpperArm { get; private set; }
        public Transform LeftForearm { get; private set; }
        public Transform LeftHand { get; private set; }
        public Transform LeftGripSocket { get; private set; }
        public Transform RightUpperArm { get; private set; }
        public Transform RightForearm { get; private set; }
        public Transform RightHand { get; private set; }
        public Transform RightGripSocket { get; private set; }
        public NpcHandPose HandPose { get; private set; }
        public AnimationClip WaitClip => clips[0];
        public AnimationClip DismountClip => clips[1];
        public AnimationClip WalkClip => clips[2];
        public AnimationClip BoardClip => clips[3];
        public AnimationClip DriveClip => clips[4];
        public float PerchHipRise => contactManifest.contacts.perch_hip_rise_m;
        public float PerchHipReach => contactManifest.contacts.perch_hip_reach_m;

        internal void Bind(VillageResidentPresentation actor)
        {
            LoadDriverBank();
            Animator = actor.Animator;
            HeadAnchor = actor.Head;
            HandPose = actor.GetComponent<NpcHandPose>();
            if (HandPose == null) throw new InvalidOperationException("Ferryman needs the default NPC's hand contacts.");
            var rig = new Dictionary<string, Transform>();
            foreach (Transform bone in actor.ModelRoot.GetComponentsInChildren<Transform>(true))
                rig.TryAdd(bone.name, bone);
            Transform Bone(string name) => rig.TryGetValue(name, out var bone) ? bone :
                throw new InvalidOperationException($"Ferryman's default rig is missing {name}.");
            Pelvis = Bone("pelvis");
            Spine = Bone("spine");
            Chest = Bone("chest");
            LeftFoot = Bone("foot.L");
            RightFoot = Bone("foot.R");
            LeftUpperArm = Bone("upper_arm.L");
            LeftForearm = Bone("forearm.L");
            LeftHand = Bone("hand.L");
            LeftGripSocket = actor.LeftGrip;
            RightUpperArm = Bone("upper_arm.R");
            RightForearm = Bone("forearm.R");
            RightHand = Bone("hand.R");
            RightGripSocket = actor.RightGrip;

            AnatomicalHandFrame palm = HandPose.GetAnatomicalFrame(true);
            var coin = new GameObject("Coin on Left Palm").transform;
            coin.position = LeftHand.position + LeftHand.rotation * (palm.PalmCentreInHand + .005f * palm.PalmNormalInHand);
            coin.rotation = LeftHand.rotation * Quaternion.LookRotation(palm.FingerDirectionInHand, palm.PalmNormalInHand);
            coin.SetParent(LeftHand, true);
            CoinRestAnchor = coin;
        }

        internal static void LoadDriverBank()
        {
            if (clips != null) return;
            AnimationClip[] source = Resources.LoadAll<AnimationClip>(DriverBank);
            var loaded = new AnimationClip[DriverClips.Length];
            for (int i = 0; i < DriverClips.Length; i++)
            {
                loaded[i] = Array.Find(source, candidate => candidate.name == DriverClips[i]);
                if (loaded[i] == null)
                    throw new InvalidOperationException($"Missing Last Route action {DriverClips[i]}.");
            }
            TextAsset manifest = Resources.Load<TextAsset>(DriverBank);
            ContactManifest read = manifest != null ? JsonUtility.FromJson<ContactManifest>(manifest.text) : null;
            if (read?.contacts == null || read.contacts.perch_hip_rise_m <= 0f || read.contacts.perch_hip_reach_m <= 0f)
                throw new InvalidOperationException("Missing Last Route perch contacts.");
            contactManifest = read;
            clips = loaded;
        }

        [Serializable]
        private sealed class ContactManifest { public Contacts contacts; }

        [Serializable]
        private sealed class Contacts
        {
            public float perch_hip_rise_m;
            public float perch_hip_reach_m;
        }
    }
}
