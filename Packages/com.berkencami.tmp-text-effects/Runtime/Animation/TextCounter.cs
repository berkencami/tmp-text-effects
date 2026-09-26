using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace TMPTextEffects
{
    /// <summary>
    /// Counts a number up/down in a TMP text ("0" → "1,234,567"). Plays itself via <see cref="CountTo"/>, or drive
    /// <see cref="Value"/> from a tween / Animator. Works alongside <see cref="TextAnimator"/> and <see cref="LayeredText"/>.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Text Counter")]
    public class TextCounter : MonoBehaviour
    {
        [Tooltip("Displayed value. Animatable from Animator/Timeline or tweens.")]
        [SerializeField] private double _Value;

        [Tooltip(".NET numeric format: N0 = 1,234,567 · N2 = 1,234.57 · F0 = 1234567 · P0 = 45%")]
        [SerializeField] private string _Format = "N0";
        [SerializeField] private string _Prefix = "";
        [SerializeField] private string _Suffix = "";
        [Tooltip("Off: invariant separators (1,234.5). On: the device culture (e.g. 1.234,5 in Turkish).")]
        [SerializeField] private bool _UseCurrentCulture;

        [Header("Self-play")]
        [SerializeField] private bool _PlayOnEnable;
        [SerializeField] private double _From;
        [SerializeField] private double _To = 1234567;
        [SerializeField, Min(0.01f)] private float _Duration = 1.5f;
        [SerializeField] private Ease _Ease = Ease.OutCubic;
        [SerializeField, Min(0f)] private float _Delay;
        [SerializeField] private bool _UnscaledTime = true;

        private TMP_Text _tmp;
        private double _shown = double.NaN;
        private double _tweenFrom, _tweenTo;
        private float _elapsed, _tweenDuration;
        private bool _playing;

        public event Action Completed;

        public double Value
        {
            get => _Value;
            set
            {
                _Value = value;
                Refresh();
            }
        }

        public bool IsPlaying => _playing;

        /// <summary>On (default): ignores Time.timeScale.</summary>
        public bool UnscaledTime { get => _UnscaledTime; set => _UnscaledTime = value; }

        /// <summary>Animates from the current value to <paramref name="target"/>.</summary>
        public void CountTo(double target, float duration = -1)
        {
            _tweenFrom = _Value;
            _tweenTo = target;
            _tweenDuration = duration > 0 ? duration : _Duration;
            _elapsed = -_Delay;
            _playing = true;
        }

        public void CountFromTo(double from, double to, float duration = -1)
        {
            Value = from;
            CountTo(to, duration);
        }

        public void Stop() => _playing = false;

        private void OnEnable()
        {
            _tmp = GetComponent<TMP_Text>();
            _shown = double.NaN;
            if (_PlayOnEnable && Application.isPlaying) CountFromTo(_From, _To);
            else Refresh();
        }

        private void OnValidate()
        {
            _shown = double.NaN;
            if (isActiveAndEnabled) Refresh();
        }

        private void Update()
        {
            if (_playing && Application.isPlaying)
            {
                _elapsed += _UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                if (_elapsed >= 0)
                {
                    float t = Mathf.Clamp01(_elapsed / _tweenDuration);
                    _Value = _tweenFrom + (_tweenTo - _tweenFrom) * Easing.Evaluate(_Ease, t);
                    if (t >= 1)
                    {
                        _Value = _tweenTo;
                        _playing = false;
                        Refresh();
                        Completed?.Invoke();
                        return;
                    }
                }
            }
            Refresh(); // also picks up Animator/Timeline writes to _Value
        }

        private void Refresh()
        {
            if (_tmp == null) _tmp = GetComponent<TMP_Text>();
            if (_tmp == null || _Value.Equals(_shown)) return;
            _shown = _Value;

            var culture = _UseCurrentCulture ? CultureInfo.CurrentCulture : CultureInfo.InvariantCulture;
            string format = string.IsNullOrEmpty(_Format) ? "N0" : _Format;

            // Whole-number formats go through a reused char buffer: counting every frame allocates nothing.
            if (TryWriteInteger(format, culture)) return;

            string number;
            try { number = _Value.ToString(format, culture); }
            catch (FormatException) { number = _Value.ToString("N0", culture); }
            _tmp.text = _Prefix + number + _Suffix;
        }

        private char[] _chars = new char[64];

        private bool TryWriteInteger(string format, CultureInfo culture)
        {
            bool grouped;
            if (format == "N0") grouped = true;
            else if (format == "F0" || format == "D" || format == "0") grouped = false;
            else return false;

            double rounded = Math.Round(_Value, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded) >= long.MaxValue) return false;
            long value = (long)rounded;

            string separator = culture.NumberFormat.NumberGroupSeparator;
            string negative = culture.NumberFormat.NegativeSign;
            int needed = _Prefix.Length + _Suffix.Length + negative.Length + 20 * (1 + separator.Length);
            if (_chars.Length < needed) _chars = new char[needed];

            int n = 0;
            n = Append(_Prefix, n);
            if (value < 0) n = Append(negative, n);

            // Digits right to left into the tail of the buffer, then copy forward.
            ulong magnitude = value < 0 ? (ulong)(-(value + 1)) + 1 : (ulong)value;
            int tail = _chars.Length;
            int digits = 0;
            do
            {
                if (grouped && digits > 0 && digits % 3 == 0)
                {
                    for (int s = separator.Length - 1; s >= 0; s--) _chars[--tail] = separator[s];
                }
                _chars[--tail] = (char)('0' + (int)(magnitude % 10));
                magnitude /= 10;
                digits++;
            } while (magnitude > 0);

            int length = _chars.Length - tail;
            Array.Copy(_chars, tail, _chars, n, length);
            n += length;
            n = Append(_Suffix, n);

            _tmp.SetCharArray(_chars, 0, n);
            return true;
        }

        private int Append(string s, int at)
        {
            s.CopyTo(0, _chars, at, s.Length);
            return at + s.Length;
        }
    }
}
