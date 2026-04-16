using UnityEngine;
using UnityEngine.UIElements;


public class TornadoPanelUI : MonoBehaviour
{
    public TornadoParameterController controller;

    private UIDocument uiDocument;

    public CameraModeController cameraModeController;

    private Button camFixedAButton;
    private Button camFixedBButton;
    private Button camVRButton;
    private Button camFreeButton;
    private Slider spinSlider;
    private Slider radiusSlider;
    private Slider speedSlider;
    private Button pauseButton;
    private Button resetButton;

    void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("[TornadoPanelUI] No UIDocument found on this GameObject.");
            return;
        }

        if (controller == null)
        {
            Debug.LogError("[TornadoPanelUI] No TornadoParameterController assigned.");
            return;
        }

        var root = uiDocument.rootVisualElement;

        if (cameraModeController == null)
            Debug.LogError("[TornadoPanelUI] cameraModeController is NULL");
        else
            Debug.Log("[TornadoPanelUI] cameraModeController assigned");

        camFixedAButton = root.Q<Button>("cam-fixed-a-button");
        camFixedBButton = root.Q<Button>("cam-fixed-b-button");
        camVRButton = root.Q<Button>("cam-vr-button");
        camFreeButton = root.Q<Button>("cam-free-button");

        Debug.Log($"camFixedAButton found? {camFixedAButton != null}");
        Debug.Log($"camFixedBButton found? {camFixedBButton != null}");
        Debug.Log($"camVRButton found? {camVRButton != null}");
        Debug.Log($"camFreeButton found? {camFreeButton != null}");

        spinSlider = root.Q<Slider>("spin-slider");
        radiusSlider = root.Q<Slider>("radius-slider");
        speedSlider = root.Q<Slider>("speed-slider");
        pauseButton = root.Q<Button>("pause-button");
        resetButton = root.Q<Button>("reset-button");

        if (spinSlider == null) Debug.LogError("[TornadoPanelUI] spin-slider not found.");
        if (radiusSlider == null) Debug.LogError("[TornadoPanelUI] radius-slider not found.");
        if (speedSlider == null) Debug.LogError("[TornadoPanelUI] speed-slider not found.");
        if (pauseButton == null) Debug.LogError("[TornadoPanelUI] pause-button not found.");
        if (resetButton == null) Debug.LogError("[TornadoPanelUI] reset-button not found.");
        if (camFixedAButton != null)
            camFixedAButton.clicked -= OnFixedAClicked;
        if (camFixedBButton != null)
            camFixedBButton.clicked -= OnFixedBClicked;
        if (camVRButton != null)
            camVRButton.clicked -= OnVRClicked;
        if (camFreeButton != null)
            camFreeButton.clicked -= OnFreeClicked;
        if (camFixedAButton != null)
            camFixedAButton.clicked += OnFixedAClicked;
        if (camFixedBButton != null)
            camFixedBButton.clicked += OnFixedBClicked;
        if (camVRButton != null)
            camVRButton.clicked += OnVRClicked;
        if (camFreeButton != null)
            camFreeButton.clicked += OnFreeClicked;

        if (controller.tornado == null)
        {
            Debug.LogError("[TornadoPanelUI] Controller has no Tornado assigned.");
            return;
        }

        if (spinSlider != null)
        {
            spinSlider.SetValueWithoutNotify(controller.tornado.tornadoSpinSpeed);
            spinSlider.RegisterValueChangedCallback(evt =>
            {
                controller.SetSpin(evt.newValue);
            });
        }

        if (radiusSlider != null)
        {
            radiusSlider.SetValueWithoutNotify(controller.tornado.topRadius);
            radiusSlider.RegisterValueChangedCallback(evt =>
            {
                controller.SetRadius(evt.newValue);
            });
        }

        if (speedSlider != null)
        {
            speedSlider.SetValueWithoutNotify(controller.tornado.tornadoSpeed);
            speedSlider.RegisterValueChangedCallback(evt =>
            {
                controller.SetSpeed(evt.newValue);
            });
        }

        if (pauseButton != null)
        {
            pauseButton.clicked += () =>
            {
                controller.TogglePause();
                pauseButton.text = controller.IsPaused() ? "Play" : "Pause";
            };

            pauseButton.text = controller.IsPaused() ? "Play" : "Pause";
        }

        if (resetButton != null)
        {
            resetButton.clicked += () =>
            {
                controller.ResetToDefaults();

                if (controller.tornado != null)
                {
                    if (spinSlider != null)
                        spinSlider.SetValueWithoutNotify(controller.tornado.tornadoSpinSpeed);

                    if (radiusSlider != null)
                        radiusSlider.SetValueWithoutNotify(controller.tornado.topRadius);

                    if (speedSlider != null)
                        speedSlider.SetValueWithoutNotify(controller.tornado.tornadoSpeed);
                }
            };
        }
    }

    void OnFixedAClicked()
    {
        if (cameraModeController != null) cameraModeController.SetFixedA();
    }

    void OnFixedBClicked()
    {
        if (cameraModeController != null) cameraModeController.SetFixedB();
    }

    void OnVRClicked()
    {
        if (cameraModeController != null) cameraModeController.SetVR();
    }

    void OnFreeClicked()
    {
        if (cameraModeController != null) cameraModeController.SetFree();
    }
}