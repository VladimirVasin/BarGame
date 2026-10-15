using System;

namespace BarPromenade
{
    public enum WarehouseFishPart { Tail = 0, Belly = 1, Head = 2 }
    public enum WarehouseFishAssemblyPhase { WaitingForCans, Pouring, Assembling, Convulsing, Spitting, Complete }

    /// <summary>Metres and Unity-style yaw in the table's local XZ plane; no scene references.</summary>
    public readonly struct WarehouseFishPartPose
    {
        public WarehouseFishPartPose(float x, float z, float yaw) { X = x; Z = z; Yaw = yaw; }
        public float X { get; }
        public float Z { get; }
        public float Yaw { get; }
    }

    /// <summary>One finite table for the current game, independent of scene and warehouse seed.</summary>
    public sealed class WarehouseFishAssemblyState
    {
        public const int PartCount = 3;
        public const float PourSeconds = 3.2f, ConvulsionSeconds = 4f, SpitSeconds = 1f;
        public const float PartSpacing = .12f, SnapDistance = .055f, SnapAngle = 15f;
        private readonly WarehouseFishPartPose[] poses = new WarehouseFishPartPose[PartCount];

        public WarehouseFishAssemblyState() => Reset();

        public WarehouseFishAssemblyPhase Phase { get; private set; }
        public bool HasTransferredCans => Phase != WarehouseFishAssemblyPhase.WaitingForCans;
        public int LinkedMask { get; private set; }
        public bool IsAssembled => LinkedMask == 3;
        public float PourElapsed { get; private set; }
        public float FinaleElapsed { get; private set; }
        public float PhaseElapsed => Phase == WarehouseFishAssemblyPhase.Pouring ? PourElapsed :
            Phase == WarehouseFishAssemblyPhase.Convulsing ? FinaleElapsed :
            Phase == WarehouseFishAssemblyPhase.Spitting ? FinaleElapsed - ConvulsionSeconds : 0f;
        public bool RewardGranted { get; private set; }
        public bool ReceiptPending { get; private set; }

        public WarehouseFishPartPose GetPose(WarehouseFishPart part) => poses[Index(part)];

        public bool IsPartLinked(WarehouseFishPart part)
        {
            Index(part);
            return part == WarehouseFishPart.Belly ? LinkedMask != 0 :
                (LinkedMask & (part == WarehouseFishPart.Tail ? 1 : 2)) != 0;
        }

        /// <summary>Moves a joined group rigidly when any member is dragged; loose pieces stay independent.</summary>
        public bool SetPose(WarehouseFishPart part, WarehouseFishPartPose pose)
        {
            int index = Index(part);
            if ((Phase != WarehouseFishAssemblyPhase.Pouring && Phase != WarehouseFishAssemblyPhase.Assembling) ||
                !Finite(pose.X) || !Finite(pose.Z) || !Finite(pose.Yaw)) return false;
            pose = new WarehouseFishPartPose(pose.X, pose.Z, Normalize(pose.Yaw));
            WarehouseFishPartPose previous = poses[index];
            bool moveGroup = part == WarehouseFishPart.Belly || IsPartLinked(part);
            if (moveGroup)
            {
                float turn = Normalize(pose.Yaw - previous.Yaw);
                for (int other = 0; other < PartCount; other++)
                {
                    if (other == index || other != (int)WarehouseFishPart.Belly &&
                        !IsPartLinked((WarehouseFishPart)other)) continue;
                    WarehouseFishPartPose old = poses[other];
                    Rotate(old.X - previous.X, old.Z - previous.Z, turn, out float x, out float z);
                    poses[other] = new WarehouseFishPartPose(pose.X + x, pose.Z + z, Normalize(old.Yaw + turn));
                }
            }
            poses[index] = pose;
            return true;
        }

        /// <summary>Both authored seams are checked, so moving the belly can join either loose end.</summary>
        public bool TrySnap(WarehouseFishPart movedPart)
        {
            Index(movedPart);
            if (Phase != WarehouseFishAssemblyPhase.Assembling) return false;
            bool joined = TryJoin(WarehouseFishPart.Tail, 1, -PartSpacing);
            joined = TryJoin(WarehouseFishPart.Head, 2, PartSpacing) || joined;
            if (IsAssembled)
            {
                Phase = WarehouseFishAssemblyPhase.Convulsing;
                FinaleElapsed = 0f;
            }
            return joined;
        }

        private bool TryJoin(WarehouseFishPart part, int bit, float offset)
        {
            if ((LinkedMask & bit) != 0) return false;
            WarehouseFishPartPose belly = poses[(int)WarehouseFishPart.Belly];
            Rotate(offset, 0f, belly.Yaw, out float x, out float z);
            var expected = new WarehouseFishPartPose(belly.X + x, belly.Z + z, belly.Yaw);
            WarehouseFishPartPose actual = poses[(int)part];
            float dx = actual.X - expected.X, dz = actual.Z - expected.Z;
            if (dx * dx + dz * dz > SnapDistance * SnapDistance ||
                Math.Abs(Normalize(actual.Yaw - expected.Yaw)) > SnapAngle) return false;
            poses[(int)part] = expected;
            LinkedMask |= bit;
            return true;
        }

        /// <summary>Called only after the session has atomically transferred all three cans.</summary>
        public bool BeginPouring()
        {
            if (HasTransferredCans) return false;
            Phase = WarehouseFishAssemblyPhase.Pouring;
            return true;
        }

        public void Advance(float seconds)
        {
            if (!Finite(seconds) || seconds <= 0f) return;
            if (Phase == WarehouseFishAssemblyPhase.Pouring)
            {
                PourElapsed = Math.Min(PourSeconds, PourElapsed + seconds);
                if (PourElapsed >= PourSeconds) Phase = WarehouseFishAssemblyPhase.Assembling;
            }
            else if (Phase == WarehouseFishAssemblyPhase.Convulsing || Phase == WarehouseFishAssemblyPhase.Spitting)
            {
                FinaleElapsed = Math.Min(ConvulsionSeconds + SpitSeconds, FinaleElapsed + seconds);
                Phase = FinaleElapsed >= ConvulsionSeconds + SpitSeconds ? WarehouseFishAssemblyPhase.Complete :
                    FinaleElapsed >= ConvulsionSeconds ? WarehouseFishAssemblyPhase.Spitting : WarehouseFishAssemblyPhase.Convulsing;
            }
        }

        public bool MarkRewardGranted()
        {
            if (Phase != WarehouseFishAssemblyPhase.Complete || RewardGranted) return false;
            RewardGranted = ReceiptPending = true;
            return true;
        }

        public bool MarkReceiptPresented()
        {
            if (!RewardGranted || !ReceiptPending) return false;
            ReceiptPending = false;
            return true;
        }

        public void Reset()
        {
            Phase = WarehouseFishAssemblyPhase.WaitingForCans;
            LinkedMask = 0;
            PourElapsed = FinaleElapsed = 0f;
            RewardGranted = ReceiptPending = false;
            poses[0] = new WarehouseFishPartPose(-.38f, -.18f, 24f);
            poses[1] = new WarehouseFishPartPose(0f, .18f, -38f);
            poses[2] = new WarehouseFishPartPose(.38f, -.15f, 157f);
        }

        private static int Index(WarehouseFishPart part)
        {
            int index = (int)part;
            if (index < 0 || index >= PartCount) throw new ArgumentOutOfRangeException(nameof(part));
            return index;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Normalize(float value)
        {
            value %= 360f;
            return value > 180f ? value - 360f : value < -180f ? value + 360f : value;
        }

        private static void Rotate(float x, float z, float yaw, out float resultX, out float resultZ)
        {
            double angle = yaw * (Math.PI / 180d);
            float cosine = (float)Math.Cos(angle), sine = (float)Math.Sin(angle);
            resultX = x * cosine + z * sine;
            resultZ = z * cosine - x * sine;
        }
    }
}
