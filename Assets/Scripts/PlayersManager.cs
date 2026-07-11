using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayersManager : MonoBehaviour
{
    public Color color;
    public int score;
    public int playerIndex; // Add playerIndex property
    public int ID;
    public int totalPieceCnt;

    public void AddScore(int amount)
    {
        score += amount;
    }

    public void OnPieceLost()
    {
        totalPieceCnt = Mathf.Max(0, totalPieceCnt - 1);
    }
}