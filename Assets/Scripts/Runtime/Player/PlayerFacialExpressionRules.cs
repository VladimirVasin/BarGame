namespace BarPromenade
{
    /// <summary>
    /// What one face stands in for when a rig cannot show it: the drink's
    /// four faces are cells the V2 atlas gained late, and a rig or an
    /// atlas without them shows the nearest of the five it always had.
    /// </summary>
    public static class PlayerFacialExpressionRules
    {
        /// <summary>
        /// The nearest older face, or <see cref="PlayerFacialExpression.Neutral"/>
        /// for a face that already is one of the five.
        /// </summary>
        public static PlayerFacialExpression Fallback(PlayerFacialExpression expression)
        {
            switch (expression)
            {
                case PlayerFacialExpression.Drowsy:
                    return PlayerFacialExpression.HalfBlink;
                case PlayerFacialExpression.Grimace:
                    return PlayerFacialExpression.Tense;
                case PlayerFacialExpression.Glazed:
                case PlayerFacialExpression.Slack:
                default:
                    return PlayerFacialExpression.Neutral;
            }
        }

        /// <summary>Whether the face is one of the five every rig can show.</summary>
        public static bool IsCanonical(PlayerFacialExpression expression)
        {
            return expression >= PlayerFacialExpression.Neutral &&
                   expression <= PlayerFacialExpression.Tense;
        }
    }
}
