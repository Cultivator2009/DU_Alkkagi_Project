using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class InputManager : MonoBehaviour
{
    public int playerIndex;
    public UIManager uiManager;
    
    public void Start()
    {
        uiManager = GameObject.FindGameObjectWithTag("UIManager").GetComponent<UIManager>();
        uiManager.valueChangeEvent.ValueChange += ValueChangeInput;
    }

    public void ValueChangeInput(object sender, EventArgs e)
    {
        Debug.Log("Value Changed22");
    }
}