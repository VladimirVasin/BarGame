using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Builds the minimal fake raven rig the actor tests articulate:
    /// the pivot empties and named MeshRenderer stand-ins of the
    /// authored prefab, mirroring the real instance chain exactly -
    /// actor host, then the factory's half-turned "Raven Model"
    /// instance root, then the prefab's half-turned inner Model whose
    /// local -Z is the geometry's forward. No imported assets
    /// required - the stairwell cat rig's pattern on a bird, with the
    /// part list taken verbatim from the generator manifest so a
    /// renamed pivot fails here before it fails on the real FBX.
    /// </summary>
    internal static class CemeteryRavenTestRig
    {
        public static CemeteryRavenRigAnchors Create(GameObject host)
        {
            GameObject instanceRoot = new GameObject(
                CemeteryRavenFactory.ModelInstanceName);
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

            //  Feet on the origin (the "origin = contact point" rule),
            //  a standing height around 0.24 m, wings on the flanks,
            //  the tail pointing local +Z because the geometry faces
            //  local -Z.
            Transform bodyRoot = Pivot(
                CemeteryRavenRigAnchors.BodyRootPivotName,
                new Vector3(0f, 0.075f, 0.01f));
            Transform head = Pivot(
                CemeteryRavenRigAnchors.HeadPivotName,
                new Vector3(0f, 0.185f, -0.085f));
            Transform wingLeft = Pivot(
                CemeteryRavenRigAnchors.WingLeftPivotName,
                new Vector3(0.042f, 0.145f, 0.015f));
            Transform wingRight = Pivot(
                CemeteryRavenRigAnchors.WingRightPivotName,
                new Vector3(-0.042f, 0.145f, 0.015f));
            Transform tail = Pivot(
                CemeteryRavenRigAnchors.TailPivotName,
                new Vector3(0f, 0.14f, 0.12f));
            Transform feetContact = Pivot(
                CemeteryRavenRigAnchors.FeetContactAnchorName,
                Vector3.zero);

            Color plumage = new Color(0.10f, 0.10f, 0.115f, 1f);
            Color beakGrey = new Color(0.34f, 0.33f, 0.31f, 1f);
            Color legGrey = new Color(0.30f, 0.30f, 0.30f, 1f);
            Color eyePale = new Color(0.66f, 0.64f, 0.58f, 1f);

            Renderer body = Mesh("GEO_Body", bodyRoot.localPosition);
            Renderer headMesh = Mesh("GEO_Head", head.localPosition);
            Renderer beak = Mesh(
                "GEO_Beak",
                head.localPosition + new Vector3(0f, 0f, -0.05f));
            Renderer eyeLeft = Mesh(
                "GEO_Eye.L",
                head.localPosition + new Vector3(0.02f, 0.01f, -0.03f));
            Renderer eyeRight = Mesh(
                "GEO_Eye.R",
                head.localPosition + new Vector3(-0.02f, 0.01f, -0.03f));
            Renderer wingLeftMesh = Mesh(
                "GEO_Wing.L",
                wingLeft.localPosition);
            Renderer wingRightMesh = Mesh(
                "GEO_Wing.R",
                wingRight.localPosition);
            Renderer tailMesh = Mesh("GEO_Tail", tail.localPosition);
            Renderer legLeft = Mesh(
                "GEO_Leg.L",
                new Vector3(0.02f, 0.03f, 0.01f));
            Renderer legRight = Mesh(
                "GEO_Leg.R",
                new Vector3(-0.02f, 0.03f, 0.01f));

            CemeteryRavenRendererBinding[] bindings =
            {
                new CemeteryRavenRendererBinding(
                    "GEO_Body",
                    CemeteryRavenRigAnchors.BodyRootPivotName,
                    "raven_body",
                    "body_black",
                    body,
                    plumage,
                    true),
                new CemeteryRavenRendererBinding(
                    "GEO_Head",
                    CemeteryRavenRigAnchors.HeadPivotName,
                    "raven_head",
                    "head_black",
                    headMesh,
                    plumage,
                    true),
                new CemeteryRavenRendererBinding(
                    "GEO_Beak",
                    CemeteryRavenRigAnchors.HeadPivotName,
                    "raven_beak",
                    "beak_grey",
                    beak,
                    beakGrey,
                    true),
                new CemeteryRavenRendererBinding(
                    "GEO_Eye.L",
                    CemeteryRavenRigAnchors.HeadPivotName,
                    "raven_eye",
                    "eye_pale",
                    eyeLeft,
                    eyePale,
                    false),
                new CemeteryRavenRendererBinding(
                    "GEO_Eye.R",
                    CemeteryRavenRigAnchors.HeadPivotName,
                    "raven_eye",
                    "eye_pale",
                    eyeRight,
                    eyePale,
                    false),
                new CemeteryRavenRendererBinding(
                    "GEO_Wing.L",
                    CemeteryRavenRigAnchors.WingLeftPivotName,
                    "raven_wing",
                    "wing_black",
                    wingLeftMesh,
                    plumage,
                    true),
                new CemeteryRavenRendererBinding(
                    "GEO_Wing.R",
                    CemeteryRavenRigAnchors.WingRightPivotName,
                    "raven_wing",
                    "wing_black",
                    wingRightMesh,
                    plumage,
                    true),
                new CemeteryRavenRendererBinding(
                    "GEO_Tail",
                    CemeteryRavenRigAnchors.TailPivotName,
                    "raven_tail",
                    "tail_black",
                    tailMesh,
                    plumage,
                    true),
                new CemeteryRavenRendererBinding(
                    "GEO_Leg.L",
                    CemeteryRavenRigAnchors.BodyRootPivotName,
                    "raven_leg",
                    "leg_grey",
                    legLeft,
                    legGrey,
                    true),
                new CemeteryRavenRendererBinding(
                    "GEO_Leg.R",
                    CemeteryRavenRigAnchors.BodyRootPivotName,
                    "raven_leg",
                    "leg_grey",
                    legRight,
                    legGrey,
                    true)
            };
            Renderer[] renderers =
            {
                body,
                headMesh,
                beak,
                eyeLeft,
                eyeRight,
                wingLeftMesh,
                wingRightMesh,
                tailMesh,
                legLeft,
                legRight
            };

            var anchors =
                model.AddComponent<CemeteryRavenRigAnchors>();
            anchors.Configure(
                model.transform,
                renderers,
                bindings,
                bodyRoot,
                head,
                wingLeft,
                wingRight,
                tail,
                feetContact,
                Texture2D.whiteTexture,
                new Bounds(
                    new Vector3(0f, 0.115f, 0.02f),
                    new Vector3(0.17f, 0.24f, 0.36f)),
                496,
                "1.0.0",
                CemeteryRavenProvider.DesignId,
                new string('0', 64));
            return anchors;
        }
    }
}
