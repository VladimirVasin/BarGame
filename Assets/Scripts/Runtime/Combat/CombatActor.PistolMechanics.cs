using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal const float PistolSlideTravel = .038f;
        internal Transform PistolEjectionPort { get; private set; }
        internal Transform PistolSlidePull { get; private set; }
        internal Transform PistolMagazineSeat { get; private set; }
        internal float PistolSlideBack => Pistol?.SlideBack ?? 0f;
        internal bool PistolMagazineSeated => Pistol != null && Pistol.MagazineAttached;
        internal bool PistolMagazineInHand => Pistol != null && Pistol.ReloadPending &&
            (PistolReloadSeconds >= .25f && PistolReloadSeconds < .75f ||
             PistolReloadSeconds >= .9f && PistolReloadSeconds < 1.3f);
        internal Transform PistolMagazineTransform => PistolMagazineInHand ? pistolHandMagazine?.transform : pistolMountedMagazine;
        private float PistolReloadSeconds => Mathf.Min(1.8f, Pistol.ReloadProgress * 1.8f + .000001f);
        private float pistolReloadSettleRemaining;
        private Transform pistolSlide, pistolMountedMagazine;
        private Vector3 pistolSlideRestPosition;
        private GameObject pistolHandMagazine;

        private void LateUpdate()
        {
            // ReleaseWeapon opens both hands for the shared crowbar path. A
            // suspended exchange still has a real magazine in the other palm,
            // including while that hand belongs to the visible ragdoll.
            if (PistolMagazineInHand) handPose.SetGrip(true, .8f);
        }

        private void InitializePistolMechanics()
        {
            pistolSlide = CombatPistolAssetProvider.FindAnchor(Weapon, "Slide");
            pistolSlideRestPosition = Weapon.transform.InverseTransformPoint(pistolSlide.position);
            pistolMountedMagazine = CombatPistolAssetProvider.FindAnchor(Weapon, "Magazine");
            PistolMagazineSeat = CombatPistolAssetProvider.FindAnchor(Weapon, "MagazineSeat");
            PistolEjectionPort = CombatPistolAssetProvider.FindAnchor(Weapon, "EjectionPort");
            PistolSlidePull = CombatPistolAssetProvider.FindAnchor(Weapon, "SlidePull");
            // Both copies share the authored asset/materials. The external copy
            // is reused at the belt, never allocated once per reload.
            pistolHandMagazine = CombatPistolAssetProvider.CreateMagazine(Weapon.transform);
            hero.RegisterAccessoryRenderers(pistolHandMagazine.GetComponentsInChildren<Renderer>(true));
            UpdatePistolMechanics();
        }

        private void UpdatePistolMechanics()
        {
            if (IsShotgun) { UpdateShotgunMechanics(); return; }
            if (Pistol == null || pistolSlide == null || Weapon == null) return;
            // The FBX root retains its unit factor. Move through the metric
            // wrapper instead of treating an imported localPosition as metres.
            pistolSlide.position = Weapon.transform.TransformPoint(pistolSlideRestPosition - Vector3.forward * (PistolSlideTravel * Pistol.SlideBack));
            pistolMountedMagazine.gameObject.SetActive(Pistol.MagazineAttached);
            bool inHand = PistolMagazineInHand;
            if (inHand)
                CombatPistolAssetProvider.PlaceMagazineInHand(pistolHandMagazine, hero.Registry.Anchors.LeftGrip, handPose);
            pistolHandMagazine.SetActive(inHand);
        }

        private float PistolReloadGripWeight
        {
            get
            {
                float seconds = PistolReloadSeconds;
                if (seconds < .25f) return Mathf.Lerp(CombatPistolAssetProvider.SupportGripWeight, .8f, seconds / .25f);
                if (seconds < .75f || seconds >= .9f && seconds < 1.3f) return .8f;
                if (seconds >= 1.42f && seconds < 1.58f) return .6f;
                if (seconds >= 1.58f) return Mathf.Lerp(0f, CombatPistolAssetProvider.SupportGripWeight,
                    Mathf.SmoothStep(0f, 1f, (seconds - 1.58f) / .22f));
                return 0f;
            }
        }
    }
}
