using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameFlowManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Health playerHealth; 
    [SerializeField] private List<EnemyController> enemies;

    [Header("Pause Menu UI")]
    [SerializeField] private GameObject pauseMenuPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button retryButton1;

    [Header("End‐Game UI")]
    [SerializeField] private GameObject endGamePanel;
    [SerializeField] private TextMeshProUGUI endGameTitleText;
    [SerializeField] private TextMeshProUGUI averageAccuracyText;
    [SerializeField] private Button retryButton2;
    [SerializeField] private Button homeButton;

    [Header("Settings")]
    [SerializeField] private float chaseInterval = 10f;

    private bool isPaused = false;
    private bool gameEnded = false;
    private List<float> allAccuracies = new List<float>();

    private void Start()
    {
        pauseMenuPanel.SetActive(false);
        endGamePanel.SetActive(false);

        resumeButton.onClick.AddListener(ResumeGame);
        retryButton1.onClick.AddListener(RetryGame);
        retryButton2.onClick.AddListener(RetryGame);
        homeButton.onClick.AddListener(ReturnHome);

        GameManager.Instance.onAttack += (acc) =>
        {
            allAccuracies.Add(acc);
        };
    }

    private void Update()
    {
        if (gameEnded) return;

        // Toggle pause
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused) ResumeGame();
            else PauseGame();
        }

        if (!isPaused)
        {
            // 1) Game Over?
            if (playerHealth != null && playerHealth.CurrentHealth <= 0)
            {
                EndGame("GAME OVER");
                return;
            }

            // 2) Game Clear? (no enemies left)
            var remaining = Object.FindObjectsByType<EnemyController>(
                                FindObjectsSortMode.None
                            ).Length;
            if (remaining == 0)
            {
                EndGame("GAME CLEAR");
                return;
            }
        }
    }

    private void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f;
        GameManager.Instance.isPaused = true;

        pauseMenuPanel.SetActive(true);
        endGamePanel.SetActive(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = GameManager.Instance.CurrentTimeScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
        GameManager.Instance.isPaused = false;

        pauseMenuPanel.SetActive(false);
        endGamePanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void EndGame(string message)
    {
        gameEnded = true;
        Time.timeScale = 0f;
        GameManager.Instance.isPaused = true;

        endGameTitleText.text = message;
        endGamePanel.SetActive(true);
        pauseMenuPanel.SetActive(false);

        if (allAccuracies.Count > 0)
        {
            float sum = 0f;
            foreach (var a in allAccuracies) sum += a;
            float avg = sum / allAccuracies.Count;
            averageAccuracyText.gameObject.SetActive(true);
            averageAccuracyText.text = $"Average Gaze Accuracy:\n{avg*100f:F1}%";
        }
        else
        {
            float sum = 0f;
            foreach (var a in allAccuracies) sum += a;
            float avg = sum;
            averageAccuracyText.gameObject.SetActive(true);
            averageAccuracyText.text = $"Average Gaze Accuracy:\n{avg * 100f:F1}%";
        }

            Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RetryGame()
    {
        Time.timeScale = 1f;
        Loader.Load(Loader.Scene.GameScene);
    }

    private void ReturnHome()
    {
        Time.timeScale = 1f;
        Loader.Load(Loader.Scene.MainMenu);
    }
}
