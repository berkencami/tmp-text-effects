using UnityEngine;

namespace TMPTextEffects
{
    /// <summary>Ready-made animator setups. Each replaces the animator's effects and timing.</summary>
    public static class TextAnimatorPresets
    {
        /// <summary>Characters scale up from nothing with an overshoot, one after another.</summary>
        public static void PopIn(TextAnimator a)
        {
            a.Effects.Clear();
            // Scale only: fading a layered text makes each layer translucent separately, so lower layers
            // (outlines) show through the face mid-fade.
            a.AddEffect(new PopEffect());
            a.CharDuration = 0.35f;
            a.Stagger = 0.05f;
            a.Order = StaggerOrder.LeftToRight;
        }

        /// <summary>Characters appear instantly in reading order.</summary>
        public static void Typewriter(TextAnimator a)
        {
            a.Effects.Clear();
            a.AddEffect(new FadeEffect { Ease = Ease.Step });
            a.CharDuration = 0.01f;
            a.Stagger = 0.045f;
            a.Order = StaggerOrder.LeftToRight;
        }

        public static void SlideUp(TextAnimator a)
        {
            a.Effects.Clear();
            a.AddEffect(new SlideEffect { From = new Vector2(0, -0.6f), Ease = Ease.OutBack });
            a.AddEffect(new FadeEffect { Ease = Ease.OutQuad });
            a.CharDuration = 0.45f;
            a.Stagger = 0.04f;
            a.Order = StaggerOrder.LeftToRight;
        }

        public static void SpinIn(TextAnimator a)
        {
            a.Effects.Clear();
            a.AddEffect(new RotateEffect { FromAngle = -180 });
            a.AddEffect(new PopEffect { Ease = Ease.OutBack });
            a.CharDuration = 0.5f;
            a.Stagger = 0.04f;
            a.Order = StaggerOrder.CenterOut;
        }

        /// <summary>A continuous wave through the glyphs.</summary>
        public static void Wave(TextAnimator a)
        {
            a.Effects.Clear();
            a.AddEffect(new WaveEffect());
        }

        /// <summary>Cycling hues through the glyphs.</summary>
        public static void Rainbow(TextAnimator a)
        {
            a.Effects.Clear();
            a.AddEffect(new RainbowEffect());
        }

        /// <summary>Adds a recurring shine sweep on top of the current effects (LayeredText only).</summary>
        public static void AddShine(TextAnimator a)
        {
            a.Effects.RemoveAll(e => e is ShineEffect);
            a.AddEffect(new ShineEffect());
        }

        public static void Shake(TextAnimator a)
        {
            a.Effects.Clear();
            a.AddEffect(new ShakeEffect());
        }
    }
}
