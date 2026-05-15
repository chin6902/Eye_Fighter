using UnityEngine;
using UnityEngine.UI;

public class Controls : MonoBehaviour
{
    //Panels
    [SerializeField] private GameObject controlsPanel;
    [SerializeField] private GameObject firstPage;
    [SerializeField] private GameObject secondPage;
    [SerializeField] private GameObject thirdPage;

    //Buttons
    [SerializeField] private Button controlsButton;
    [SerializeField] private Button closeButton1;
    [SerializeField] private Button closeButton2;
    [SerializeField] private Button closeButton3;
    [SerializeField] private Button firstPageNextButton;
    [SerializeField] private Button secondPageNextButton;
    [SerializeField] private Button secondPageBackButton;
    [SerializeField] private Button thirdPageBackButton;

    private void Awake()
    {
        controlsButton.onClick.AddListener(() =>
        {
            OpenControlsPanel();
        });

        closeButton1.onClick.AddListener(() =>
        {
            CloseControlsPanel();
        });

        closeButton2.onClick.AddListener(() =>
        {
            CloseControlsPanel();
        });

        closeButton3.onClick.AddListener(() =>
        {
            CloseControlsPanel();
        });

        firstPageNextButton.onClick.AddListener(() =>
        {
            firstPage.SetActive(false);
            secondPage.SetActive(true);
            thirdPage.SetActive(false);
        });

        secondPageNextButton.onClick.AddListener(() =>
        {
            firstPage.SetActive(false);
            secondPage.SetActive(false);
            thirdPage.SetActive(true);
        });

        secondPageBackButton.onClick.AddListener(() =>
        {
            firstPage.SetActive(true);
            secondPage.SetActive(false);
            thirdPage.SetActive(false);
        });

        thirdPageBackButton.onClick.AddListener(() =>
        {
            firstPage.SetActive(false);
            secondPage.SetActive(true);
            thirdPage.SetActive(false);
        });
    }

    private void Start()
    {
        CloseControlsPanel();
    }

    private void OpenControlsPanel()
    {
        controlsPanel.SetActive(true);

        firstPage.SetActive(true);
        secondPage.SetActive(false);
        thirdPage.SetActive(false);
    }

    private void CloseControlsPanel()
    {
        controlsPanel.SetActive(false);
    }
}
