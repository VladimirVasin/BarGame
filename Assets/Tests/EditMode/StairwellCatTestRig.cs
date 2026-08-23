using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Builds the minimal fake cat rig the actor tests articulate:
    /// the pivot empties and named MeshRenderer stand-ins of the
    /// authored prefab, mirroring the real instance chain exactly -
    /// actor host, then the factory's half-turned instance root,
    /// then the prefab's half-turned inner Model whose local -Z is
    /// the geometry's forward. No imported assets required.
    /// </summary>
    internal static class StairwellCatTestRig
    {
        public static StairwellCatRigAnchors Create(GameObject host)
        {
            GameObject instanceRoot = new GameObject("Cat Model");
            instanceRoot.transform.SetParent(host.transform, false);
            instanceRoot.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f);
            GameObject model = new GameObject("Model");
            model.transform.SetParent(
                instanceRoot.transform, false);
            model.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f);

            Transform Pivot(string name, Vector3 localPosition)
            {
                var pivot = new GameObject(name);
                pivot.transform.SetParent(model.transform, false);
                pivot.transform.localPosition = localPosition;
                return pivot.transform;
            }

            Renderer Mesh(string name, Vector3 localPosition)
            {
                var mesh = new GameObject(name);
                mesh.transform.SetParent(model.transform, false);
                mesh.transform.localPosition = localPosition;
                mesh.AddComponent<MeshFilter>();
                return mesh.AddComponent<MeshRenderer>();
            }

            Transform chest = Pivot(
                StairwellCatRigAnchors.ChestPivotName,
                new Vector3(0f, 0.12f, -0.02f));
            Transform head = Pivot(
                StairwellCatRigAnchors.HeadPivotName,
                new Vector3(0f, 0.38f, -0.035f));
            Transform earLeft = Pivot(
                StairwellCatRigAnchors.EarLeftPivotName,
                new Vector3(0.05f, 0.515f, -0.045f));
            Transform earRight = Pivot(
                StairwellCatRigAnchors.EarRightPivotName,
                new Vector3(-0.05f, 0.515f, -0.045f));
            Transform[] tail =
            {
                Pivot(
                    StairwellCatRigAnchors.TailPivotNames[0],
                    new Vector3(0.07f, 0.10f, 0.075f)),
                Pivot(
                    StairwellCatRigAnchors.TailPivotNames[1],
                    new Vector3(0.09f, -0.05f, 0.10f)),
                Pivot(
                    StairwellCatRigAnchors.TailPivotNames[2],
                    new Vector3(0.10f, -0.20f, 0.105f))
            };
            Transform muzzle = Pivot(
                StairwellCatRigAnchors.MuzzleAnchorName,
                new Vector3(0f, 0.44f, -0.13f));

            Renderer haunches = Mesh(
                "GEO_Haunches",
                new Vector3(0f, 0.115f, 0.02f));
            Renderer torso = Mesh("GEO_Torso", chest.localPosition);
            Renderer headMesh = Mesh("GEO_Head", head.localPosition);
            Renderer earLeftMesh = Mesh(
                "GEO_Ear.L",
                earLeft.localPosition);
            Renderer earRightMesh = Mesh(
                "GEO_Ear.R",
                earRight.localPosition);
            Renderer tail01 = Mesh(
                "TAIL_Segment.01",
                tail[0].localPosition);
            Renderer tail02 = Mesh(
                "TAIL_Segment.02",
                tail[1].localPosition);
            Renderer tail03 = Mesh(
                "TAIL_Segment.03",
                tail[2].localPosition);
            Renderer grin = Mesh("ACC_Grin", head.localPosition);
            grin.enabled = false;

            StairwellCatRendererBinding[] bindings =
            {
                new StairwellCatRendererBinding(
                    "GEO_Haunches",
                    "",
                    "cat_body",
                    "fur",
                    haunches,
                    Color.black),
                new StairwellCatRendererBinding(
                    "GEO_Torso",
                    StairwellCatRigAnchors.ChestPivotName,
                    "cat_chest",
                    "fur",
                    torso,
                    Color.black),
                new StairwellCatRendererBinding(
                    "GEO_Head",
                    StairwellCatRigAnchors.HeadPivotName,
                    "cat_head",
                    "fur",
                    headMesh,
                    Color.black),
                new StairwellCatRendererBinding(
                    "GEO_Ear.L",
                    StairwellCatRigAnchors.EarLeftPivotName,
                    "cat_ear",
                    "fur_dark",
                    earLeftMesh,
                    Color.black),
                new StairwellCatRendererBinding(
                    "GEO_Ear.R",
                    StairwellCatRigAnchors.EarRightPivotName,
                    "cat_ear",
                    "fur_dark",
                    earRightMesh,
                    Color.black),
                new StairwellCatRendererBinding(
                    "TAIL_Segment.01",
                    StairwellCatRigAnchors.TailPivotNames[0],
                    "cat_tail",
                    "fur_tail",
                    tail01,
                    Color.black),
                new StairwellCatRendererBinding(
                    "TAIL_Segment.02",
                    StairwellCatRigAnchors.TailPivotNames[1],
                    "cat_tail",
                    "fur_tail",
                    tail02,
                    Color.black),
                new StairwellCatRendererBinding(
                    "TAIL_Segment.03",
                    StairwellCatRigAnchors.TailPivotNames[2],
                    "cat_tail",
                    "fur_dark",
                    tail03,
                    Color.black),
                new StairwellCatRendererBinding(
                    StairwellCatRigAnchors.GrinRendererName,
                    StairwellCatRigAnchors.HeadPivotName,
                    "cheshire_grin",
                    "grin_teeth",
                    grin,
                    Color.white)
            };
            Renderer[] renderers =
            {
                haunches,
                torso,
                headMesh,
                earLeftMesh,
                earRightMesh,
                tail01,
                tail02,
                tail03,
                grin
            };

            var anchors =
                model.AddComponent<StairwellCatRigAnchors>();
            anchors.Configure(
                model.transform,
                renderers,
                bindings,
                chest,
                head,
                earLeft,
                earRight,
                tail,
                muzzle,
                grin,
                haunches,
                new Bounds(
                    new Vector3(0f, 0.15f, 0f),
                    new Vector3(0.30f, 0.90f, 0.50f)),
                908,
                "1.0.0",
                StairwellCatProvider.DesignId,
                new string('0', 64));
            return anchors;
        }
    }
}
