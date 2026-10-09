using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CombatBodyDamageRulesTests
    {
        [Test]
        public void TissueLossAccumulatesOnlyAtContactAndUniquePelletsCountOnce()
        {
            var body = new CombatBodyDamageState();
            for (int pellet = 0; pellet < 12; pellet++)
            {
                Assert.That(body.Apply(BodyDamageRegion.LeftForearm, 2, .1f, 1, 1, pellet), Is.True);
                Assert.That(body.Apply(BodyDamageRegion.LeftForearm, 2, .1f, 1, 1, pellet), Is.False,
                    "The same pellet in a later contact batch must not deepen its wound.");
            }
            Assert.That(body.TissueLoss(BodyDamageRegion.LeftForearm, 2), Is.EqualTo(1f).Within(.0001f));
            Assert.That(body.IsAttached(BodyDamageRegion.LeftForearm), Is.True,
                "Exposed bone can retain a structural connection.");
            Assert.That(body.IsFunctional(BodyDamageRegion.LeftForearm), Is.False,
                "Tissue loss must disable the limb before exposed bone can act like healthy tissue.");
            Assert.That(body.IsTerminal, Is.False);
            for (int region = 0; region < CombatBodyDamageState.RegionCount; region++)
                for (int patch = 0; patch < CombatBodyDamageState.PatchCount; patch++)
                    if (region != (int)BodyDamageRegion.LeftForearm || patch != 2)
                        Assert.That(body.TissueLoss((BodyDamageRegion)region, patch), Is.Zero,
                            "Missed anatomical regions and patches cannot borrow a shell's power.");
            Assert.That(body.Apply(BodyDamageRegion.LeftForearm, 2, .1f, 1, 1, 12), Is.True);
            Assert.That(body.IsAttached(BodyDamageRegion.LeftForearm), Is.False);
            Assert.That(body.IsAttached(BodyDamageRegion.LeftHand), Is.False);
            Assert.That(body.IsAttached(BodyDamageRegion.LeftUpperArm), Is.True);
            Assert.That(body.IsAttached(BodyDamageRegion.RightForearm), Is.True);
            Assert.That(body.IsTerminal, Is.False);
        }

        [TestCase(BodyDamageRegion.LeftUpperArm, false, true, true)]
        [TestCase(BodyDamageRegion.RightForearm, true, false, true)]
        [TestCase(BodyDamageRegion.LeftFoot, true, true, false)]
        [TestCase(BodyDamageRegion.RightThigh, true, true, false)]
        public void LimbLossPreservesLifeAndOnlyRemovesDependentCapabilities(
            BodyDamageRegion region, bool leftHand, bool rightHand, bool canStand)
        {
            var body = new CombatBodyDamageState();
            Assert.That(body.Apply(region, 0, 2f, 1, 1, 0), Is.True);
            Assert.That(body.IsAttached(region), Is.False);
            Assert.That(body.IsTerminal, Is.False, "A lost limb must not decide the round.");
            Assert.That(body.CanUseLeftHand, Is.EqualTo(leftHand));
            Assert.That(body.CanUseRightHand, Is.EqualTo(rightHand));
            Assert.That(body.CanStand, Is.EqualTo(canStand));
            Assert.That(body.CanRise, Is.EqualTo(canStand));
            Assert.That(body.CanCrawl, Is.True);
        }

        [TestCase(BodyDamageRegion.Head)]
        [TestCase(BodyDamageRegion.Neck)]
        [TestCase(BodyDamageRegion.Chest)]
        [TestCase(BodyDamageRegion.Abdomen)]
        [TestCase(BodyDamageRegion.Pelvis)]
        public void CriticalStructuralLossIsTerminalAndResetRestoresEverything(BodyDamageRegion region)
        {
            var body = new CombatBodyDamageState();
            Assert.That(body.Apply(region, 1, 2f, 1, 1, 0), Is.True);
            Assert.That(body.IsTerminal, Is.True);
            if (CombatBodyDamageState.IsTorso(region))
                for (int i = 0; i < CombatBodyDamageState.RegionCount; i++)
                    Assert.That(body.IsAttached((BodyDamageRegion)i), Is.True,
                        "Critical torso failure ends life while preserving the core and its connected anatomy.");
            Assert.That(body.CanCrawl, Is.False);
            body.Reset();
            Assert.That(body.IsTerminal, Is.False);
            Assert.That(body.CanUseRightHand && body.CanUseLeftHand && body.CanStand && body.CanRise, Is.True);
            for (int i = 0; i < CombatBodyDamageState.RegionCount; i++)
            {
                Assert.That(body.IsAttached((BodyDamageRegion)i), Is.True);
                Assert.That(body.IsFunctional((BodyDamageRegion)i), Is.True);
                for (int patch = 0; patch < CombatBodyDamageState.PatchCount; patch++)
                {
                    Assert.That(body.TissueLoss((BodyDamageRegion)i, patch), Is.Zero);
                    Assert.That(body.BoneContinuity((BodyDamageRegion)i, patch), Is.EqualTo(1f));
                }
            }
            Assert.That(body.Apply(region, 1, .1f, 1, 1, 0), Is.True,
                "Reset also forgets the previous round's contact identity.");
        }

        [Test]
        public void NewContactsRemainIdentifiableAfterTorsoTraumaSaturates()
        {
            var body = new CombatBodyDamageState();
            Assert.That(body.Apply(BodyDamageRegion.Chest, 0, 2f, 1, 1, 0), Is.True);
            uint revision = body.ContactRevision;
            Assert.That(body.Apply(BodyDamageRegion.Chest, 0, .2f, 1, 1, 0), Is.False);
            Assert.That(body.ContactRevision, Is.EqualTo(revision));
            Assert.That(body.Apply(BodyDamageRegion.Chest, 0, .2f, 1, 2, 0), Is.False);
            Assert.That(body.ContactRevision, Is.EqualTo(revision + 1),
                "A fresh wound in the same coarse patch must still deepen its own local tissue field.");
            body.Reset(); Assert.That(body.ContactRevision, Is.Zero);
        }

        [Test]
        public void LimbHealthBudgetIncludesAllSegmentsAndRestoresOnlyOnReset()
        {
            var body = new CombatBodyDamageState();
            Assert.That(body.ResolveHealthDamage(BodyDamageRegion.LeftUpperArm, 24f), Is.EqualTo(24f));
            Assert.That(body.ResolveHealthDamage(BodyDamageRegion.LeftForearm, 24f), Is.EqualTo(11f));
            Assert.That(body.ResolveHealthDamage(BodyDamageRegion.LeftHand, 24f), Is.Zero);
            Assert.That(body.ResolveHealthDamage(BodyDamageRegion.RightHand, 24f), Is.EqualTo(24f));
            Assert.That(body.ResolveHealthDamage(BodyDamageRegion.LeftThigh, 24f), Is.EqualTo(24f));
            Assert.That(body.ResolveHealthDamage(BodyDamageRegion.LeftShin, 24f), Is.EqualTo(21f));
            Assert.That(body.ResolveHealthDamage(BodyDamageRegion.LeftFoot, 24f), Is.Zero);
            Assert.That(body.ResolveHealthDamage(BodyDamageRegion.Chest, 240f), Is.EqualTo(240f));
            body.Reset();
            Assert.That(body.ResolveHealthDamage(BodyDamageRegion.LeftHand, 24f), Is.EqualTo(24f));
        }

        [Test]
        public void RetiredContactsCannotReappearAfterBoundedHistoryEvictsTheirShot()
        {
            var body = new CombatBodyDamageState();
            for (int sequence = 1; sequence <= 66; sequence++)
                Assert.That(body.Apply(BodyDamageRegion.RightForearm, 0, .001f, 1, sequence, 0), Is.True);
            float tissue = body.TissueLoss(BodyDamageRegion.RightForearm, 0);
            Assert.That(body.Apply(BodyDamageRegion.RightForearm, 0, .2f, 1, 1, 0), Is.False);
            Assert.That(body.TissueLoss(BodyDamageRegion.RightForearm, 0), Is.EqualTo(tissue));
            Assert.That(body.Apply(BodyDamageRegion.RightForearm, 0, .1f, 2, 1, 0), Is.True,
                "Another actor owns a separate projectile sequence.");
        }

        [Test]
        public void SeparatedAnatomyKeepsLocalDamageWithoutRestoringItsCapabilities()
        {
            var body = new CombatBodyDamageState();
            Assert.That(body.Apply(BodyDamageRegion.LeftForearm, 0, 2f, 1, 1, 0), Is.True);
            Assert.That(body.Apply(BodyDamageRegion.LeftForearm, 1, .5f, 1, 2, 0), Is.True,
                "Released surfaces remain finite damage targets after separation.");
            Assert.That(body.TissueLoss(BodyDamageRegion.LeftForearm, 1), Is.EqualTo(.5f));
            Assert.That(body.IsAttached(BodyDamageRegion.LeftForearm), Is.False);
            Assert.That(body.CanUseLeftHand, Is.False);
            Assert.That(body.IsTerminal, Is.False);
        }
    }
}
