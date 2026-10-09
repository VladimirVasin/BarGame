namespace BarPromenade
{
    public sealed partial class CombatRagdoll
    {
        /// <summary>A surviving body may wake for an arm stroke without beginning
        /// a new fall, changing its pose or manufacturing another hit.</summary>
        internal bool WakeLivingBody()
        {
            if (!IsActive || !recoverable || hitStopFrozen || physicsController == null) return false;
            if (IsRecovering || IsSettled || physicsController.IsFrozen)
            {
                // A voluntary arm task resumes this same supported pose, rather
                // than beginning another impact. Keep its accepted floor source
                // across duel substeps before PhysX can report contact again.
                bool retainedSupport = !IsRecovering && HasGroundContact && IsStaticSupportSurface(GroundContactSurface);
                UnityEngine.Vector3 point = GroundContactPoint, normal = GroundContactNormal;
                UnityEngine.Collider surface = GroundContactSurface;
                if (!ResumeHeldSimulation()) return false;
                if (retainedSupport)
                {
                    HasGroundContact = true;
                    GroundContactPoint = point;
                    GroundContactNormal = normal;
                    GroundContactSurface = surface;
                }
            }
            return physicsController.IsSimulating;
        }
    }
}
