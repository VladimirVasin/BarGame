using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Identifies a bar building and owns its hanging sign.
    /// <para>
    /// The sign used to be a camera-facing pixel sprite. That made it the one
    /// piece of a bar facade that did not sit in the world: it turned with the
    /// viewer while the bracket it hung from did not, so the two came apart at
    /// any oblique angle, and it kept its size and facing from the balcony
    /// where every other surface foreshortened. It is now a real blade sign
    /// built from the same shared-material boxes as the rest of the facade,
    /// hanging under the same bracket arm and reading along the street the way
    /// a projecting sign does.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarBuildingMarker : MonoBehaviour
    {
        [SerializeField] private string barId = string.Empty;
        [SerializeField] private Renderer panelRenderer;
        [SerializeField] private List<Renderer> signRenderers =
            new List<Renderer>();

        public string BarId => barId;

        /// <summary>
        /// The sign's dark backing plate, whose shared material every bar
        /// sign in the world is expected to have in common.
        /// </summary>
        public Renderer PanelRenderer => panelRenderer;

        public IReadOnlyList<Renderer> SignRenderers => signRenderers;

        public void Initialize(string id)
        {
            barId = id ?? string.Empty;
        }

        /// <summary>
        /// Records geometry placed beneath this marker. The builder marks the
        /// actual backing panel explicitly because generated bindings are
        /// sorted by source name and a hanger may otherwise arrive first.
        /// </summary>
        public void RegisterSignPart(
            GameObject part,
            bool isPanel = false)
        {
            if (part == null)
            {
                return;
            }

            Renderer partRenderer = part.GetComponent<Renderer>();
            if (partRenderer == null)
            {
                return;
            }

            signRenderers.Add(partRenderer);
            if (isPanel || panelRenderer == null)
            {
                panelRenderer = partRenderer;
            }
        }
    }
}
