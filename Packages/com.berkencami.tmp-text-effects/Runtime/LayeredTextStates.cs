using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TMPTextEffects
{
    /// <summary>
    /// Button-style states for a <see cref="LayeredText"/>: transitions its style on hover, press and disable.
    /// Put it on the object that receives the pointer (usually the Button); empty states fall back to Normal.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Layered Text States")]
    public class LayeredTextStates : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("The text to restyle. Defaults to the first LayeredText in children.")]
        [SerializeField] private LayeredText _Target;

        [Tooltip("Optional: disabled while this selectable is not interactable.")]
        [SerializeField] private Selectable _Selectable;

        [SerializeField] private LayeredTextStyle _Normal;
        [SerializeField] private LayeredTextStyle _Highlighted;
        [SerializeField] private LayeredTextStyle _Pressed;
        [SerializeField] private LayeredTextStyle _Disabled;

        [SerializeField, Min(0f)] private float _Duration = 0.12f;
        [SerializeField] private Ease _Ease = Ease.OutQuad;

        private bool _hovered;
        private bool _pressed;
        private bool _wasInteractable = true;

        public LayeredText Target => _Target;

        private void Reset()
        {
            _Target = GetComponentInChildren<LayeredText>();
            _Selectable = GetComponent<Selectable>();
            if (_Target != null) _Normal = _Target.Style;
        }

        private void Awake()
        {
            if (_Target == null) _Target = GetComponentInChildren<LayeredText>();
        }

        private void OnEnable()
        {
            _hovered = _pressed = false;
            _wasInteractable = IsInteractable;
            Refresh(instant: true);
        }

        private void Update()
        {
            bool interactable = IsInteractable;
            if (interactable == _wasInteractable) return;
            _wasInteractable = interactable;
            Refresh(instant: false);
        }

        public void OnPointerEnter(PointerEventData eventData) { _hovered = true; Refresh(false); }
        public void OnPointerExit(PointerEventData eventData) { _hovered = false; Refresh(false); }
        public void OnPointerDown(PointerEventData eventData) { _pressed = true; Refresh(false); }
        public void OnPointerUp(PointerEventData eventData) { _pressed = false; Refresh(false); }

        private bool IsInteractable => _Selectable == null || _Selectable.IsInteractable();

        private LayeredTextStyle Current
        {
            get
            {
                LayeredTextStyle style = null;
                if (!IsInteractable) style = _Disabled;
                else if (_pressed) style = _Pressed;
                else if (_hovered) style = _Highlighted;
                return style != null ? style : _Normal;
            }
        }

        private void Refresh(bool instant)
        {
            var style = Current;
            if (_Target == null || style == null || _Target.Style == style && !_Target.IsTransitioning) return;
            if (instant) _Target.Style = style;
            else _Target.TransitionTo(style, _Duration, _Ease);
        }
    }
}
