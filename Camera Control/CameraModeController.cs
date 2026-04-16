using UnityEngine;
using Unity.XR.CoreUtils;

public class CameraModeController : MonoBehaviour
{
    [Header("XR Rig")]
    public XROrigin xrOrigin;

    [Header("Static Camera Positions")]
    public Transform staticPointA;
    public Transform staticPointB;

    [Header("VR Movement Components")]
    public MonoBehaviour moveProvider;
    public MonoBehaviour turnProvider;
    public MonoBehaviour characterControllerDriver;
    public CharacterController characterController;

    [Header("Mode-Specific Controllers")]
    public FreeCameraController freeCameraController;
    public StaticCameraPan staticCameraPan;

    [Header("UI")]
    public Canvas worldCanvas;

    private enum CameraMode { VR, Free, StaticA, StaticB }
    private CameraMode currentMode;

    public bool IsStaticMode()
    {
        return currentMode == CameraMode.StaticA || currentMode == CameraMode.StaticB;
    }
    

    void Start()
    {
        SetVR();

        // Attach UI to camera
        if (worldCanvas != null)
        {
            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.transform.SetParent(Camera.main.transform, false);
            worldCanvas.transform.localPosition = new Vector3(0f, 0f, 2f);
            worldCanvas.transform.localRotation = Quaternion.identity;
            worldCanvas.transform.localScale = Vector3.one * 0.001f;
            worldCanvas.worldCamera = Camera.main;
        }
    }

    public void SetVR()
    {
        currentMode = CameraMode.VR;

        SetVRMovementEnabled(true);

        if (freeCameraController != null)
            freeCameraController.enabled = false;

        if (staticCameraPan != null)
            staticCameraPan.enabled = false;

        Debug.Log("[CameraModeController] Switched to VR");
    }

    public void SetFree()
    {
        currentMode = CameraMode.Free;

        SetVRMovementEnabled(false);

        if (staticCameraPan != null)
            staticCameraPan.enabled = false;

        if (freeCameraController != null)
            freeCameraController.enabled = true;

        Debug.Log("[CameraModeController] Switched to Free");
    }

    public void SetFixedA()
    {
        currentMode = CameraMode.StaticA;

        SetVRMovementEnabled(false);

        if (freeCameraController != null)
            freeCameraController.enabled = false;

        if (staticCameraPan != null)
            staticCameraPan.enabled = true;

        TeleportTo(staticPointA);

        Debug.Log("[CameraModeController] Switched to Static A");
    }

    public void SetFixedB()
    {
        currentMode = CameraMode.StaticB;

        SetVRMovementEnabled(false);

        if (freeCameraController != null)
            freeCameraController.enabled = false;

        if (staticCameraPan != null)
            staticCameraPan.enabled = true;

        TeleportTo(staticPointB);

        Debug.Log("[CameraModeController] Switched to Static B");
    }

    void SetVRMovementEnabled(bool enabled)
    {
        if (moveProvider != null)
            moveProvider.enabled = enabled;

        if (turnProvider != null)
            turnProvider.enabled = enabled;

        if (characterControllerDriver != null)
            characterControllerDriver.enabled = enabled;

        if (characterController != null)
            characterController.enabled = enabled;
    }

    void TeleportTo(Transform target)
    {
        if (target == null || xrOrigin == null) return;

        // Disable controller during teleport so it doesn't immediately shove the rig
        if (characterController != null)
            characterController.enabled = false;

        xrOrigin.MoveCameraToWorldLocation(target.position);

        // Match yaw to the target
        Vector3 euler = xrOrigin.transform.eulerAngles;
        xrOrigin.transform.rotation = Quaternion.Euler(euler.x, target.eulerAngles.y, euler.z);

        // Leave the controller OFF in static mode
    }
}