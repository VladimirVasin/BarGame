using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class VillageResidentPresentation
    {
        private static BucketManifest bucketTrack;
        public static Pose SampleBucketPose(float elapsedSeconds, out float dipWeight)
        {
            if (bucketTrack == null)
            {
                var text = Resources.Load<TextAsset>("VillageLife/VillageResidentErrandActions");
                if (text == null) throw new InvalidOperationException("The authored NPC bucket bank is missing.");
                bucketTrack = JsonUtility.FromJson<BucketManifest>(text.text);
            }
            float frame = Mathf.Clamp(elapsedSeconds, 0f, 8f) * 24f;
            int index = Mathf.Min((int)frame, bucketTrack.frames.Length - 1);
            BucketSample a = bucketTrack.frames[index], b = bucketTrack.frames[Mathf.Min(index + 1, bucketTrack.frames.Length - 1)];
            float t = frame - index;
            dipWeight = Mathf.Lerp(a.dip_weight, b.dip_weight, t);
            return new Pose(Vector3.Lerp(Point(a.position), Point(b.position), t), Quaternion.Slerp(
                new Quaternion(a.rotation[0], a.rotation[1], a.rotation[2], a.rotation[3]),
                new Quaternion(b.rotation[0], b.rotation[1], b.rotation[2], b.rotation[3]), t));
        }
        [Serializable] private sealed class BucketManifest { public BucketSample[] frames; }
        [Serializable] private sealed class BucketSample { public float[] position, rotation; public float dip_weight; }
    }
}
