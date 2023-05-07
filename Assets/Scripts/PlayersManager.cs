using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayersManager : MonoBehaviour
{
    public Color color;
    public int score;
    public int playerIndex; // Add playerIndex property
    public int ID;

    // https://www.csharpstudy.com/CSharp/CSharp-static.aspx
    public PlayersManager(Color color, int index, int ID) // Add index parameter to constructor
    {
        this.color = color;
        this.ID = ID;
        playerIndex = index; // Set playerIndex property
        score = 0;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}