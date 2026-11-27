using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Metre-space contacts shared by the mesh, moving parts, route and interaction.</summary>
    public sealed class CityStreetUtilityModel : MonoBehaviour
    {
        internal const float Outside = 1.03f;
        internal const float Inside = .27f;
        internal const float BinStand = .83f;
        private Transform hinge, handle, receiver, key, firstCord, secondCord;
        private Transform cardboard, cardboardGrip;
        private Quaternion restCardboardRotation;
        private Transform[] dialKeys;
        private Vector3[] restKeys;
        private Quaternion closedRotation, receiverRotation;
        private Vector3 receiverRest;
        internal string SiteId { get; private set; }
        internal Vector3 Handle => handle.position;
        internal Vector3 CardboardGrip => cardboardGrip.position;
        internal Vector3 Receiver => receiver.position;
        internal Vector3 Key => key.position;
        internal Vector3 DialKey(int index) => transform.TransformPoint(restKeys[index]);
        internal Vector3 KeyFace(int index) => dialKeys[index].position;
        internal void SetKeyTravel(int index, float weight) =>
            dialKeys[index].position = DialKey(index) - transform.forward * (.0035f * weight);
        internal bool Phone => receiver != null;
        internal float OpenAmount { get; private set; }

        internal static bool IsUtility(CityDecorationKind kind) => kind == CityDecorationKind.RoadsidePhoneBooth ||
            kind == CityDecorationKind.RoadsideDumpsterAndUtility;

        internal static CityStreetUtilityModel Build(Transform parent, CityDecorationDescriptor descriptor,
            Vector3 origin, Vector3 forward, bool physical)
        {
            bool phone = descriptor.Kind == CityDecorationKind.RoadsidePhoneBooth;
            string asset = phone ? "PhoneBooth" : "Dumpster";
            var template = Resources.Load<GameObject>("City/StreetUtilities/" + asset);
            if (!template) throw new InvalidOperationException("Missing municipal model " + asset);
            var root = new GameObject(descriptor.StableId).transform;
            root.SetParent(parent, false);
            root.localPosition = origin;
            root.localRotation = Quaternion.LookRotation(forward);
            var mesh = Instantiate(template, root, false).transform;
            var model = root.gameObject.AddComponent<CityStreetUtilityModel>();
            model.SiteId = descriptor.StableId;
            var nodes = new Dictionary<string, Transform>();
            foreach (Transform part in mesh.GetComponentsInChildren<Transform>(true)) nodes[part.name] = part;
            model.hinge = nodes[phone ? "Door" : "Lid"];
            model.handle = nodes["Handle"];
            model.handle.SetParent(model.hinge, true);
            model.closedRotation = model.hinge.localRotation;
            if (phone)
            {
                nodes["DoorGlass"].SetParent(model.hinge, true);
                model.receiver = nodes["Receiver"];
                model.key = nodes["Key"];
                model.dialKeys = new[] { nodes["Key4"], nodes["Key5"], nodes["Key2"] };
                model.restKeys = Array.ConvertAll(model.dialKeys, cap => root.InverseTransformPoint(cap.position));
                model.receiverRest = root.InverseTransformPoint(model.receiver.position);
                model.receiverRotation = Quaternion.Inverse(root.rotation) * model.receiver.rotation;
                model.firstCord = Cord(nodes["UpperCord"]);
                model.secondCord = Cord(nodes["LowerCord"]);
                model.SetReceiver(model.receiver.position, 0f);
                var lamp = nodes["LampSurface"].GetComponent<Renderer>();
                lamp.sharedMaterial = CityNightResources.EmissiveMaterial;
                CityNightGlowRegistry.Register(lamp, new Color(1.15f, 1.05f, .80f));
                if (physical)
                {
                    Box("Rear wall", new Vector3(0,1.25f,-.53f), new Vector3(1.34f,2.5f,.065f));
                    foreach (float x in new[] { -.64f, .64f })
                        Box("Side wall", new Vector3(x,1.25f,0), new Vector3(.075f,2.5f,1.06f));
                    var door = Box("Door collision", new Vector3(0,1.21f,.49f), new Vector3(1.01f,2.34f,.055f));
                    door.SetParent(model.hinge, true);
                }
            }
            else
            {
                model.cardboard = nodes["Cardboard"];
                model.cardboardGrip = nodes["CardboardGrip"];
                model.cardboardGrip.SetParent(model.cardboard, true);
                model.restCardboardRotation = model.cardboard.localRotation;
                if (physical) Box("Steel bin", new Vector3(0,.57f,0), new Vector3(1.78f,1.13f,.96f));
            }
            return model;

            Transform Box(string label, Vector3 center, Vector3 size)
            {
                var part = new GameObject(label).transform;
                part.SetParent(root, false);
                part.localPosition = center;
                part.gameObject.AddComponent<BoxCollider>().size = size;
                return part;
            }

            Transform Cord(Transform imported)
            {
                // FBX empties may carry an axis conversion. Pose a metric frame around the authored mesh.
                var frame = new GameObject(imported.name + " span").transform;
                frame.SetParent(root, false);
                imported.SetParent(frame, true);
                return frame;
            }
        }

        internal void CloseWhenClear(Transform hero)
        {
            if (Phone && isActiveAndEnabled && OpenAmount > .01f && InDoorway(hero))
                StartCoroutine(WaitForDoorway(hero));
            else SetOpen(0f);
        }

        private bool InDoorway(Transform hero)
        {
            if (!hero) return false;
            Vector3 p = transform.InverseTransformPoint(hero.position);
            return Mathf.Abs(p.x) < .89f && p.z < .85f && p.z > -.82f && Mathf.Abs(p.y) < .6f;
        }

        private System.Collections.IEnumerator WaitForDoorway(Transform hero)
        {
            SetOpen(1f);
            while (InDoorway(hero)) yield return null;
            SetOpen(0f);
        }

        internal void SetCardboard(float lift, float direction)
        {
            cardboard.rotation = transform.rotation * Quaternion.Euler(-31f * lift, 13f * lift * direction, 0f) *
                Quaternion.Inverse(transform.rotation) * cardboard.parent.rotation * restCardboardRotation;
        }

        internal void SetOpen(float amount)
        {
            OpenAmount = Mathf.Clamp01(amount);
            Quaternion turn = Quaternion.AngleAxis(OpenAmount * (Phone ? 90f : -84f),
                Phone ? transform.up : transform.right);
            hinge.rotation = turn * hinge.parent.rotation * closedRotation;
        }

        internal void SetReceiver(Vector3 ear, float lift)
        {
            Vector3 rest = transform.TransformPoint(receiverRest);
            receiver.SetPositionAndRotation(Vector3.Lerp(rest, ear, lift) + transform.forward * (.075f * Mathf.Sin(lift * Mathf.PI)),
                Quaternion.AngleAxis(lift * 18f, transform.forward) * transform.rotation * receiverRotation);
            Vector3 socket = transform.TransformPoint(new Vector3(.19f,1.16f,-.33f));
            Vector3 end = receiver.position - transform.up * .105f;
            Vector3 bend = (socket + end) * .5f - transform.up * Mathf.Max(.07f,.32f - Vector3.Distance(socket,end)*.20f);
            Span(firstCord,socket,bend);
            Span(secondCord,bend,end);
        }

        private static void Span(Transform part, Vector3 a, Vector3 b)
        {
            Vector3 delta = b-a;
            part.SetPositionAndRotation(a, Quaternion.FromToRotation(Vector3.up,delta.normalized));
            part.localScale = new Vector3(1f,delta.magnitude,1f);
        }
    }
}
