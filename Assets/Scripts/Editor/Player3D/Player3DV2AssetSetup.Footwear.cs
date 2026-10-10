using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static partial class Player3DV2AssetSetup
    {
        private static void ValidateFootwearManifest(Player3DV2Manifest manifest)
        {
            FootwearManifest footwear = manifest.footwear;
            if (footwear == null || footwear.contract != "hero_footwear_v1" ||
                footwear.source_space != "blender_z_up_minus_y_forward" || footwear.feet == null || footwear.feet.Length != 2)
                throw new InvalidOperationException("Hero requires its authored footwear flex contract.");
            var sides = new HashSet<string>(StringComparer.Ordinal);
            var parts = new HashSet<string>(manifest.parts.Select(part => part.name), StringComparer.Ordinal);
            foreach (FootwearFoot foot in footwear.feet)
            {
                if (foot == null || (foot.side != "L" && foot.side != "R") || !sides.Add(foot.side) ||
                    foot.foot_bone != "foot." + foot.side || foot.shin_bone != "shin." + foot.side ||
                    foot.renderers == null || foot.renderers.Length < 2 ||
                    foot.renderers.Distinct().Count() != foot.renderers.Length || foot.renderers.Any(name => !parts.Contains(name)) ||
                    !foot.renderers.Contains("CLO_Boot." + foot.side) || !foot.renderers.Contains("CLO_BootSole." + foot.side) ||
                    foot.toe_shapes == null || foot.toe_shapes.Length != 3 ||
                    foot.ankle_dorsiflex_shape != "BootAnkleDorsiflex." + foot.side ||
                    foot.ankle_plantarflex_shape != "BootAnklePlantarflex." + foot.side ||
                    foot.ankle_dorsiflex_max_degrees != 45f || foot.ankle_plantarflex_max_degrees != 30f ||
                    foot.ankle_weight_curve != "linear")
                    throw new InvalidOperationException("Footwear must retain both feet, measured supports and declared endpoint keys.");
                for (int index = 0; index < 3; index++)
                {
                    FootwearToeShape shape = foot.toe_shapes[index];
                    int angle = (index + 1) * 15;
                    if (shape == null || shape.angle_degrees != angle || shape.shape != "BootToeRoll" + angle + "." + foot.side)
                        throw new InvalidOperationException("Toe flex requires independent 15/30/45 degree endpoint keys.");
                }
                if (!Finite(foot.ball_pivot_blender) || !Finite(foot.ball_contact_blender) ||
                    !Finite(foot.forward_blender) || !Finite(foot.lateral_blender) || !Finite(foot.up_blender) ||
                    foot.ball_contact_blender.z >= foot.ball_pivot_blender.z ||
                    Mathf.Abs(foot.ball_contact_blender.x - foot.ball_pivot_blender.x) > .000001f ||
                    Mathf.Abs(foot.ball_contact_blender.y - foot.ball_pivot_blender.y) > .000001f ||
                    (foot.forward_blender - new Vector3(0f, -1f, 0f)).sqrMagnitude > .000001f ||
                    (foot.lateral_blender - Vector3.right).sqrMagnitude > .000001f ||
                    (foot.up_blender - Vector3.forward).sqrMagnitude > .000001f)
                    throw new InvalidOperationException("Footwear needs finite ball points and its physical forward/lateral/up frame.");
            }
        }

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static void ConfigureFootwear(GameObject root, Player3DV2Manifest manifest,
            IReadOnlyDictionary<string, Renderer> renderers, IReadOnlyDictionary<string, Transform> bones)
        {
            var authored = new List<PlayerBootDeformation.FootBinding>(2);
            foreach (FootwearFoot source in manifest.footwear.feet.OrderBy(foot => foot.side, StringComparer.Ordinal))
            {
                Transform foot = RequireTransform(bones, source.foot_bone);
                Transform shin = RequireTransform(bones, source.shin_bone);
                Vector3 WorldDirection(Vector3 point) => root.transform.TransformDirection(new Vector3(-point.x, point.z, -point.y)).normalized;
                Vector3 forward = WorldDirection(source.forward_blender), up = WorldDirection(source.up_blender);
                // A lateral rotation axis is axial: derive it after the coordinate reflection.
                Vector3 right = Vector3.Cross(up, forward).normalized;
                Vector3 ballWorld = root.transform.TransformPoint(new Vector3(-source.ball_contact_blender.x,
                    source.ball_contact_blender.z, -source.ball_contact_blender.y));
                Vector3 pivotWorld = root.transform.TransformPoint(new Vector3(-source.ball_pivot_blender.x,
                    source.ball_pivot_blender.z, -source.ball_pivot_blender.y));
                var shapes = new List<PlayerBootDeformation.ShapeBinding>(source.renderers.Length);
                foreach (string name in source.renderers)
                {
                    if (!(renderers[name] is SkinnedMeshRenderer renderer) || renderer.sharedMesh == null)
                        throw new InvalidOperationException("Footwear lost its skinned renderer: " + name);
                    int RequireShape(string shapeName, bool optional = false)
                    {
                        Mesh mesh = renderer.sharedMesh;
                        int index = CharacterJointDeformation.FindShape(mesh, shapeName);
                        if (index < 0 && optional) return -1;
                        if (index < 0 || mesh.GetBlendShapeFrameCount(index) != 1 ||
                            Mathf.Abs(mesh.GetBlendShapeFrameWeight(index, 0) - 100f) > .001f)
                            throw new InvalidOperationException("Footwear lost its single authored endpoint frame: " + name + "/" + shapeName);
                        return index;
                    }
                    shapes.Add(new PlayerBootDeformation.ShapeBinding { Renderer = renderer,
                        Toe15 = RequireShape(source.toe_shapes[0].shape), Toe30 = RequireShape(source.toe_shapes[1].shape),
                        Toe45 = RequireShape(source.toe_shapes[2].shape),
                        Dorsiflex = RequireShape(source.ankle_dorsiflex_shape, name == "CLO_BootSole." + source.side),
                        Plantarflex = RequireShape(source.ankle_plantarflex_shape, name == "CLO_BootSole." + source.side) });
                    renderer.quality = SkinQuality.Bone4;
                }
                authored.Add(new PlayerBootDeformation.FootBinding { Side = source.side == "L" ? FootSide.Left : FootSide.Right,
                    Foot = foot, Shin = shin, FootForwardLocal = foot.InverseTransformDirection(forward),
                    FootUpLocal = foot.InverseTransformDirection(up), ShinForwardLocal = shin.InverseTransformDirection(forward),
                    ShinRightLocal = shin.InverseTransformDirection(right), BallContactLocal = foot.InverseTransformPoint(ballWorld),
                    BallPivotLocal = foot.InverseTransformPoint(pivotWorld),
                    DorsiflexMaximumDegrees = source.ankle_dorsiflex_max_degrees,
                    PlantarflexMaximumDegrees = source.ankle_plantarflex_max_degrees, Shapes = shapes.ToArray() });
            }
            root.AddComponent<PlayerBootDeformation>().Configure(authored.ToArray());
        }

        [Serializable] private sealed class FootwearManifest
        {
            public string contract, source_space;
            public FootwearFoot[] feet;
        }
        [Serializable] private sealed class FootwearFoot
        {
            public string side, foot_bone, shin_bone, ankle_dorsiflex_shape, ankle_plantarflex_shape, ankle_weight_curve;
            public string[] renderers;
            public Vector3 ball_pivot_blender, ball_contact_blender, forward_blender, lateral_blender, up_blender;
            public FootwearToeShape[] toe_shapes;
            public float ankle_dorsiflex_max_degrees, ankle_plantarflex_max_degrees;
        }
        [Serializable] private sealed class FootwearToeShape { public string shape; public float angle_degrees; }
    }
}
