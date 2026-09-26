using UnityEngine;

namespace TMPTextEffects
{
    public enum Ease
    {
        Linear,
        InQuad,
        OutQuad,
        InOutQuad,
        OutCubic,
        InOutCubic,
        OutBack,
        OutElastic,
        OutBounce,
        /// <summary>Jumps from 0 to 1 at the start — a typewriter.</summary>
        Step,
        /// <summary>Uses the effect's AnimationCurve.</summary>
        Custom,
    }

    public static class Easing
    {
        public static float Evaluate(Ease ease, float t, AnimationCurve custom = null)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return 1 - (1 - t) * (1 - t);
                case Ease.InOutQuad: return t < 0.5f ? 2 * t * t : 1 - Mathf.Pow(-2 * t + 2, 2) / 2;
                case Ease.OutCubic: return 1 - Mathf.Pow(1 - t, 3);
                case Ease.InOutCubic: return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
                case Ease.OutBack:
                {
                    const float c1 = 1.70158f, c3 = c1 + 1;
                    return 1 + c3 * Mathf.Pow(t - 1, 3) + c1 * Mathf.Pow(t - 1, 2);
                }
                case Ease.OutElastic:
                {
                    if (t <= 0 || t >= 1) return t;
                    const float c4 = 2 * Mathf.PI / 3;
                    return Mathf.Pow(2, -10 * t) * Mathf.Sin((t * 10 - 0.75f) * c4) + 1;
                }
                case Ease.OutBounce:
                {
                    const float n1 = 7.5625f, d1 = 2.75f;
                    if (t < 1 / d1) return n1 * t * t;
                    if (t < 2 / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
                    if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
                    t -= 2.625f / d1;
                    return n1 * t * t + 0.984375f;
                }
                case Ease.Step: return t > 0 ? 1 : 0;
                case Ease.Custom: return custom != null && custom.length > 0 ? custom.Evaluate(t) : t;
                default: return t;
            }
        }
    }
}
