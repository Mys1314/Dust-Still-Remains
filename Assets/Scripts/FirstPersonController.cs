using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public float walkSpeed = 2.5f;
    public float runSpeed = 5f;
    public float mouseSensitivity = 0.1f;
    public float gravity = -9.81f;

    public Transform cameraPivot;
    public Animator animator;

    [Header("Sprint FOV")]
    [Tooltip("Additional FOV applied while sprinting (Shift).")]
    public float sprintFovIncrease = 12f;

    [Tooltip("How quickly the camera blends to the target FOV.")]
    public float fovLerpSpeed = 10f;

    [Header("Sprint Camera Offset")]
    [Tooltip("Camera pivot local-position while sprinting (Shift).")]
    public Vector3 sprintCameraLocalOffset = new Vector3(0f, 1.4f, 0.3f);

    [Tooltip("How quickly the camera pivot blends to the target local-position.")]
    public float cameraOffsetLerpSpeed = 10f;

    float yVelocity;
    float xRotation;

    CharacterController controller;
    PlayerInputActions input;
    Vector2 moveInput;
    Vector2 lookInput;
    bool isRunning;

    Camera playerCamera;
    float baseFov;

    Vector3 baseCameraPivotLocalPos;

    private void Start()
    {

        if (cameraPivot != null)
            baseCameraPivotLocalPos = cameraPivot.localPosition;

        // Prefer the camera on the pivot (common setup), otherwise fall back to any child camera.
        if (cameraPivot != null)
            playerCamera = cameraPivot.GetComponentInChildren<Camera>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera != null)
            baseFov = playerCamera.fieldOfView;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = new PlayerInputActions();
    }

    void OnEnable()
    {
        input.Enable();

        input.Player.Move.performed += OnMovePerformed;
        input.Player.Move.canceled += OnMoveCanceled;

        input.Player.Look.performed += OnLookPerformed;
        input.Player.Look.canceled += OnLookCanceled;

        input.Player.Run.performed += OnRunPerformed;
        input.Player.Run.canceled += OnRunCanceled;
    }

    void OnDisable()
    {
        if (input == null) return;

        input.Player.Move.performed -= OnMovePerformed;
        input.Player.Move.canceled -= OnMoveCanceled;

        input.Player.Look.performed -= OnLookPerformed;
        input.Player.Look.canceled -= OnLookCanceled;

        input.Player.Run.performed -= OnRunPerformed;
        input.Player.Run.canceled -= OnRunCanceled;

        input.Disable();
    }

    void Update()
    {
        if (PauseMenuManager.IsPaused)
            return;
        HandleLook();
        HandleMove();
        UpdateSprintFov();
        UpdateSprintCameraOffset();
        UpdateAnimator();
    }

    void OnMovePerformed(InputAction.CallbackContext ctx) => moveInput = ctx.ReadValue<Vector2>();
    void OnMoveCanceled(InputAction.CallbackContext ctx) => moveInput = Vector2.zero;
    void OnLookPerformed(InputAction.CallbackContext ctx) => lookInput = ctx.ReadValue<Vector2>();
    void OnLookCanceled(InputAction.CallbackContext ctx) => lookInput = Vector2.zero;
    void OnRunPerformed(InputAction.CallbackContext ctx) => isRunning = true;
    void OnRunCanceled(InputAction.CallbackContext ctx) => isRunning = false;

    void HandleLook()
    {
        Vector2 mouse = lookInput * mouseSensitivity;

        xRotation -= mouse.y;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);
        cameraPivot.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        transform.Rotate(Vector3.up * mouse.x);
    }

    void HandleMove()
    {
        float speed = isRunning ? runSpeed : walkSpeed;
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;

        if (controller.isGrounded && yVelocity < 0)
            yVelocity = -0.1f;

        yVelocity += gravity * Time.deltaTime;

        Vector3 velocity = move * speed + Vector3.up * yVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    void UpdateSprintFov()
    {
        if (playerCamera == null) return;

        // Only widen FOV while actually moving + holding sprint.
        bool isMoving = moveInput.sqrMagnitude > 0.001f;
        float targetFov = baseFov + ((isRunning && isMoving) ? sprintFovIncrease : 0f);

        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
    }

    void UpdateSprintCameraOffset()
    {
        if (cameraPivot == null) return;

        // Apply requested pivot offset while sprinting, otherwise return to the original position.
        bool isMoving = moveInput.sqrMagnitude > 0.001f;
        Vector3 targetLocalPos = (isRunning && isMoving) ? sprintCameraLocalOffset : baseCameraPivotLocalPos;

        cameraPivot.localPosition = Vector3.Lerp(cameraPivot.localPosition, targetLocalPos, cameraOffsetLerpSpeed * Time.deltaTime);
    }

    void UpdateAnimator()
    {
        float targetSpeed = moveInput.magnitude * (isRunning ? 1f : 0.5f);
        animator.SetFloat("Speed", targetSpeed, 0.1f, Time.deltaTime);
    }
}
