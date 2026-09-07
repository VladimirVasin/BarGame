using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One walk through the pool for the whole playthrough.
    ///
    /// The City is loaded <see cref="UnityEngine.SceneManagement.LoadSceneMode.Single"/>
    /// every time the hero steps into a bar, a stairwell or his own flat
    /// and comes back out, and the insult controller is built again with
    /// the city root. A walk that lived on the controller therefore began
    /// again at the city seed on every return — and the city seed is a
    /// constant, so the street said the same lines in the same order after
    /// every door, in every playthrough, on every machine. The walk lives
    /// here instead, above the scene, and the salt below is what makes one
    /// playthrough sound unlike the last.
    ///
    /// Reset on a fresh domain and on a new game, the way the wet surfaces
    /// and the garden pots are.
    /// </summary>
    public static class CityPedestrianInsultSessionState
    {
        private static CityPedestrianInsultWalk walk;
        private static int salt;
        private static bool salted;

        /// <summary>The salt this playthrough is walking on.</summary>
        public static int Salt
        {
            get
            {
                EnsureSalt();
                return salt;
            }
        }

        /// <summary>
        /// The session's walk, made on the first call and handed back
        /// unchanged to every City after it.
        /// </summary>
        public static CityPedestrianInsultWalk Walk(int citySeed)
        {
            if (walk == null)
            {
                EnsureSalt();
                walk = new CityPedestrianInsultWalk(
                    CityPedestrianInsultLines.CreateState(citySeed, salt));
            }

            return walk;
        }

        /// <summary>Pins the salt before the walk is made, so a fixture can
        /// hear the same street twice.</summary>
        public static void BeginWithSalt(int sessionSalt)
        {
            salt = sessionSalt;
            salted = true;
            walk = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            walk = null;
            salted = false;
            salt = 0;
        }

        /// <summary>The clock, folded to an int. Nothing here is a plan, a
        /// layout or a capture: it is which of twenty insults a stranger
        /// picks, and it is the one number in the street that has to differ
        /// between two evenings.</summary>
        private static void EnsureSalt()
        {
            if (salted)
            {
                return;
            }

            long ticks = DateTime.UtcNow.Ticks;
            salt = unchecked((int)(ticks ^ (ticks >> 32)));
            salted = true;
        }
    }
}
