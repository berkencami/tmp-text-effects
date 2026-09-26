using UnityEngine;

namespace TMPTextEffects.Samples
{
    /// <summary>Swings the camera left and right around a point, always looking at it.</summary>
    public class CameraOrbit : MonoBehaviour
    {
        [SerializeField] private Vector3 _Target = Vector3.zero;
        [SerializeField, Min(0.1f)] private float _Distance = 8f;
        [SerializeField] private float _Height = 0.8f;
        [SerializeField] private float _Angle = 28f;
        [Tooltip("Seconds for one full left-right-left swing.")]
        [SerializeField, Min(0.1f)] private float _Period = 6f;

        private float _time;

        public void Restart() => _time = 0;

        private void LateUpdate()
        {
            _time += Time.deltaTime;
            float yaw = Mathf.Sin(_time / _Period * 2 * Mathf.PI) * _Angle;
            var offset = Quaternion.Euler(0, yaw, 0) * new Vector3(0, _Height, -_Distance);
            transform.position = _Target + offset;
            transform.LookAt(_Target);
        }
    }
}
