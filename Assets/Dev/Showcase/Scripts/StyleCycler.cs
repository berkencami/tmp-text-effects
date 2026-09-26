using TMPro;
using UnityEngine;

namespace TMPTextEffects.Samples
{
    /// <summary>Steps a <see cref="LayeredText"/> through a list of styles with smooth transitions, optionally captioning each step.</summary>
    [RequireComponent(typeof(LayeredText))]
    public class StyleCycler : MonoBehaviour
    {
        [SerializeField] private LayeredTextStyle[] _Styles;
        [SerializeField] private string[] _Captions;
        [SerializeField] private TMP_Text _Caption;
        [SerializeField, Min(0.1f)] private float _Hold = 0.9f;
        [SerializeField, Min(0f)] private float _Transition = 0.25f;

        private LayeredText _text;
        private float _time;
        private int _index;

        private void Start()
        {
            _text = GetComponent<LayeredText>();
            if (_Styles.Length > 0) _text.Style = _Styles[0];
            UpdateCaption();
        }

        private void Update()
        {
            if (_Styles.Length < 2) return;
            _time += Time.deltaTime;
            if (_time < _Hold + _Transition) return;
            _time -= _Hold + _Transition;

            _index = (_index + 1) % _Styles.Length;
            _text.TransitionTo(_Styles[_index], _Transition, Ease.OutQuad, unscaledTime: false);
            UpdateCaption();
        }

        /// <summary>Back to the first style, instantly.</summary>
        public void Restart()
        {
            _time = 0;
            _index = 0;
            if (_Styles.Length > 0) _text.Style = _Styles[0];
            UpdateCaption();
        }

        private void UpdateCaption()
        {
            if (_Caption != null && _index < _Captions.Length) _Caption.text = _Captions[_index];
        }
    }
}
