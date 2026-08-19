using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Keeps the stamped plate in step with what the plaque says.
    ///
    /// It exists because the board is fitted before the hero has
    /// written anything — the camera walks round to the front of the
    /// stone and a bare face there would be asking him to inscribe
    /// nothing — so the plate has to be re-stamped the moment the line
    /// is cut. It owns the texture it made and destroys it with itself,
    /// because that texture is the one thing in the grave that is per
    /// player rather than per plan.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryPlaqueSurface : MonoBehaviour
    {
        private Texture2D stamp;

        /// <summary>What is currently on the brass, for tests.
        /// </summary>
        public Texture2D Stamp => stamp;

        /// <summary>
        /// Cuts the three lines into the plate again from whatever the
        /// session currently holds.
        /// </summary>
        public void Refresh()
        {
            Renderer plate = GetComponentInChildren<Renderer>(true);
            if (plate == null)
            {
                return;
            }

            Texture2D previous = stamp;
            stamp = CemeteryPlaqueTexture.Create(
                LocalizationService.Get(
                    CemeteryEpitaph.UnknownNameKey),
                LocalizationService.Get(
                    CemeteryEpitaph.UnknownYearsKey),
                GameSessionState.GraveEpitaph);
            CityCemeteryPlaqueWorldBuilder.ApplyPlate(plate, stamp);
            Release(previous);
        }

        private void OnDestroy()
        {
            Release(stamp);
            stamp = null;
        }

        private static void Release(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }
    }
}
