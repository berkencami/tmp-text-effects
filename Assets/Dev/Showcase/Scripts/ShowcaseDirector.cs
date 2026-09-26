using UnityEngine;

namespace TMPTextEffects.Samples
{
    /// <summary>
    /// Replays every animator and counter in the scene on a fixed period: play at 0, hide at <see cref="_HideAt"/>.
    /// Keeps showcase scenes looping seamlessly (a recording of one period loops perfectly).
    /// </summary>
    public class ShowcaseDirector : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _Period = 4f;
        [Tooltip("Seconds into the period when the texts animate out. 0 = never.")]
        [SerializeField, Min(0f)] private float _HideAt = 3.2f;
        [SerializeField] private double _CounterTarget = 1234567;

        private TextAnimator[] _animators;
        private TextCounter[] _counters;
        private float _time;
        private bool _hidden;

        private void Start()
        {
            _animators = FindObjectsByType<TextAnimator>(FindObjectsSortMode.None);
            _counters = FindObjectsByType<TextCounter>(FindObjectsSortMode.None);
            foreach (var a in _animators)
            {
                a.PlayOnEnable = false;
                a.Loop = LoopMode.Once;
                a.UnscaledTime = false; // follow Time.captureFramerate while recording
            }
            foreach (var c in _counters) c.UnscaledTime = false;
            Restart();
        }

        private void Update()
        {
            _time += Time.deltaTime;
            if (!_hidden && _HideAt > 0 && _time >= _HideAt)
            {
                _hidden = true;
                foreach (var a in _animators) a.Hide();
            }
            if (_time >= _Period) Restart();
        }

        /// <summary>Starts a new period now (the capture tool calls this so recordings begin at the top of a loop).</summary>
        public void Restart()
        {
            _time = 0;
            _hidden = false;
            foreach (var a in _animators) a.Play();
            foreach (var c in _counters) c.CountFromTo(0, _CounterTarget);
        }
    }
}
