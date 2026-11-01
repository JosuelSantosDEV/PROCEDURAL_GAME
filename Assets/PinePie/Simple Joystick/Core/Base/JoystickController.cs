using System;
using UnityEngine;
using UnityEngine.EventSystems;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PinePie.SimpleJoystick
{
    public enum JoystickBaseMode
    {
        Static,
        Dynamic,
        Floating
    }

    [AddComponentMenu("PinePie/Joystick Controller")]
    public class JoystickController : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        [Header("References")]
        public RectTransform joystickBase;
        public RectTransform handle;

        [Header("Settings")]
        public JoystickBaseMode baseMode = JoystickBaseMode.Static;
        public float joystickRange = 55f;
        [Range(0f, 1f)] public float deadZone = 0.1f;

        [Range(0, 16)]
        [Tooltip("Set 0 or 1 for free handle, or set according to number of direction need like 4 or 8")]
        public int directionSnaps = 0;

        [Tooltip("if true, snap handle to center when not using. Use false only when a certain direction is always needed.")]
        public bool snapHandleBack = true;

        [Tooltip("[Alteração local ao pacote] Nos modos Floating/Dynamic, mantém a base visível na posição inicial quando não está em uso, em vez de só aparecer ao tocar.")]
        public bool keepVisible = false;

        private Vector2 inputDirection = Vector2.zero;
        public Vector2 InputDirection => inputDirection;

        public event Action OnTouchPressed;
        public event Action OnTouchRemoved;
        public event Action OnDirectionChanged;

        private Vector2 baseStartPos;
        private Canvas parentCanvas;
        private bool dragStartedInside = false;

        void Awake()
        {
            baseStartPos = joystickBase.anchoredPosition;
            parentCanvas = GetComponentInParent<Canvas>();

            if (baseMode != JoystickBaseMode.Static && !keepVisible) joystickBase.gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnTouchPressed?.Invoke();

            joystickBase.gameObject.SetActive(true);

            if (baseMode == JoystickBaseMode.Floating)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentCanvas.transform as RectTransform,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 newPos
                );
                joystickBase.anchoredPosition = newPos;
            }

            // handle snappping
            if (baseMode != JoystickBaseMode.Static)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        (RectTransform)joystickBase.parent,
                        eventData.position,
                        eventData.pressEventCamera,
                        out Vector2 touchPoint
                    );

                if (snapHandleBack) joystickBase.anchoredPosition = touchPoint;
                else
                {
                    joystickBase.anchoredPosition = touchPoint - (inputDirection * joystickRange);
                    handle.anchoredPosition = inputDirection * joystickRange;
                }
            }


            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        joystickBase, eventData.position, eventData.pressEventCamera, out Vector2 localPoint
                    );
            dragStartedInside = localPoint.magnitude <= joystickRange;

            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (baseMode == JoystickBaseMode.Static && !dragStartedInside) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                joystickBase,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint
            );

            Vector2 clamped = Vector2.ClampMagnitude(localPoint, joystickRange);
            handle.anchoredPosition = clamped;

            Vector2 rawInput = clamped / joystickRange;
            inputDirection = rawInput.magnitude < deadZone ? Vector2.zero : rawInput;

            if (inputDirection != Vector2.zero) OnDirectionChanged?.Invoke();

            // snap to directions
            if (directionSnaps > 1 && inputDirection != Vector2.zero)
            {
                float angle = Vector2.SignedAngle(Vector2.left, inputDirection);
                float snapAngle = 360f / directionSnaps;
                float snappedAngle = Mathf.Round(angle / snapAngle) * snapAngle;

                inputDirection = Quaternion.Euler(0, 0, snappedAngle) * Vector2.left;
                handle.anchoredPosition = inputDirection * joystickRange;
            }

            if (baseMode == JoystickBaseMode.Dynamic && localPoint.magnitude > joystickRange)
            {
                Vector2 offset = localPoint.normalized * (localPoint.magnitude - joystickRange);
                joystickBase.anchoredPosition += offset;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            OnTouchRemoved?.Invoke();

            if (snapHandleBack)
            {
                handle.anchoredPosition = Vector2.zero;
                inputDirection = Vector2.zero;
            }

            if (baseMode == JoystickBaseMode.Floating || baseMode == JoystickBaseMode.Dynamic)
                joystickBase.anchoredPosition = baseStartPos;

            if (baseMode != JoystickBaseMode.Static && !keepVisible) joystickBase.gameObject.SetActive(false);
        }


#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (joystickBase != null)
            {
                Vector3 baseWorldPos = joystickBase.position;

                float angleStep = 360f / directionSnaps;

                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                for (int i = 0; i < directionSnaps; i++)
                {
                    float angle = i * angleStep;
                    Vector2 dir = Quaternion.Euler(0, 0, angle) * Vector2.left;

                    Vector3 start = baseWorldPos + (Vector3)(dir * (joystickRange - 10f));
                    Vector3 end = baseWorldPos + (Vector3)(dir * (joystickRange + 4f));

                    Handles.DrawLine(start, end);
                }

                // handle range
                Color cyanCol = Color.cyan;
                Handles.color = new(cyanCol.r, cyanCol.g, cyanCol.b, 0.03f);
                Handles.DrawSolidDisc(baseWorldPos, Vector3.forward, joystickRange);
                Handles.color = cyanCol;
                Handles.DrawWireDisc(baseWorldPos, Vector3.forward, joystickRange);

                // dead zone
                Handles.color = Color.red;
                Handles.DrawWireDisc(baseWorldPos, Vector3.forward, joystickRange * deadZone);

                // direction output
                Handles.color = Color.white;
                Handles.DrawLine(baseWorldPos, baseWorldPos + ((Vector3)inputDirection * joystickRange));
            }
        }
#endif

    }

}