using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class CityMouthBridgeArt : CityRiverBridgeArt
    {
        internal const string FinishPath = "City/Surfaces/MouthBridge";
        internal const float SoffitDrop = .94f;
        internal const float GuardWidth = .435f;

        internal CityMouthBridgeArt() : base("MouthBridge", FinishPath, CityWetSurfaceKind.DressedGranite) { }

        private const float EndWidth = .98f;

        private static float EndX(Rect span, int side) =>
            span.center.x + side*((span.width - .02f)*.5f - .20f);

        internal static Vector3 AbutmentLampPlate(CityRiverBridgeDescriptor bridge, int side)
        {
            Rect span = bridge.SpanBounds;
            float x = EndX(span, side) - side*EndWidth*.5f;
            float y = bridge.AverageY + CityStreetSurfacePlanner.RoadTop - SoffitDrop - .23f;
            return new Vector3(x, y, span.yMin + .60f);
        }

        internal void Span(Transform root, Rect span, float deckY, float waterY, float bedY)
        {
            float soffit = deckY - SoffitDrop;
            float width = span.width - .02f;
            float depth = span.height - .06f;
            Vector3 centre = new(span.center.x, (soffit + deckY - .08f) * .5f, span.center.y);
            Vector3 size = new(width, SoffitDrop - .08f, depth);
            GameObject course = Batch("Bridge Underside", root, new() { Fit("StoneSpan", centre, size, Quaternion.identity) });
            Box(course, centre, size);

            for (int side = -1; side <= 1; side += 2)
            {
                float x = span.center.x + side * span.width * .30f;
                var courses = new List<RuntimeMeshPlacement>(4);
                var solids = new List<Bounds>(4);
                float height = soffit - bedY;
                float tideTop = Mathf.Clamp(waterY + .18f, bedY + .42f * height, soffit - .30f * height);
                float tideBase = Mathf.Min(tideTop - .08f, waterY - .27f);
                float shaftTop = soffit - Mathf.Min(.19f, (soffit - tideTop)*.35f);
                Stack("PierBase", bedY, tideBase, 1.65f, depth + .56f);
                Stack("TideCourse", tideBase, tideTop, 1.49f, depth + .40f);
                Stack("PierShaft", tideTop, shaftTop, 1.13f, depth + .19f);
                Stack("BearingCap", shaftTop, soffit, 1.38f, depth + .32f);
                GameObject pier = Batch(side < 0 ? "Bridge Pier West" : "Bridge Pier East", root, courses);
                foreach (Bounds solid in solids) Box(pier, solid.center, solid.size);

                // The end stones bond into the bank behind its face; their tops bear on the soffit.
                float endX = EndX(span, side);
                Vector3 end = new(endX, (bedY + soffit)*.5f, span.center.y);
                Vector3 endSize = new(EndWidth, height, depth + .18f);
                GameObject abutment = Batch(side < 0 ? "West Bank Abutment" : "East Bank Abutment", root,
                    new() { Fit("Abutment", end, endSize, Quaternion.identity) });
                Box(abutment, end, endSize);

                void Stack(string name, float from, float to, float w, float d)
                {
                    var at = new Vector3(x, (from + to)*.5f, span.center.y);
                    var dimensions = new Vector3(w, to - from, d);
                    courses.Add(Fit(name, at, dimensions, Quaternion.identity));
                    solids.Add(new Bounds(at, dimensions));
                }
            }
        }

        internal void Parapet(string name, Transform root, Vector3 start, Vector3 end)
        {
            float length = end.x - start.x;
            var size = new Vector3(length, 1.069f, GuardWidth);
            Vector3 centre = (start + end)*.5f + Vector3.up*(size.y*.5f);
            GameObject stone = Batch(name, root, new() { Fit("Parapet", centre, size, Quaternion.identity) });
            Box(stone, centre, size);
        }
    }
}
