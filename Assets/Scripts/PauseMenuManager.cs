using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;


public class PauseMenuManager : MonoBehaviour
{
    PlayerInputActions input;

    [Header("UI")]
    public GameObject pauseMenu;

    [Tooltip("Optional: crosshair/aim reticle to disable while paused.")]
    public GameObject aim;

    [Header("Mouse Sensitivity")]
    public FirstPersonController playerController;
    public float minSensitivity = 0.02f;
    public float maxSensitivity = 0.3f;

    bool isPaused;

    public static bool IsPaused { get; private set; }


    void Awake()
    {
        input = new PlayerInputActions();
    }

    void OnEnable()
    {
        input.Enable();
        input.Player.Pause.performed += OnPause;
    }

    void OnDisable()
    {
        input.Player.Pause.performed -= OnPause;
        input.Disable();
    }

    void OnPause(InputAction.CallbackContext ctx)
    {
        ToggleMenu();
    }


    void Start()
    {
        ResumeGame();
    }


    public void ToggleMenu()
    {
        if (isPaused)
            ResumeGame();
        else
            PauseGame();
    }

    public void PauseGame()
    {
        isPaused = true;

        if (pauseMenu != null)
            pauseMenu.SetActive(true);

        if (aim != null)
            aim.SetActive(false);

        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        IsPaused = true;
    }

    public void ResumeGame()
    {
        isPaused = false;

        if (pauseMenu != null)
            pauseMenu.SetActive(false);

        if (aim != null)
            aim.SetActive(true);

        Time.timeScale = 1f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        IsPaused = false;
    }


    public void RestartScene()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void SetMouseSensitivity(float value)
    {
        playerController.mouseSensitivity = value;
    }
}
