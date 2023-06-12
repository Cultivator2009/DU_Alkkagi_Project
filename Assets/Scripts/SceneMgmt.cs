using UnityEngine;
//using UnityEngine.Events;
using UnityEngine.SceneManagement;


//https://m.blog.naver.com/kimsung4752/221363671733
//https://daebalstudio.tistory.com/entry/%EC%9C%A0%EB%8B%88%ED%8B%B0-%EC%9D%B4%EB%B2%A4%ED%8A%B8-%EC%99%84%EB%B2%BD%ED%95%98%EA%B2%8C-%EC%9D%B4%ED%95%B4%ED%95%98%EA%B8%B0-3
//https://docs.unity3d.com/ScriptReference/SceneManagement.SceneManager-sceneLoaded.html
public class SceneMgmt : MonoBehaviour
{
    //public UnityEvent onGameStart;
    private GameManager gameManager = null;
    private GameObject gameManagerGameObject = null;
    // called zero
    void Awake()
    {
        gameManagerGameObject = GameObject.FindGameObjectWithTag("GameManager");
        gameManager = gameManagerGameObject.GetComponent<GameManager>();
    }

    // called first
    void OnEnable()
    {
        Debug.Log("OnEnable called");
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // called second
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log(scene.name);
        if (scene.name == "GameScene")
        {
            //onGameStart.Invoke();
            gameManager.gameState = GameManager.GameState.GameReadyProcess;
        }
    }

    // called third
    void Start()
    {
        Debug.Log("Start");
    }

    // called when the game is terminated
    void OnDisable()
    {
        Debug.Log("OnDisable");
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}