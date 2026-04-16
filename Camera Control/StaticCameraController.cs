using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using Unity.XR.CoreUtils;

public class StaticCameraPan : MonoBehaviour
{
    public CameraModeController cameraModeController;
    public float yawSpeed = 60f;

    public XROrigin xrOrigin;

    private InputDevice rightController;

    void Start()
    {
        if (xrOrigin == null)
            xrOrigin = FindObjectOfType<XROrigin>();
    }

    void OnEnable()
    {
        TryGetController();
        Debug.Log("[StaticCameraPan] Enabled");
    }

    void OnDisable()
    {
        Debug.Log("[StaticCameraPan] Disabled");
    }

    void TryGetController()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
            devices
        );

        if (devices.Count > 0)
            rightController = devices[0];
    }

    void Update()
    {
        if (cameraModeController == null || xrOrigin == null) return;
        if (!cameraModeController.IsStaticMode()) return;

        if (!rightController.isValid)
        {
            TryGetController();
            return;
        }

        rightController.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 input);

        xrOrigin.transform.Rotate(Vector3.up, input.x * yawSpeed * Time.deltaTime, Space.World);
    }
}