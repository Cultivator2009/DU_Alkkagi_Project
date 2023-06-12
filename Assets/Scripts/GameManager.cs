using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;


public class GameManager : MonoBehaviour
{
    public static GameManager manager;
    public enum GameState
    {
        Mainmenu,
        GameReadyProcess,
        WaitingForInput,
        WaitingForEndTurn,
        ProcessingTurn,
        TurnChanging
    }

    public GameState gameState;
    public List<GamePieceDragAndReleaseForce> gamePieceScripts = new List<GamePieceDragAndReleaseForce>();
    public GamePieceDragAndReleaseForce gamePieceScript;
    public GamePieceDragAndReleaseForce selGamePiece;

    public int totalPlayerCnt=2; // get input from UI future TODO
    public int currentPlayerIndex;
    public int currentPlayerID;
    public List<PlayersManager> playersList = new List<PlayersManager>();

    public bool isAllGamePieceStopped = true;

    public GameObject[] vcams = null;

    private void Awake()
    {
        if (manager == null)
        {
            manager = this;
            DontDestroyOnLoad(manager);
            Debug.Log(manager);
        }
        else if (manager != this)
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        gameState = GameState.Mainmenu;
    }

    public void OnGameStart()
    {
        gameState = GameState.GameReadyProcess;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftAlt))
            if (vcams != null)
                vcams[0].SetActive(false);
        if (Input.GetKeyUp(KeyCode.LeftAlt))
            if (vcams != null)
                vcams[0].SetActive(true);
        switch (gameState)
        {
            case GameState.GameReadyProcess:
                GamePreparation();
                gameState = GameState.WaitingForInput;
            break;

            case GameState.WaitingForInput:
                InputReady();
            break;

            case GameState.WaitingForEndTurn:
                EndTurnReady();
            break;

            case GameState.ProcessingTurn:
                TurnProcess();
            break;
        }
    }

    private void GamePreparation()
    {
        // Create two players with different colors and add them to the players list
        // Constructor?
        // https://etst.tistory.com/32
        // PlayersManager playersManager = new PlayersManager(Color.black, 0, 0);
        // playersList.Add(playersManager);
        // playersManager = new PlayersManager(Color.white, 1, 1);
        // playersList.Add(playersManager);

        // Assigning Players by adding GameObject with Component
        var playersParentOb = new GameObject("Players");
        for (var playerIndex=0; playerIndex<totalPlayerCnt; playerIndex++){
            var playersOb = new GameObject("P"+(playerIndex+1));
            playersOb.transform.SetParent(playersParentOb.transform);
            playersOb.AddComponent<PlayersManager>();
            var playersObComp = playersOb.GetComponent<PlayersManager>();
            playersObComp.ID = playerIndex;
            playersList.Add(playersObComp);
        }
        Debug.Log(playersList);

        // Finding all SpawnPoints
        GameObject spawnPointsGOb = null;
        //Find all GameObjects with specific tag
        GameObject[] spawnPointsTaggedObjects = GameObject.FindGameObjectsWithTag("SpawnPoints");
        int i=0;
        //iterate through all returned objects, and find the one with the correct name
        foreach (GameObject gOb in spawnPointsTaggedObjects){
            string name = "P"+(i+1);
            // GameObject GetChildWithName(this GameObject gOb, string name)
            //  => gOb.transform.Find(name)?.gameObject;
        }
        
        //If still null, that means no object matched the tag and name criteria
        // if (objectImLookingFor == null){
        //     Debug.Log("SuperCoolName with SuperCoolTag not found");
        // }
        // else{
        //     Debug.Log("VICTORY!");  //We found the object
        // }
        
        currentPlayerIndex = 0;
        currentPlayerID = playersList[currentPlayerIndex].ID;
        gameState = GameState.WaitingForInput;
        GameObject[] gamePieceGOs = GameObject.FindGameObjectsWithTag("GamePiece_GO");
        // Loop through the game objects and add their scripts to the list
        foreach (GameObject gamePieceGO in gamePieceGOs)
        {
            gamePieceScript = gamePieceGO.GetComponent<GamePieceDragAndReleaseForce>();
            if (gamePieceScript != null)gamePieceScripts.Add(gamePieceScript);
            Debug.Log(gamePieceScript);
        }
        vcams = GameObject.FindGameObjectsWithTag("vcam");
    }
    private void InputReady()
    {
        // Check for input from the current player
        // if (Input.GetMouseButtonDown(0))
        RaycastHit hit;
        foreach (GamePieceDragAndReleaseForce gamePieceScript in gamePieceScripts)
        {
            if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit))
            {
                if (gamePieceScript.isSelected == true && !Input.GetMouseButtonDown(1))
                {
                    Debug.Log(gamePieceScript);
                    // Check if the clicked object belongs to the current player
                    // PlayersManager player = hit.collider.GetComponent<PlayersManager>();
                    // PlayersManager player = gamePieceScript.gameObject.GetComponent<PlayersManager>();
                    GamePieceManager gamePieceManager =
                        gamePieceScript.gameObject.GetComponent<GamePieceManager>();
                    // if (player == null) Debug.LogError("Player component not found on this GameObject");
                    // if (player != null && player.playerIndex == currentPlayerIndex)
                    if (gamePieceManager.playerIndex == currentPlayerID)
                    {
                        if (selGamePiece != null)
                        {
                            //selGamePiece.isSelected = false;
                            //selGamePiece.isDragging = false;
                            selGamePiece.isCancelled = false;
                            //selGamePiece.lr.enabled = false;
                        }
                        selGamePiece = gamePieceScript;
                        selGamePiece.isDragging = true;
                        gameState = GameState.WaitingForEndTurn;
                        Debug.Log(gamePieceManager);
                        Debug.Log("WaitingForEndTurn");
                        break;
                    }
                    else
                    {
                        gamePieceScript.isSelected = false;
                        Debug.Log("It's not Your turn! Currently : " + (currentPlayerID + 1));
                    }
                }
            }
        }
    }
    private void EndTurnReady()
    {
        if (selGamePiece.isCancelled == true)
        {
            gameState = GameState.WaitingForInput;
        }
        else if (selGamePiece.isDragging == false)
        {
            gameState = GameState.ProcessingTurn;
        }
    }
    private void TurnProcess()
    {
        // Perform actions for the current player's turn
        // ...
        // Highlight the selected object or do other actions
        // ...

        foreach (GamePieceDragAndReleaseForce gamePieceScript in gamePieceScripts)
        {
            if (gamePieceScript.isGamePieceMoving)
            {
                isAllGamePieceStopped = false;
                break;
            }
            else isAllGamePieceStopped = true;
        }
        // End the turn and switch to the next player
        if (selGamePiece.isCancelled == false && 
            selGamePiece.isDragging == false && 
            isAllGamePieceStopped)
        {
            EndTurn();
        }
    }
    private void EndTurn()
    {
        // Reset the game state to WaitingForInput
        gameState = GameState.WaitingForInput;
        selGamePiece = null;

        // Switch to the next player
        // currentPlayerIndex = (currentPlayerIndex + 1) % playersList.Count;
        int nextPlayerIndex = (playersList.FindIndex(p => p.ID == currentPlayerID) + 1) % playersList.Count;
        currentPlayerID = playersList[nextPlayerIndex].ID;
        Debug.Log("Current Turn : "+(currentPlayerID+1));
    }
}
