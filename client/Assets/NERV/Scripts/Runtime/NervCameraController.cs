using UnityEngine;

namespace Nerv.Client
{
    /// <summary>
    /// Камера-«орбита» вокруг центра сцены:
    /// правая кнопка (или Alt + левая) — вращение, средняя (или Shift + правая) — сдвиг, колесо/трекпад — приближение.
    /// </summary>
    public sealed class NervCameraController : MonoBehaviour
    {
        public Vector3 Target = Vector3.zero;
        public float Distance = 3.2f;
        public float Yaw = 30f, Pitch = 20f;
        public float MinDistance = 0.3f, MaxDistance = 12f;

        private Vector2 _lastPointer;

        private void Start() => Apply();

        private void LateUpdate()
        {
            Vector2 p = NervInput.PointerPosition;
            Vector2 delta = p - _lastPointer;
            _lastPointer = p;

            bool overUi = NervHud.PointerOverUi;
            bool orbit = NervInput.RightHeld && !NervInput.ShiftHeld || (NervInput.AltHeld && NervInput.LeftHeld);
            bool pan = NervInput.MiddleHeld || (NervInput.RightHeld && NervInput.ShiftHeld);

            if (!overUi && orbit)
            {
                Yaw += delta.x * 0.25f;
                Pitch = Mathf.Clamp(Pitch - delta.y * 0.25f, -85f, 85f);
            }
            else if (!overUi && pan)
            {
                float k = Distance * 0.0015f;
                Target -= transform.right * (delta.x * k) + transform.up * (delta.y * k);
            }

            float scroll = overUi ? 0f : NervInput.Scroll;
            if (Mathf.Abs(scroll) > 1e-4f)
                Distance = Mathf.Clamp(Distance * Mathf.Pow(0.88f, scroll), MinDistance, MaxDistance);

            Apply();
        }

        private void Apply()
        {
            Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.SetPositionAndRotation(Target - rot * Vector3.forward * Distance, rot);
        }
    }
}
