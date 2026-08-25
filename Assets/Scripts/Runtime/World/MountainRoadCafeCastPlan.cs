using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadCafeCastRole
    {
        LonePatron = 0,
        PairMan = 1,
        PairWoman = 2,
        Attendant = 3
    }

    /// <summary>
    /// One authored mark in the cafe's silent four-person composition.
    /// Positions are world-space results of the cafe basis, so the staged
    /// prefabs never need to know anything about the terminal layout.
    /// </summary>
    public sealed class MountainRoadCafeCastMemberPlan
    {
        internal MountainRoadCafeCastMemberPlan(
            MountainRoadCafeCastRole role,
            string name,
            string stableId,
            Vector3 position,
            Vector3 facing,
            float idlePhaseSeconds)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "A cafe cast member requires a name.",
                    nameof(name));
            }

            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A cafe cast member requires a stable id.",
                    nameof(stableId));
            }

            Vector3 flatFacing = new Vector3(facing.x, 0f, facing.z);
            if (flatFacing.sqrMagnitude < 0.0001f)
            {
                throw new ArgumentException(
                    "A cafe cast member requires a horizontal facing.",
                    nameof(facing));
            }

            Role = role;
            Name = name;
            StableId = stableId;
            Position = position;
            Facing = flatFacing.normalized;
            IdlePhaseSeconds = Mathf.Max(0f, idlePhaseSeconds);
        }

        public MountainRoadCafeCastRole Role { get; }
        public string Name { get; }
        public string StableId { get; }
        public Vector3 Position { get; }
        public Vector3 Facing { get; }
        public float IdlePhaseSeconds { get; }
    }

    /// <summary>
    /// Pure placement contract for the four figures. The empty stool between
    /// the lone man and the pair is deliberately absent from this list: it is
    /// negative space, not a spawn slot.
    /// </summary>
    public sealed class MountainRoadCafeCastPlan
    {
        private MountainRoadCafeCastPlan(
            IList<MountainRoadCafeCastMemberPlan> sourceMembers)
        {
            Members = new ReadOnlyCollection<
                MountainRoadCafeCastMemberPlan>(
                    new List<MountainRoadCafeCastMemberPlan>(
                        sourceMembers));
        }

        public IReadOnlyList<MountainRoadCafeCastMemberPlan> Members
        {
            get;
        }

        public static MountainRoadCafeCastPlan Create(
            MountainRoadCafePlan cafe)
        {
            if (cafe == null)
            {
                throw new ArgumentNullException(nameof(cafe));
            }

            Vector3 forward = cafe.Forward.normalized;
            Vector3 right = cafe.Right.normalized;
            var members = new List<MountainRoadCafeCastMemberPlan>(
                MountainRoadCafeWorldBuilder.TableauNpcCount)
            {
                CreateMember(
                    cafe,
                    MountainRoadCafeCastRole.LonePatron,
                    "Lone Patron",
                    MountainRoadCafeWorldBuilder.LonePatronAnchorId,
                    -1.50f,
                    -2.18f,
                    forward,
                    1.10f),
                CreateMember(
                    cafe,
                    MountainRoadCafeCastRole.PairMan,
                    "Couple Man",
                    MountainRoadCafeWorldBuilder.PairFirstAnchorId,
                    0.75f,
                    -2.18f,
                    (forward + right * 0.12f).normalized,
                    2.35f),
                CreateMember(
                    cafe,
                    MountainRoadCafeCastRole.PairWoman,
                    "Couple Woman",
                    MountainRoadCafeWorldBuilder.PairSecondAnchorId,
                    1.80f,
                    -2.18f,
                    (forward - right * 0.09f).normalized,
                    0.45f),
                CreateMember(
                    cafe,
                    MountainRoadCafeCastRole.Attendant,
                    "White Attendant",
                    MountainRoadCafeWorldBuilder.AttendantAnchorId,
                    2.10f,
                    -0.16f,
                    -forward,
                    1.70f)
            };

            return new MountainRoadCafeCastPlan(members);
        }

        private static MountainRoadCafeCastMemberPlan CreateMember(
            MountainRoadCafePlan cafe,
            MountainRoadCafeCastRole role,
            string name,
            string stableId,
            float right,
            float forward,
            Vector3 facing,
            float idlePhaseSeconds)
        {
            Vector3 position = cafe.Center +
                               cafe.Right * right +
                               cafe.Forward * forward;
            return new MountainRoadCafeCastMemberPlan(
                role,
                name,
                stableId,
                position,
                facing,
                idlePhaseSeconds);
        }
    }
}
