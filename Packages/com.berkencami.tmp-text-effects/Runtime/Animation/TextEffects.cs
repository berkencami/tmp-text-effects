using System;
using UnityEngine;

namespace TMPTextEffects
{
    /// <summary>
    /// Per-character transform an effect writes into. Applied around the character's baseline centre:
    /// scale, then rotation, then offset. Offsets are in em (1 = the character's font size).
    /// </summary>
    public struct CharacterState
    {
        public Vector2 Offset;
        public Vector2 Scale;
        public float Rotation; // degrees, counter-clockwise
        public float Alpha;
        /// <summary>Shine sweep position across the gradient box (0..1, may overshoot); NaN = no shine. LayeredText only.</summary>
        public float Shine;
        /// <summary>Multiplies the text colour (plain TMP, and LayeredText faces tinted by the text colour).</summary>
        public Color Tint;
        /// <summary>0..1 blend of the face towards white.</summary>
        public float Flash;

        public static CharacterState Identity => new() { Scale = Vector2.one, Alpha = 1, Shine = float.NaN, Tint = Color.white };

        public bool IsIdentity =>
            Offset == Vector2.zero && Scale == Vector2.one && Rotation == 0 && Mathf.Approximately(Alpha, 1) &&
            float.IsNaN(Shine) && Tint == Color.white && Flash <= 0;
    }

    public struct EffectContext
    {
        /// <summary>This character's own 0..1 transition progress (after stagger).</summary>
        public float Progress;
        /// <summary>Seconds since the animator started; drives looping effects.</summary>
        public float Time;
        /// <summary>Position of the character in the stagger order (visible characters only).</summary>
        public int Index;
        /// <summary>Position among visible characters in reading order — use for spatial patterns (waves).</summary>
        public int TextIndex;
        public int Count;
    }

    [Serializable]
    public abstract class TextEffect
    {
        public bool Enabled = true;

        /// <summary>Transition effects animate from a "hidden" state to rest as progress goes 0 → 1.</summary>
        public virtual bool IsTransition => true;

        public abstract void Apply(ref CharacterState state, in EffectContext ctx);

        public virtual string DisplayName => GetType().Name.Replace("Effect", "");
    }

    [Serializable]
    public abstract class TransitionEffect : TextEffect
    {
        public Ease Ease = Ease.OutCubic;
        public AnimationCurve CustomCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        protected float Eased(in EffectContext ctx) => Easing.Evaluate(Ease, ctx.Progress, CustomCurve);
    }

    [Serializable]
    public class PopEffect : TransitionEffect
    {
        [Tooltip("Scale the character starts from.")]
        public Vector2 FromScale = Vector2.zero;

        public PopEffect() => Ease = Ease.OutBack;

        public override void Apply(ref CharacterState s, in EffectContext ctx)
        {
            float e = Eased(ctx);
            s.Scale *= new Vector2(Mathf.LerpUnclamped(FromScale.x, 1, e), Mathf.LerpUnclamped(FromScale.y, 1, e));
        }
    }

    [Serializable]
    public class FadeEffect : TransitionEffect
    {
        public FadeEffect() => Ease = Ease.Linear;

        public override void Apply(ref CharacterState s, in EffectContext ctx) => s.Alpha *= Mathf.Clamp01(Eased(ctx));
    }

    [Serializable]
    public class SlideEffect : TransitionEffect
    {
        [Tooltip("Offset the character starts from, in em.")]
        public Vector2 From = new(0, -0.6f);

        public override void Apply(ref CharacterState s, in EffectContext ctx) =>
            s.Offset += From * (1 - Eased(ctx));
    }

    [Serializable]
    public class RotateEffect : TransitionEffect
    {
        [Tooltip("Angle the character starts from, in degrees.")]
        public float FromAngle = -90;

        public RotateEffect() => Ease = Ease.OutBack;

        public override void Apply(ref CharacterState s, in EffectContext ctx) =>
            s.Rotation += FromAngle * (1 - Eased(ctx));
    }

    [Serializable]
    public class WaveEffect : TextEffect
    {
        [Tooltip("Height in em.")]
        public float Amplitude = 0.12f;
        [Tooltip("Waves per second.")]
        public float Frequency = 1f;
        [Tooltip("Phase step between neighbouring characters, in degrees.")]
        public float PhasePerChar = 35f;

        public override bool IsTransition => false;

        public override void Apply(ref CharacterState s, in EffectContext ctx)
        {
            float phase = 2 * Mathf.PI * Frequency * ctx.Time - ctx.TextIndex * PhasePerChar * Mathf.Deg2Rad;
            s.Offset.y += Amplitude * Mathf.Sin(phase);
        }
    }

    [Serializable]
    public class ShakeEffect : TextEffect
    {
        [Tooltip("Offset in em.")]
        public float Amplitude = 0.04f;
        [Tooltip("Jitter angle in degrees.")]
        public float Angle = 4f;
        public float Speed = 18f;

        public override bool IsTransition => false;

        public override void Apply(ref CharacterState s, in EffectContext ctx)
        {
            float t = ctx.Time * Speed;
            float seed = ctx.TextIndex * 13.37f;
            s.Offset += new Vector2(Mathf.PerlinNoise(t, seed) - 0.5f, Mathf.PerlinNoise(seed, t) - 0.5f) * (2 * Amplitude);
            s.Rotation += (Mathf.PerlinNoise(t + 100, seed) - 0.5f) * 2 * Angle;
        }
    }

    /// <summary>Characters flash white as they appear, then settle to their colour.</summary>
    [Serializable]
    public class FlashEffect : TransitionEffect
    {
        [Tooltip("Starting whiteness (1 = pure white).")]
        [Range(0f, 1f)] public float Strength = 1f;

        public FlashEffect() => Ease = Ease.OutQuad;

        public override void Apply(ref CharacterState s, in EffectContext ctx) =>
            s.Flash = Mathf.Max(s.Flash, Strength * (1 - Mathf.Clamp01(Eased(ctx))));
    }

    /// <summary>Characters appear in a colour and fade to their own.</summary>
    [Serializable]
    public class ColorFadeEffect : TransitionEffect
    {
        public Color From = new(1f, 0.3f, 0.3f, 1f);

        public override void Apply(ref CharacterState s, in EffectContext ctx) =>
            s.Tint *= Color.LerpUnclamped(From, Color.white, Mathf.Clamp01(Eased(ctx)));
    }

    /// <summary>Cycles each character's hue over time, offset along the text.</summary>
    [Serializable]
    public class RainbowEffect : TextEffect
    {
        [Tooltip("Hue cycles per second.")]
        public float Speed = 0.35f;
        [Tooltip("Hue step between neighbouring characters (1 = a full cycle).")]
        public float HuePerChar = 0.07f;
        [Range(0f, 1f)] public float Saturation = 0.75f;
        [Tooltip("0 = original colour, 1 = full rainbow.")]
        [Range(0f, 1f)] public float Strength = 1f;

        public override bool IsTransition => false;

        public override void Apply(ref CharacterState s, in EffectContext ctx)
        {
            float hue = Mathf.Repeat(ctx.Time * Speed + ctx.TextIndex * HuePerChar, 1f);
            s.Tint *= Color.Lerp(Color.white, Color.HSVToRGB(hue, Saturation, 1f), Strength);
        }
    }

    /// <summary>
    /// Sweeps the style's shine band across the text, then waits. Colour, width and angle come from the Face layer
    /// of the LayeredText style (plain TMP texts have no shine).
    /// </summary>
    [Serializable]
    public class ShineEffect : TextEffect
    {
        [Tooltip("Seconds one sweep takes.")]
        [Min(0.01f)] public float Duration = 0.6f;
        [Tooltip("Seconds between sweeps.")]
        [Min(0f)] public float Interval = 1.8f;
        [Tooltip("Seconds before the first sweep.")]
        [Min(0f)] public float StartDelay = 0.3f;
        public Ease Ease = Ease.InOutQuad;

        public override bool IsTransition => false;

        public override void Apply(ref CharacterState s, in EffectContext ctx)
        {
            float t = ctx.Time - StartDelay;
            if (t < 0) return;
            float phase = (t % (Duration + Interval)) / Duration;
            if (phase > 1) return;
            // Start and end fully outside the box so the band enters and leaves cleanly.
            s.Shine = Mathf.LerpUnclamped(-0.35f, 1.35f, Easing.Evaluate(Ease, phase));
        }
    }

    [Serializable]
    public class PulseEffect : TextEffect
    {
        [Tooltip("Extra scale at the peak (0.1 = 10% larger).")]
        public float Amount = 0.08f;
        [Tooltip("Pulses per second.")]
        public float Frequency = 1.5f;
        [Tooltip("Phase step between neighbouring characters, in degrees.")]
        public float PhasePerChar = 25f;

        public override bool IsTransition => false;

        public override void Apply(ref CharacterState s, in EffectContext ctx)
        {
            float phase = 2 * Mathf.PI * Frequency * ctx.Time - ctx.TextIndex * PhasePerChar * Mathf.Deg2Rad;
            s.Scale *= 1 + Amount * (0.5f + 0.5f * Mathf.Sin(phase));
        }
    }
}
