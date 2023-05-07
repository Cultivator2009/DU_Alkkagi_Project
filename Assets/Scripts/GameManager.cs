using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class GameManager : MonoBehaviour
{
    public enum GameState
    {
        WaitingForInput,
        ProcessingTurn
    }

    public GameState gameState;
    public List<GamePieceDragAndReleaseForce> gamePieceScripts = new List<GamePieceDragAndReleaseForce>();
    public GamePieceDragAndReleaseForce gamePieceScript;
    public GamePieceDragAndReleaseForce selGamePiece;

    public int currentPlayerIndex;
    public int currentPlayerID;
    public List<PlayersManager> playersList = new List<PlayersManager>();

    public bool isAllGamePieceStopped = true;
    private void Start()
    {
        // Create two players with different colors and add them to the players list
        // Constructor?
        // https://etst.tistory.com/32
        PlayersManager playersManager = new PlayersManager(Color.black, 0, 0);
        playersList.Add(playersManager);
        playersManager = new PlayersManager(Color.white, 1, 1);
        playersList.Add(playersManager);
        Debug.Log(playersList);
        
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
    }

    private void Update()
    {
        switch (gameState)
        {
            case GameState.WaitingForInput:
                // Check for input from the current player
                // if (Input.GetMouseButtonDown(0))
                RaycastHit hit;
                foreach (GamePieceDragAndReleaseForce gamePieceScript in gamePieceScripts){
                    if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit)){
                        if (gamePieceScript.isSelected == true){
                            Debug.Log(gamePieceScript);
                            // Check if the clicked object belongs to the current player
                            // PlayersManager player = hit.collider.GetComponent<PlayersManager>();
                            PlayersManager player = gamePieceScript.gameObject.GetComponent<PlayersManager>();
                            // if (player == null) Debug.LogError("Player component not found on this GameObject");
                            // if (player != null && player.playerIndex == currentPlayerIndex)
                            if (player.ID == currentPlayerID){
                                selGamePiece = gamePieceScript;
                                selGamePiece.isDragging = true;
                                gameState = GameState.ProcessingTurn;
                                Debug.Log(player);
                                break;
                            }
                            else if (player.ID != currentPlayerID){
                                gamePieceScript.isSelected = false;
                                Debug.Log("It's not Your turn! Currently : "+(currentPlayerID+1));
                            }
                        }
                    }
                }
            break;

            case GameState.ProcessingTurn:
                // Perform actions for the current player's turn
                // ...
                    // Highlight the selected object or do other actions
                    // ...
                    foreach (GamePieceDragAndReleaseForce gamePieceScript in gamePieceScripts){
                        if (gamePieceScript.isGamePieceMoving){
                            isAllGamePieceStopped = false;
                            break;
                        }
                        else isAllGamePieceStopped = true;
                    }
                    // End the turn and switch to the next player
                    if (selGamePiece.isDragging == false && isAllGamePieceStopped){
                        Debug.Log("ProcessingTurn");
                        EndTurn();
                    }
            break;
        }
    }

    private void EndTurn()
    {
        // Reset the game state to WaitingForInput
        gameState = GameState.WaitingForInput;

        // Switch to the next player
        // currentPlayerIndex = (currentPlayerIndex + 1) % playersList.Count;
        int nextPlayerIndex = (playersList.FindIndex(p => p.ID == currentPlayerID) + 1) % playersList.Count;
        currentPlayerID = playersList[nextPlayerIndex].ID;
        Debug.Log("Current Turn : "+(currentPlayerID+1));
    }
}
