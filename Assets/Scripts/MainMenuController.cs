using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace MokaWars.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Name of the scene to load when clicking Play. Defaults to 'Game'.")]
        public string playSceneName = "Game";

        private UIDocument uiDocument;
        private VisualElement root;

        private VisualElement mainButtonsContainer;
        private VisualElement optionsContainer;
        private VisualElement creditsContainer;

        private Button btnPlay;
        private Button btnOptions;
        private Button btnCredits;
        private Button btnQuit;
        private Button btnBackOptions;
        private Button btnBackCredits;

        private Slider sliderMusic;
        private Slider sliderSfx;
        private Toggle toggleFullscreen;

        private bool isBound = false;
        private bool isMenuVisible = true;

        private void Awake()
        {
            EnsureEventSystem();
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }
        }

        private void OnEnable()
        {
            InitializeUI();
        }

        private void Start()
        {
            // Fallback in case rootVisualElement was not ready during OnEnable
            if (!isBound)
            {
                InitializeUI();
            }
        }

        private void Update()
        {
            // Allow toggling menu with Escape key if in game
            bool toggleRequested = false;

            #if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                toggleRequested = true;
            }
            #endif

            #if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                toggleRequested = true;
            }
            #endif

            if (toggleRequested)
            {
                ToggleMenuVisibility();
            }
        }

        private void InitializeUI()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            if (uiDocument == null) return;
            root = uiDocument.rootVisualElement;
            if (root == null) return;

            // Unbind first to prevent multiple subscriptions
            UnbindEvents();

            // Find Containers
            mainButtonsContainer = root.Q<VisualElement>("main-buttons-container");
            optionsContainer = root.Q<VisualElement>("options-container");
            creditsContainer = root.Q<VisualElement>("credits-container");

            // Find Buttons
            btnPlay = root.Q<Button>("btn-play");
            btnOptions = root.Q<Button>("btn-options");
            btnCredits = root.Q<Button>("btn-credits");
            btnQuit = root.Q<Button>("btn-quit");
            btnBackOptions = root.Q<Button>("btn-back-options");
            btnBackCredits = root.Q<Button>("btn-back-credits");

            // Find Controls
            sliderMusic = root.Q<Slider>("slider-music");
            sliderSfx = root.Q<Slider>("slider-sfx");
            toggleFullscreen = root.Q<Toggle>("toggle-fullscreen");

            // Bind Button Click Events
            if (btnPlay != null)
            {
                btnPlay.clicked += OnPlayClicked;
            }

            if (btnOptions != null)
            {
                btnOptions.clicked += OnOptionsClicked;
            }

            if (btnCredits != null)
            {
                btnCredits.clicked += OnCreditsClicked;
            }

            if (btnQuit != null)
            {
                btnQuit.clicked += OnQuitClicked;
            }

            if (btnBackOptions != null)
            {
                btnBackOptions.clicked += ShowMainMenu;
            }

            if (btnBackCredits != null)
            {
                btnBackCredits.clicked += ShowMainMenu;
            }

            // Bind Controls Callbacks
            if (toggleFullscreen != null)
            {
                toggleFullscreen.value = Screen.fullScreen;
                toggleFullscreen.RegisterValueChangedCallback(OnFullscreenChanged);
            }

            if (sliderMusic != null)
            {
                sliderMusic.RegisterValueChangedCallback(OnMusicSliderChanged);
            }

            if (sliderSfx != null)
            {
                sliderSfx.RegisterValueChangedCallback(OnSfxSliderChanged);
            }

            isBound = true;
            Debug.Log("[MainMenuController] Successfully initialized and bound all menu buttons and controls.");
        }

        private void OnDisable()
        {
            UnbindEvents();
        }

        private void UnbindEvents()
        {
            if (!isBound) return;

            if (btnPlay != null) btnPlay.clicked -= OnPlayClicked;
            if (btnOptions != null) btnOptions.clicked -= OnOptionsClicked;
            if (btnCredits != null) btnCredits.clicked -= OnCreditsClicked;
            if (btnQuit != null) btnQuit.clicked -= OnQuitClicked;
            if (btnBackOptions != null) btnBackOptions.clicked -= ShowMainMenu;
            if (btnBackCredits != null) btnBackCredits.clicked -= ShowMainMenu;

            if (toggleFullscreen != null) toggleFullscreen.UnregisterValueChangedCallback(OnFullscreenChanged);
            if (sliderMusic != null) sliderMusic.UnregisterValueChangedCallback(OnMusicSliderChanged);
            if (sliderSfx != null) sliderSfx.UnregisterValueChangedCallback(OnSfxSliderChanged);

            isBound = false;
        }

        private void OnPlayClicked()
        {
            string targetScene = string.IsNullOrEmpty(playSceneName) ? "Game" : playSceneName;
            Debug.Log($"[MainMenu] START GAME clicked! Loading scene: '{targetScene}'");
            SceneManager.LoadScene(targetScene);
        }

        private void OnOptionsClicked()
        {
            Debug.Log("[MainMenu] SETTINGS clicked!");
            mainButtonsContainer?.AddToClassList("hidden");
            creditsContainer?.AddToClassList("hidden");
            optionsContainer?.RemoveFromClassList("hidden");
        }

        private void OnCreditsClicked()
        {
            Debug.Log("[MainMenu] CREDITS clicked!");
            mainButtonsContainer?.AddToClassList("hidden");
            optionsContainer?.AddToClassList("hidden");
            creditsContainer?.RemoveFromClassList("hidden");
        }

        private void ShowMainMenu()
        {
            Debug.Log("[MainMenu] BACK to Main Menu clicked!");
            optionsContainer?.AddToClassList("hidden");
            creditsContainer?.AddToClassList("hidden");
            mainButtonsContainer?.RemoveFromClassList("hidden");
        }

        private void OnQuitClicked()
        {
            Debug.Log("[MainMenu] QUIT GAME clicked!");
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }

        private void OnFullscreenChanged(ChangeEvent<bool> evt)
        {
            Screen.fullScreen = evt.newValue;
            Debug.Log($"[MainMenu] Fullscreen changed to: {evt.newValue}");
        }

        private void OnMusicSliderChanged(ChangeEvent<float> evt)
        {
            float normalized = Mathf.Clamp01(evt.newValue / 100f);
            AudioListener.volume = normalized;
            Debug.Log($"[MainMenu] Master/Music Volume set to: {evt.newValue:F0}%");
        }

        private void OnSfxSliderChanged(ChangeEvent<float> evt)
        {
            Debug.Log($"[MainMenu] SFX Volume set to: {evt.newValue:F0}%");
        }

        public void ToggleMenuVisibility()
        {
            if (isMenuVisible)
            {
                HideMenu();
            }
            else
            {
                ShowMenu();
            }
        }

        public void HideMenu()
        {
            if (root != null)
            {
                root.style.display = DisplayStyle.None;
            }
            isMenuVisible = false;
            Debug.Log("[MainMenu] Menu hidden (Press Escape to show).");
        }

        public void ShowMenu()
        {
            if (root != null)
            {
                root.style.display = DisplayStyle.Flex;
            }
            ShowMainMenu();
            isMenuVisible = true;
            Debug.Log("[MainMenu] Menu shown.");
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current == null)
            {
                var existing = FindAnyObjectByType<EventSystem>();
                if (existing == null)
                {
                    var esGo = new GameObject("EventSystem");
                    esGo.AddComponent<EventSystem>();
                    esGo.AddComponent<InputSystemUIInputModule>();
                    Debug.Log("[MainMenuController] Created EventSystem with InputSystemUIInputModule.");
                }
            }
        }
    }
}
