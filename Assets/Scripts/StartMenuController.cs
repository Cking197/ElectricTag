using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;


public class StartMenuController : MonoBehaviour
{
    [Header("MenuParameters")] [SerializeField]
    private string gameSceneName = "GameScene";
    
    [Header("Actions")]
    [SerializeField] private InputActionReference cancelAction;

    [Header("Additional Panels")] [SerializeField]
    private GameObject startPanel;

    [SerializeField] private GameObject boutSelectPanel;


    [Header("DefaultSelections")] [SerializeField]
    private GameObject startFirstSelected;

    [SerializeField] private GameObject boutFirstSelected;

    private enum MenuStage
    {
        Start,
        BoutSelect
    }

    private MenuStage _currentStage = MenuStage.Start;

    private void OnEnable()
    {
        cancelAction.action.Enable();

        cancelAction.action.performed += OnCancel;
    }

    private void OnDisable()
    {
        cancelAction.action.performed -= OnCancel;
        
        cancelAction.action.Disable();
    }

    private void OnCancel(InputAction.CallbackContext ctx)
    {
        switch (_currentStage)
        {
            case MenuStage.BoutSelect:
                SetStage(MenuStage.Start);
                break;
        }
    }

    void Start()
    {
        SetStage(MenuStage.Start);
        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(startFirstSelected);
    }

    private void SetStage(MenuStage newStage)
    {
        _currentStage = newStage;

        startPanel.SetActive(_currentStage == MenuStage.Start);
        boutSelectPanel.SetActive(_currentStage == MenuStage.BoutSelect);

        EventSystem.current.SetSelectedGameObject(null);

        switch (_currentStage)
        {
            case MenuStage.Start:
                EventSystem.current.SetSelectedGameObject(startFirstSelected);
                break;

            case MenuStage.BoutSelect:
                EventSystem.current.SetSelectedGameObject(boutFirstSelected);
                break;
        }
    }

    public void StartGame()
    {
        SetStage(MenuStage.BoutSelect);
    }

    public void QuitGame()
    {
        Application.Quit();


#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void GoToBoutSelect()
    {
        startPanel.SetActive(false);
        boutSelectPanel.SetActive(true);

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(boutFirstSelected);
    }

    public void SelectFiveTouch()
    {
        Debug.Log("Selected 5 touch bout");
        LoadGameScene(5);
    }

    public void SelectFifteenTouch()
    {

        Debug.Log("Selected 15 touch bout");
        LoadGameScene(15);
    }
    
    public void LoadGameScene(int boutLength = 5)
    {
        GameSession.Instance.boutLength=boutLength;
        SceneManager.LoadScene(gameSceneName);
    }
}