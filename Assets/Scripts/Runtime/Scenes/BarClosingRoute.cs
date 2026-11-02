using UnityEngine;

namespace BarPromenade
{
    /// <summary>A bounded, on-demand capsule route through the actual furnished room.</summary>
    internal sealed class BarClosingRoute
    {
        private const float Step = .4f;
        private const int Capacity = 4096;
        private readonly int[] previous = new int[Capacity];
        private readonly int[] queue = new int[Capacity];
        private readonly byte[] clearance = new byte[Capacity];
        private readonly Vector3[] reversed = new Vector3[Capacity];
        private Collider[] overlaps = new Collider[16];
        private RaycastHit[] hits = new RaycastHit[16];
        private readonly Transform frame, hero;
        private readonly BarInteriorWalkableArea area;
        private readonly Rect bounds;
        private readonly int columns, rows;
        private readonly float radius;

        public BarClosingRoute(BarInteriorRoot bar)
        {
            frame = bar.transform;
            hero = bar.Player.GameObject.transform;
            area = new BarInteriorWalkableArea(bar.Layout);
            Rect hall = bar.Layout.WalkableBounds, wc = bar.Layout.WashroomBounds;
            bounds = Rect.MinMaxRect(Mathf.Min(hall.xMin, wc.xMin), Mathf.Min(hall.yMin, wc.yMin),
                Mathf.Max(hall.xMax, wc.xMax), Mathf.Max(hall.yMax, wc.yMax));
            columns = Mathf.CeilToInt(bounds.width / Step) + 1;
            rows = Mathf.CeilToInt(bounds.height / Step) + 1;
            radius = bar.Player.GameObject.GetComponent<CharacterController>().radius + .045f;
        }

        public bool TryBuild(Vector3 destination, Vector3[] output, out int count)
        {
            count = 0;
            Vector3 start = hero.position;
            if (ClearSegment(start, destination)) return true;
            int size = columns * rows;
            if (size > Capacity) return false;
            System.Array.Clear(clearance, 0, size);
            System.Array.Fill(previous, -2, 0, size);
            int head = 0, tail = 0, found = -1;
            // Connect the live pose to nearby reachable grid cells; never snap it onto the grid.
            Vector3 localStart = frame.InverseTransformPoint(start);
            for (int z = Mathf.Max(0, Mathf.FloorToInt((localStart.z - bounds.yMin) / Step) - 2);
                 z <= Mathf.Min(rows - 1, Mathf.CeilToInt((localStart.z - bounds.yMin) / Step) + 2); z++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt((localStart.x - bounds.xMin) / Step) - 2);
                     x <= Mathf.Min(columns - 1, Mathf.CeilToInt((localStart.x - bounds.xMin) / Step) + 2); x++)
                {
                    int index = z * columns + x;
                    if (!ClearCell(index) || !ClearSegment(start, Point(index))) continue;
                    previous[index] = -1;
                    queue[tail++] = index;
                }
            while (head < tail)
            {
                int index = queue[head++];
                Vector3 point = Point(index);
                if ((point - destination).sqrMagnitude < 1f && ClearSegment(point, destination))
                { found = index; break; }
                int x = index % columns, z = index / columns;
                Visit(index, x - 1, z, ref tail);
                Visit(index, x + 1, z, ref tail);
                Visit(index, x, z - 1, ref tail);
                Visit(index, x, z + 1, ref tail);
            }
            if (found < 0) return false;
            int length = 0;
            for (int cursor = found; cursor >= 0; cursor = previous[cursor]) reversed[length++] = Point(cursor);
            Vector3 anchor = start;
            // Shorten only over a proven free capsule segment. Tight corners are not cut.
            for (int cursor = length - 1; cursor >= 0;)
            {
                int next = cursor;
                while (next > 0 && ClearSegment(anchor, reversed[next - 1])) next--;
                if (count == output.Length) { count = 0; return false; }
                output[count++] = reversed[next];
                anchor = reversed[next];
                cursor = next - 1;
            }
            return true;
        }

        private void Visit(int parent, int x, int z, ref int tail)
        {
            if (x < 0 || x >= columns || z < 0 || z >= rows) return;
            int index = z * columns + x;
            if (previous[index] != -2 || !ClearCell(index) || !ClearSegment(Point(parent), Point(index))) return;
            previous[index] = parent;
            queue[tail++] = index;
        }

        private Vector3 Point(int index) => frame.TransformPoint(new Vector3(
            bounds.xMin + index % columns * Step, PlayerFactory.GroundedRootOffset,
            bounds.yMin + index / columns * Step));

        private bool ClearCell(int index)
        {
            if (clearance[index] != 0) return clearance[index] == 1;
            Vector3 position = Point(index);
            bool clear = area.Contains(frame.InverseTransformPoint(position), radius) && ClearCapsule(position);
            clearance[index] = (byte)(clear ? 1 : 2);
            return clear;
        }

        private bool ClearCapsule(Vector3 position)
        {
            Capsule(position, out Vector3 bottom, out Vector3 top);
            int count;
            while ((count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps,
                       ~0, QueryTriggerInteraction.Ignore)) == overlaps.Length)
                System.Array.Resize(ref overlaps, overlaps.Length * 2);
            for (int index = 0; index < count; index++)
                if (!overlaps[index].transform.IsChildOf(hero)) return false;
            return true;
        }

        private bool ClearSegment(Vector3 start, Vector3 end)
        {
            Vector3 localStart = frame.InverseTransformPoint(start), localEnd = frame.InverseTransformPoint(end);
            if ((area.Constrain(localStart, localEnd, radius) - localEnd).sqrMagnitude > .0001f) return false;
            Vector3 direction = end - start;
            direction.y = 0f;
            float distance = direction.magnitude;
            if (distance < .001f) return ClearCapsule(end);
            Capsule(start, out Vector3 bottom, out Vector3 top);
            int count;
            while ((count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction / distance,
                       hits, distance, ~0, QueryTriggerInteraction.Ignore)) == hits.Length)
                System.Array.Resize(ref hits, hits.Length * 2);
            for (int index = 0; index < count; index++)
                if (!hits[index].transform.IsChildOf(hero)) return false;
            return ClearCapsule(end);
        }

        private void Capsule(Vector3 root, out Vector3 bottom, out Vector3 top)
        {
            // Step-height floor/podium surfaces remain traversable; walls/furniture/bodies block.
            bottom = root + Vector3.up * (.18f + radius);
            top = root + Vector3.up * (1.68f - radius);
        }
    }
}
