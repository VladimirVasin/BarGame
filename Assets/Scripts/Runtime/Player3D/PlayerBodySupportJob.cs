using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using static Unity.Burst.Intrinsics.X86;

namespace BarPromenade
{
    // Calibrated wide Mono arithmetic can return the exact float maximum.
    // Other profiles collect conservative candidates for the original Mono
    // replay; overflow requests a full scan.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlayerBodySupportJob : IJob
    {
        public const int RequiredStackCapacity = 64;
        public const int CandidateCapacity = 32;
        public const int ExactResult = -2;
        private const double FloatErrorScale = 8d / 16777216d;
        private const double DoubleErrorScale = 16d / 9007199254740992d;

        public struct Node
        {
            public int First, Count, Left, Right;
            public Vector3 Minimum, Maximum, ChangedMinimum, ChangedMaximum;
            public int ChangedCount;
        }

        [ReadOnly] public NativeArray<Vector3> Vertices, Normals;
        [ReadOnly] public NativeArray<int> Order;
        [ReadOnly] public NativeArray<bool> ChangedVertices;
        public NativeArray<Node> Nodes;
        public NativeArray<float> Offsets;
        public NativeArray<int> SupportPoints, Stack;
        public NativeArray<int> CandidateIndices, CandidateCounts;
        public NativeArray<double> CandidateScores, StackBounds;
        [MarshalAs(UnmanagedType.U1)] public bool RefitTree;
        [MarshalAs(UnmanagedType.U1)] public bool HasPreviousCloud;
        public int ExactArithmeticMode;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref PlayerBodySupportJob job) => job.Execute();

        public void Execute()
        {
            if (Vertices.Length == 0) return;
            if (RefitTree) Refit();
            bool hasTree = Nodes.Length != 0 && Stack.Length >= RequiredStackCapacity && StackBounds.Length >= Stack.Length;
            Vector3 absolute = MaximumAbsoluteCoordinates();
            for (int plane = 0; plane < Normals.Length; plane++)
            {
                Vector3 normal = Normals[plane];
                int support = SupportPoints[plane];
                bool changedOnly = HasPreviousCloud && ChangedVertices.Length == Vertices.Length && !ChangedVertices[support];
                double sumAbsolute = (double)Mathf.Abs(normal.x) * absolute.x +
                    (double)Mathf.Abs(normal.y) * absolute.y + (double)Mathf.Abs(normal.z) * absolute.z;
                double doubleError = sumAbsolute * DoubleErrorScale;
                int exactSupport = support;
                if (ExactArithmeticMode == 1 && !double.IsNaN(doubleError) && !double.IsInfinity(doubleError) &&
                    TryExactMaximum(normal, doubleError, hasTree, changedOnly, ref exactSupport, out float exactMaximum))
                {
                    Offsets[plane] = exactMaximum;
                    SupportPoints[plane] = exactSupport;
                    CandidateCounts[plane] = ExactResult;
                    continue;
                }
                double maximum = DoubleDot(normal, Vertices[support]);
                // Current maxima only rise, so rejected nodes never become
                // eligible later. Filtering keeps one conservative candidate set.
                double window = 2d * (sumAbsolute * FloatErrorScale + doubleError);
                int count = hasTree ? Collect(normal, window, doubleError, plane, changedOnly, ref maximum, ref support) :
                    CollectAll(normal, window, plane, changedOnly, ref maximum, ref support);
                Offsets[plane] = (float)maximum;
                SupportPoints[plane] = support;
                CandidateCounts[plane] = count;
            }
        }

        private bool TryExactMaximum(Vector3 normal, double error, bool hasTree, bool changedOnly,
            ref int support, out float maximum)
        {
            maximum = (float)DoubleDot(normal, Vertices[support]);
            if (float.IsNaN(maximum) || float.IsInfinity(maximum)) return false;
            // Equal nonzero floats have identical bits. Signed zero instead
            // keeps the first original vertex's bits, requiring the full cloud.
            if (maximum == 0f) changedOnly = false;
            if (!hasTree) return ExactAll(normal, changedOnly, ref maximum, ref support);

            int pending = 1;
            Stack[0] = 0;
            StackBounds[0] = UpperBound(Nodes[0], normal, changedOnly) + error;
            while (pending > 0)
            {
                int next = --pending;
                // The double corner bound plus its existing roundoff allowance
                // encloses every leaf dot. Float conversion is monotone, so a
                // strict comparison cannot discard a winning or tied float.
                if ((float)StackBounds[next] < maximum) continue;
                Node node = Nodes[Stack[next]];
                if (node.Left < 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int vertex = Order[i];
                        if (changedOnly && !ChangedVertices[vertex]) continue;
                        if (Sse2.IsSse2Supported && i + 1 < node.First + node.Count)
                        {
                            int second = Order[i + 1];
                            if (!changedOnly || ChangedVertices[second])
                            {
                                if (!ExactPair(vertex, second, normal, ref maximum, ref support)) return false;
                                i++;
                                continue;
                            }
                        }
                        if (!ExactVertex(vertex, normal, ref maximum, ref support)) return false;
                    }
                    continue;
                }
                double leftBound = UpperBound(Nodes[node.Left], normal, changedOnly) + error;
                double rightBound = UpperBound(Nodes[node.Right], normal, changedOnly) + error;
                bool visitLeft = (float)leftBound >= maximum, visitRight = (float)rightBound >= maximum;
                int needed = (visitLeft ? 1 : 0) + (visitRight ? 1 : 0);
                if (pending > Stack.Length - needed)
                    return ExactAll(normal, false, ref maximum, ref support);
                if (leftBound >= rightBound)
                {
                    if (visitRight) { Stack[pending] = node.Right; StackBounds[pending++] = rightBound; }
                    if (visitLeft) { Stack[pending] = node.Left; StackBounds[pending++] = leftBound; }
                }
                else
                {
                    if (visitLeft) { Stack[pending] = node.Left; StackBounds[pending++] = leftBound; }
                    if (visitRight) { Stack[pending] = node.Right; StackBounds[pending++] = rightBound; }
                }
            }
            return true;
        }

        private bool ExactAll(Vector3 normal, bool changedOnly, ref float maximum, ref int support)
        {
            for (int vertex = 0; vertex < Vertices.Length; vertex++)
            {
                if (changedOnly && !ChangedVertices[vertex]) continue;
                if (Sse2.IsSse2Supported && vertex + 1 < Vertices.Length &&
                    (!changedOnly || ChangedVertices[vertex + 1]))
                {
                    if (!ExactPair(vertex, vertex + 1, normal, ref maximum, ref support)) return false;
                    vertex++;
                    continue;
                }
                if (!ExactVertex(vertex, normal, ref maximum, ref support)) return false;
            }
            return true;
        }

        private bool ExactVertex(int vertex, Vector3 normal, ref float maximum, ref int support)
        {
            // Mode 1 was calibrated against the bundled Vector3.Dot: both
            // products and left-grouped sums stay double until the float return.
            float value = (float)DoubleDot(normal, Vertices[vertex]);
            return ExactValue(vertex, value, ref maximum, ref support);
        }

        private bool ExactPair(int first, int second, Vector3 normal, ref float maximum, ref int support)
        {
            Vector3 a = Vertices[first], b = Vertices[second];
            // Each double lane repeats one scalar dot, with separate multiply
            // and left-grouped add instructions. Winner updates keep visit order.
            v128 x = Sse2.mul_pd(new v128((double)a.x, (double)b.x), new v128((double)normal.x, (double)normal.x));
            v128 y = Sse2.mul_pd(new v128((double)a.y, (double)b.y), new v128((double)normal.y, (double)normal.y));
            v128 z = Sse2.mul_pd(new v128((double)a.z, (double)b.z), new v128((double)normal.z, (double)normal.z));
            v128 values = Sse2.add_pd(Sse2.add_pd(x, y), z);
            return ExactValue(first, (float)values.Double0, ref maximum, ref support) &&
                ExactValue(second, (float)values.Double1, ref maximum, ref support);
        }

        private static bool ExactValue(int vertex, float value, ref float maximum, ref int support)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            if (value > maximum || value == maximum && vertex < support)
            {
                maximum = value;
                support = vertex;
            }
            return true;
        }

        private void Refit()
        {
            // Preorder construction puts both children after their parent,
            // so reverse traversal merges freshly measured child bounds.
            for (int index = Nodes.Length - 1; index >= 0; index--)
            {
                Node node = Nodes[index];
                if (node.Left < 0) MeasureLeaf(ref node);
                else
                {
                    Node left = Nodes[node.Left], right = Nodes[node.Right];
                    node.Minimum = Vector3.Min(left.Minimum, right.Minimum);
                    node.Maximum = Vector3.Max(left.Maximum, right.Maximum);
                    node.ChangedCount = left.ChangedCount + right.ChangedCount;
                    if (left.ChangedCount > 0 && right.ChangedCount > 0)
                    {
                        node.ChangedMinimum = Vector3.Min(left.ChangedMinimum, right.ChangedMinimum);
                        node.ChangedMaximum = Vector3.Max(left.ChangedMaximum, right.ChangedMaximum);
                    }
                    else if (node.ChangedCount > 0)
                    {
                        node.ChangedMinimum = left.ChangedCount > 0 ? left.ChangedMinimum : right.ChangedMinimum;
                        node.ChangedMaximum = left.ChangedCount > 0 ? left.ChangedMaximum : right.ChangedMaximum;
                    }
                }
                Nodes[index] = node;
            }
        }

        private void MeasureLeaf(ref Node node)
        {
            node.ChangedCount = 0;
            bool hasChanges = ChangedVertices.Length == Vertices.Length;
            for (int i = node.First; i < node.First + node.Count; i++)
            {
                int vertex = Order[i];
                Vector3 point = Vertices[vertex];
                if (i == node.First) node.Minimum = node.Maximum = point;
                else
                {
                    node.Minimum = Vector3.Min(node.Minimum, point);
                    node.Maximum = Vector3.Max(node.Maximum, point);
                }
                if (!hasChanges || !ChangedVertices[vertex]) continue;
                if (node.ChangedCount == 0) node.ChangedMinimum = node.ChangedMaximum = point;
                else
                {
                    node.ChangedMinimum = Vector3.Min(node.ChangedMinimum, point);
                    node.ChangedMaximum = Vector3.Max(node.ChangedMaximum, point);
                }
                node.ChangedCount++;
            }
        }

        private int Collect(Vector3 normal, double window, double error, int plane, bool changedOnly, ref double maximum, ref int support)
        {
            int pending = 1, count = 0;
            double discarded = double.NegativeInfinity;
            // Every other fixed vertex was already below this exact Mono winner.
            if (changedOnly) Accumulate(support, normal, window, plane, ref maximum, ref support, ref count, ref discarded);
            Stack[0] = 0;
            StackBounds[0] = UpperBound(Nodes[0], normal, changedOnly) + error;
            while (pending > 0)
            {
                int next = --pending;
                double threshold = maximum - window;
                // Parent traversal already evaluated this immutable bound.
                // Only the live candidate threshold can change before the pop.
                if (StackBounds[next] < threshold) continue;
                Node node = Nodes[Stack[next]];
                if (node.Left < 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int vertex = Order[i];
                        if (changedOnly && !ChangedVertices[vertex]) continue;
                        Accumulate(vertex, normal, window, plane, ref maximum, ref support, ref count, ref discarded);
                    }
                    continue;
                }
                Node left = Nodes[node.Left], right = Nodes[node.Right];
                double leftBound = UpperBound(left, normal, changedOnly) + error;
                double rightBound = UpperBound(right, normal, changedOnly) + error;
                bool visitLeft = leftBound >= threshold, visitRight = rightBound >= threshold;
                int needed = (visitLeft ? 1 : 0) + (visitRight ? 1 : 0);
                if (pending > Stack.Length - needed)
                    return CollectAll(normal, window, plane, false, ref maximum, ref support);
                if (leftBound >= rightBound)
                {
                    if (visitRight) { Stack[pending] = node.Right; StackBounds[pending++] = rightBound; }
                    if (visitLeft) { Stack[pending] = node.Left; StackBounds[pending++] = leftBound; }
                }
                else
                {
                    if (visitLeft) { Stack[pending] = node.Left; StackBounds[pending++] = leftBound; }
                    if (visitRight) { Stack[pending] = node.Right; StackBounds[pending++] = rightBound; }
                }
            }
            return discarded >= maximum - window ? -1 : count;
        }

        private int CollectAll(Vector3 normal, double window, int plane, bool changedOnly, ref double maximum, ref int support)
        {
            int count = 0;
            double discarded = double.NegativeInfinity;
            if (changedOnly) Accumulate(support, normal, window, plane, ref maximum, ref support, ref count, ref discarded);
            for (int i = 0; i < Order.Length; i++)
            {
                int vertex = Order[i];
                if (changedOnly && !ChangedVertices[vertex]) continue;
                Accumulate(vertex, normal, window, plane, ref maximum, ref support, ref count, ref discarded);
            }
            return discarded >= maximum - window ? -1 : count;
        }

        private void Accumulate(int vertex, Vector3 normal, double window, int plane,
            ref double maximum, ref int support,
            ref int count, ref double discarded)
        {
            double value = DoubleDot(normal, Vertices[vertex]);
            int first = plane * CandidateCapacity;
            if (value > maximum)
            {
                maximum = value; support = vertex;
                double threshold = maximum - window;
                int kept = 0;
                for (int i = 0; i < count; i++)
                {
                    double score = CandidateScores[i];
                    if (score < threshold) continue;
                    CandidateScores[kept] = score;
                    CandidateIndices[first + kept++] = CandidateIndices[first + i];
                }
                count = kept;
                // Lost candidates can be forgotten only after every lost value
                // falls strictly below the final winner's conservative window.
                if (discarded < threshold) discarded = double.NegativeInfinity;
            }
            if (value < maximum - window) return;
            if (count < CandidateCapacity)
            {
                CandidateScores[count] = value;
                CandidateIndices[first + count++] = vertex;
            }
            else if (value > discarded) discarded = value;
        }

        private static double UpperBound(Node node, Vector3 normal, bool changedOnly)
        {
            if (changedOnly && node.ChangedCount == 0) return double.NegativeInfinity;
            Vector3 minimum = changedOnly ? node.ChangedMinimum : node.Minimum;
            Vector3 maximum = changedOnly ? node.ChangedMaximum : node.Maximum;
            Vector3 corner = new Vector3(normal.x >= 0f ? maximum.x : minimum.x,
                normal.y >= 0f ? maximum.y : minimum.y,
                normal.z >= 0f ? maximum.z : minimum.z);
            return DoubleDot(normal, corner);
        }

        private Vector3 MaximumAbsoluteCoordinates()
        {
            if (Nodes.Length != 0)
            {
                Node root = Nodes[0];
                return new Vector3(Mathf.Max(Mathf.Abs(root.Minimum.x), Mathf.Abs(root.Maximum.x)),
                    Mathf.Max(Mathf.Abs(root.Minimum.y), Mathf.Abs(root.Maximum.y)),
                    Mathf.Max(Mathf.Abs(root.Minimum.z), Mathf.Abs(root.Maximum.z)));
            }
            Vector3 maximum = Vector3.zero;
            for (int i = 0; i < Vertices.Length; i++)
            {
                Vector3 point = Vertices[i];
                maximum = Vector3.Max(maximum, new Vector3(Mathf.Abs(point.x), Mathf.Abs(point.y), Mathf.Abs(point.z)));
            }
            return maximum;
        }

        private static double DoubleDot(Vector3 normal, Vector3 point) =>
            ((double)normal.x * point.x + (double)normal.y * point.y) + (double)normal.z * point.z;

    }
}
