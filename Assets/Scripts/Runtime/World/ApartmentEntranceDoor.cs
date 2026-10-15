using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One fixed-metre authored apartment door shared by Home and the warehouse entrance.</summary>
    public static class ApartmentEntranceDoor
    {
        public const float FrameWidth = 1.32f;
        public const float FrameHeight = 2.3f;
        public const float FrameDepth = .22f;
        public static IReadOnlyList<string> PartNames { get; } = Array.AsReadOnly(new[]
        {
            "Home Exit Door", "Home Entry Door Recessed Panels", "Home Entry Door Panel Mouldings",
            "Home Entry Door Frame", "Home Entry Door Architraves", "Home Entry Door Threshold",
            "Home Entry Door Hinges", "Home Entry Door Handle Plates", "Home Entry Door Levers",
            "Home Entry Door Peephole", "Home Entry Door Hardware Insets", "Home Entry Door Edge Wear"
        });

        public static GameObject Create(Transform parent, Vector3 localPosition, string name)
        {
            var door = new GameObject(name);
            door.transform.SetParent(parent, false);
            door.transform.localPosition = localPosition;
            HomeInteriorModelLibrary library = HomeInteriorModelLibrary.Load();
            foreach (string partName in PartNames)
            {
                HomeAuthoredPart part = library.Binding(partName);
                GameObject model = HomeAuthoredVisualFactory.Place(part, partName, door.transform,
                    part.Position, Vector3.one);
                model.transform.localRotation = Quaternion.Euler(part.Rotation);
            }
            return door;
        }
    }
}
