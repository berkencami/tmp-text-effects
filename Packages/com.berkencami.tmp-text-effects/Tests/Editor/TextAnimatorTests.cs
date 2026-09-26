using System;
using System.Reflection;
using NUnit.Framework;

namespace TMPTextEffects.Tests
{
    public class TextAnimatorTests : TextTestFixture
    {
        private static readonly MethodInfo _tick =
            typeof(TextAnimator).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Tick(TextAnimator animator, float dt) => _tick.Invoke(animator, new object[] { dt });

        [TestCase(Ease.Linear)]
        [TestCase(Ease.InQuad)]
        [TestCase(Ease.OutQuad)]
        [TestCase(Ease.InOutQuad)]
        [TestCase(Ease.OutCubic)]
        [TestCase(Ease.InOutCubic)]
        [TestCase(Ease.OutBack)]
        [TestCase(Ease.OutElastic)]
        [TestCase(Ease.OutBounce)]
        public void Easing_HitsBothEnds(Ease ease)
        {
            Assert.That(Easing.Evaluate(ease, 0), Is.EqualTo(0).Within(1e-4));
            Assert.That(Easing.Evaluate(ease, 1), Is.EqualTo(1).Within(1e-4));
        }

        [Test]
        public void Easing_StepJumpsImmediately()
        {
            Assert.That(Easing.Evaluate(Ease.Step, 0), Is.EqualTo(0));
            Assert.That(Easing.Evaluate(Ease.Step, 0.001f), Is.EqualTo(1));
        }

        [Test]
        public void Duration_LeftToRight_IsStaggerTimesCountPlusCharDuration()
        {
            var tmp = CreateText("abcd");
            var animator = AddAnimator(tmp, 0.3f, 0.1f, StaggerOrder.LeftToRight);
            Regenerate(tmp);
            Assert.That(animator.Duration, Is.EqualTo(0.3f + 3 * 0.1f).Within(1e-4));
        }

        // Regression: Mathf.RoundToInt banker's rounding gave slots 2,0,0,2 for four characters.
        [TestCase("abcd", 1)]
        [TestCase("abcde", 2)]
        [TestCase("abcdef", 2)]
        public void Duration_CenterOut_UsesConsecutiveSlots(string text, int lastSlot)
        {
            var tmp = CreateText(text);
            var animator = AddAnimator(tmp, 0.3f, 0.1f, StaggerOrder.CenterOut);
            Regenerate(tmp);
            Assert.That(animator.Duration, Is.EqualTo(0.3f + lastSlot * 0.1f).Within(1e-4));
        }

        [Test]
        public void SentencePause_DelaysFollowingCharacters()
        {
            var tmp = CreateText("a.b");
            var animator = AddAnimator(tmp, 0.2f, 0.1f, StaggerOrder.LeftToRight);
            animator.SentencePause = 0.5f;
            Regenerate(tmp);
            // a @0, '.' @0.1, b @0.2 + 0.5 → ends at 0.9
            Assert.That(animator.Duration, Is.EqualTo(0.9f).Within(1e-4));
        }

        [Test]
        public void PauseTag_IsStrippedAndDelaysReveal()
        {
            var tmp = CreateText("ab<pause=1>c");
            var animator = AddAnimator(tmp, 0.2f, 0.1f, StaggerOrder.LeftToRight);
            Regenerate(tmp);
            Assert.That(tmp.textInfo.characterCount, Is.EqualTo(3));
            Assert.That(animator.Duration, Is.EqualTo(0.2f + 1f + 0.2f).Within(1e-4));
        }

        [Test]
        public void TagEffect_OnlyAffectsTaggedCharacters()
        {
            var tmp = CreateText("<wave>ab</wave>cd");
            var animator = AddAnimator(tmp, 0.2f, 0.1f, StaggerOrder.LeftToRight);
            Regenerate(tmp);
            Assert.That(tmp.textInfo.characterCount, Is.EqualTo(4), "tags must not render as text");

            Tick(animator, 0.25f); // wave phase away from zero crossing
            Assert.That(animator.TryGetState(0, out var tagged), Is.True);
            Assert.That(tagged.Offset.y, Is.Not.EqualTo(0));
            Assert.That(animator.TryGetState(2, out _), Is.False);
        }

        [Test]
        public void UnknownTags_AreLeftForTmp()
        {
            var tmp = CreateText("<b>ab</b>");
            AddAnimator(tmp, 0.2f, 0.1f, StaggerOrder.LeftToRight);
            Regenerate(tmp);
            Assert.That(tmp.textInfo.characterCount, Is.EqualTo(2));
            Assert.That((tmp.textInfo.characterInfo[0].style & TMPro.FontStyles.Bold) != 0, Is.True);
        }

        [Test]
        public void CharacterRevealed_FiresOncePerVisibleCharacterInOrder()
        {
            var tmp = CreateText("ab c");
            var animator = AddAnimator(tmp, 0.1f, 0.1f, StaggerOrder.LeftToRight);
            Regenerate(tmp);

            var revealed = new System.Collections.Generic.List<char>();
            animator.CharacterRevealed += (_, c) => revealed.Add(c);
            animator.Play();
            for (int i = 0; i < 40; i++) Tick(animator, 0.02f);

            Assert.That(revealed, Is.EqualTo(new[] { 'a', 'b', 'c' }));
        }

        [Test]
        public void Complete_JumpsToEndWithoutRaisingReveals()
        {
            var tmp = CreateText("abc");
            var animator = AddAnimator(tmp, 0.1f, 0.1f, StaggerOrder.LeftToRight);
            Regenerate(tmp);

            int count = 0;
            animator.CharacterRevealed += (_, _) => count++;
            animator.Play();
            animator.Complete();
            Tick(animator, 0.02f);

            Assert.That(animator.Progress, Is.EqualTo(1));
            Assert.That(animator.IsPlaying, Is.False);
            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void Completed_IsRaisedAtTheEndOfAOneShotPlay()
        {
            var tmp = CreateText("ab");
            var animator = AddAnimator(tmp, 0.1f, 0.1f, StaggerOrder.LeftToRight);
            Regenerate(tmp);

            bool completed = false;
            animator.Completed += () => completed = true;
            animator.Play();
            for (int i = 0; i < 30 && !completed; i++) Tick(animator, 0.02f);

            Assert.That(completed, Is.True);
        }

        [Test]
        public void PopAtProgressZero_HidesCharactersByScale()
        {
            var tmp = CreateText("a");
            var animator = AddAnimator(tmp, 0.2f, 0.1f, StaggerOrder.LeftToRight);
            animator.AddEffect(new PopEffect());
            Regenerate(tmp);

            animator.Progress = 0;
            animator.TryGetState(0, out var hidden);
            animator.Progress = 1;
            animator.TryGetState(0, out var shown);

            Assert.That(hidden.Scale, Is.EqualTo(UnityEngine.Vector2.zero));
            Assert.That(shown.Scale.x, Is.EqualTo(1).Within(1e-4));
        }
    }
}
