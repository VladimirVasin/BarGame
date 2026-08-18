using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The four combined batches the static set reaches the frame as,
    /// remembered by which table they stand on.
    ///
    /// They exist to be switched off. Fifty-six men that never move are
    /// four draw calls; a game in progress is one object per man, and
    /// the moment the hero sits down at a board that board's two
    /// batches are replaced by live pieces. The other table keeps its
    /// batches: only one game is ever being played.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityChessSetMen : MonoBehaviour
    {
        private readonly List<GameObject> chessBatches =
            new List<GameObject>(2);
        private readonly List<GameObject> draughtsBatches =
            new List<GameObject>(2);

        public IReadOnlyList<GameObject> ChessBatches => chessBatches;
        public IReadOnlyList<GameObject> DraughtsBatches =>
            draughtsBatches;

        public void RegisterBatch(int table, GameObject batch)
        {
            if (batch == null)
            {
                return;
            }

            if (table > 0)
            {
                draughtsBatches.Add(batch);
            }
            else
            {
                chessBatches.Add(batch);
            }
        }

        /// <summary>
        /// Shows or hides the untouched opening on one table. Called
        /// with `false` exactly once per table — the first time a game
        /// starts there — because the set never goes back to being the
        /// position it was set up in.
        /// </summary>
        public void SetTableVisible(int table, bool visible)
        {
            List<GameObject> batches =
                table > 0 ? draughtsBatches : chessBatches;
            for (int index = 0; index < batches.Count; index++)
            {
                GameObject batch = batches[index];
                if (batch != null)
                {
                    batch.SetActive(visible);
                }
            }
        }
    }
}
