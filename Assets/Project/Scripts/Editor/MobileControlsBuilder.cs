using PinePie.SimpleJoystick;
using PROCEDURAL_GAME.CameraControl;
using PROCEDURAL_GAME.MobileInput;
using PROCEDURAL_GAME.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Monta na cena aberta o Canvas com os controles de toque (joystick, arrastar para olhar, botões),
    /// o EventSystem do novo Input System e liga tudo ao Player e à câmera.
    /// Pode ser executado de novo: recria o Canvas "MobileControls" com o layout definido aqui.
    /// </summary>
    public static class MobileControlsBuilder
    {
        private const string SpritesFolder = "Assets/PinePie/Simple Joystick/Core/sprites/";
        private const string UndoName = "Create Mobile Controls";

        // Layout pensado para a tela de referência do CanvasScaler (paisagem).
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        // Centro do joystick em repouso, em unidades do Canvas contadas a partir do canto inferior esquerdo da tela.
        private static readonly Vector2 JoystickRestPosition = new Vector2(300f, 300f);
        private static readonly Color ButtonFill = new Color(1f, 1f, 1f, 0.3f);

        [MenuItem("PROCEDURAL_GAME/Create Mobile Controls")]
        public static void Build()
        {
            MobileControls existing = Object.FindObjectOfType<MobileControls>();
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            EnsureEventSystem();

            Sprite ring = LoadSprite("joystick Circle.png");
            Sprite thumbSprite = LoadSprite("joystick Thumb.png");
            Sprite fill = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            RectTransform canvas = CreateCanvas();
            MobileControls controls = canvas.GetComponent<MobileControls>();

            // Esquerda: joystick flutuante (aparece onde o dedo toca).
            JoystickController joystick = CreateJoystick(canvas, ring, thumbSprite);

            // Direita: arrastar para olhar. Criado antes dos botões para ficar atrás deles.
            TouchLookArea lookArea = CreateLookArea(canvas);

            TouchButton jump = CreateButton(canvas, "JumpButton", "Pular", new Vector2(1f, 0f), new Vector2(-200f, 200f), 200f, fill, ring);
            TouchButton sprint = CreateButton(canvas, "SprintButton", "Correr", new Vector2(1f, 0f), new Vector2(-450f, 130f), 150f, fill, ring);
            TouchButton toggleCamera = CreateButton(canvas, "ToggleCameraButton", "Câmera", new Vector2(1f, 1f), new Vector2(-110f, -110f), 110f, fill, ring);

            SerializedObject controlsSo = new SerializedObject(controls);
            controlsSo.FindProperty("joystick").objectReferenceValue = joystick;
            controlsSo.FindProperty("lookArea").objectReferenceValue = lookArea;
            controlsSo.FindProperty("jumpButton").objectReferenceValue = jump;
            controlsSo.FindProperty("sprintButton").objectReferenceValue = sprint;
            controlsSo.FindProperty("toggleCameraButton").objectReferenceValue = toggleCamera;
            controlsSo.ApplyModifiedPropertiesWithoutUndo();

            LinkToPlayerAndCamera(controls);

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = canvas.gameObject;
            Debug.Log("Controles de toque criados. Salve a cena (Ctrl+S).");
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindObjectOfType<EventSystem>();

            if (eventSystem == null)
            {
                GameObject go = new GameObject("EventSystem", typeof(EventSystem));
                Undo.RegisterCreatedObjectUndo(go, UndoName);
                eventSystem = go.GetComponent<EventSystem>();
            }

            // O projeto usa só o novo Input System: o módulo antigo (StandaloneInputModule) dá erro ao dar Play.
            StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacyModule != null) Undo.DestroyObjectImmediate(legacyModule);

            // O Reset do componente (executado ao adicionar no Editor) já atribui o asset de actions padrão do pacote.
            InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = Undo.AddComponent<InputSystemUIInputModule>(eventSystem.gameObject);

            if (module.actionsAsset == null)
            {
                Debug.LogWarning("EventSystem: o 'Actions Asset' do Input System UI Input Module está vazio. Selecione o EventSystem e use 'Assign Default Actions' no Inspector.", module);
            }

            // Padrão é 10 px: o arrasto da câmera só começaria depois de 10 px de dedo.
            Undo.RecordObject(eventSystem, UndoName);
            eventSystem.pixelDragThreshold = 2;
        }

        private static RectTransform CreateCanvas()
        {
            GameObject go = new GameObject("MobileControls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MobileControls));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            go.layer = LayerMask.NameToLayer("UI");

            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return (RectTransform)go.transform;
        }

        private static JoystickController CreateJoystick(RectTransform canvas, Sprite ring, Sprite thumbSprite)
        {
            // Zona de toque: metade esquerda da tela.
            // O JoystickController (modo Floating) posiciona a base assumindo que o pivô da zona é o mesmo ponto
            // em que a base está ancorada. Usamos o canto inferior esquerdo nos dois: assim a posição inicial é
            // "X pixels do canto" em qualquer proporção de tela.
            RectTransform zone = CreateRect("Joystick", canvas);
            Stretch(zone, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
            zone.pivot = Vector2.zero;
            AddImage(zone, null, new Color(1f, 1f, 1f, 0f), raycastTarget: true);

            RectTransform joystickBase = CreateRect("Base", zone);
            joystickBase.anchorMin = Vector2.zero;
            joystickBase.anchorMax = Vector2.zero;
            joystickBase.anchoredPosition = JoystickRestPosition;
            joystickBase.sizeDelta = new Vector2(260f, 260f);
            AddImage(joystickBase, ring, new Color(1f, 1f, 1f, 0.6f), raycastTarget: false);

            RectTransform handle = CreateRect("Handle", joystickBase);
            handle.sizeDelta = new Vector2(120f, 120f);
            AddImage(handle, thumbSprite, new Color(1f, 1f, 1f, 0.8f), raycastTarget: false);

            JoystickController joystick = zone.gameObject.AddComponent<JoystickController>();
            joystick.joystickBase = joystickBase;
            joystick.handle = handle;
            joystick.baseMode = JoystickBaseMode.Floating;
            joystick.joystickRange = 110f;
            joystick.deadZone = 0.1f;
            joystick.directionSnaps = 0;
            joystick.snapHandleBack = true;
            joystick.keepVisible = true;

            return joystick;
        }

        private static TouchLookArea CreateLookArea(RectTransform canvas)
        {
            RectTransform area = CreateRect("LookArea", canvas);
            Stretch(area, new Vector2(0.5f, 0f), new Vector2(1f, 1f));
            AddImage(area, null, new Color(1f, 1f, 1f, 0f), raycastTarget: true);
            return area.gameObject.AddComponent<TouchLookArea>();
        }

        private static TouchButton CreateButton(RectTransform canvas, string objectName, string label, Vector2 anchor, Vector2 position, float size, Sprite fill, Sprite ring)
        {
            RectTransform button = CreateRect(objectName, canvas);
            button.anchorMin = anchor;
            button.anchorMax = anchor;
            button.anchoredPosition = position;
            button.sizeDelta = new Vector2(size, size);
            AddImage(button, fill, ButtonFill, raycastTarget: true);

            RectTransform border = CreateRect("Ring", button);
            Stretch(border, Vector2.zero, Vector2.one);
            AddImage(border, ring, new Color(1f, 1f, 1f, 0.8f), raycastTarget: false);

            RectTransform text = CreateRect("Label", button);
            Stretch(text, Vector2.zero, Vector2.one);
            Text labelText = text.gameObject.AddComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.text = label;
            labelText.fontSize = Mathf.RoundToInt(size * 0.2f);
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelText.verticalOverflow = VerticalWrapMode.Overflow;
            labelText.color = Color.white;
            labelText.raycastTarget = false;

            return button.gameObject.AddComponent<TouchButton>();
        }

        private static void LinkToPlayerAndCamera(MobileControls controls)
        {
            PlayerInputReader reader = Object.FindObjectOfType<PlayerInputReader>();
            if (reader != null)
            {
                SerializedObject readerSo = new SerializedObject(reader);
                readerSo.FindProperty("mobileControls").objectReferenceValue = controls;
                readerSo.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("Nenhum PlayerInputReader na cena: ligue o campo 'Mobile Controls' manualmente.");
            }

            // Com o cursor travado (modo PC) o clique do mouse não alcança a UI; desligamos para poder testar o toque com o mouse.
            PlayerCameraController camera = Object.FindObjectOfType<PlayerCameraController>();
            if (camera != null)
            {
                SerializedObject cameraSo = new SerializedObject(camera);
                cameraSo.FindProperty("lockCursor").boolValue = false;
                cameraSo.ApplyModifiedProperties();
            }
        }

        private static Sprite LoadSprite(string fileName)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritesFolder + fileName);
            if (sprite == null) Debug.LogWarning($"Sprite não encontrado: {SpritesFolder}{fileName}");
            return sprite;
        }

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void AddImage(RectTransform rect, Sprite sprite, Color color, bool raycastTarget)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycastTarget;
        }
    }
}
