using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

//https://coderzero.tistory.com/entry/%EC%9C%A0%EB%8B%88%ED%8B%B0-C-%EA%B0%95%EC%A2%8C-17-%EC%9D%B4%EB%B2%A4%ED%8A%B8Event-%EB%8C%80%EB%A6%AC%EC%9E%90-%EB%8D%B8%EB%A6%AC%EA%B2%8C%EC%9D%B4%ED%8A%B8-Delegate
public class ValueChangeEvent
{
    public event EventHandler ValueChange;

    // Invoked when the value of the text field changes.
    public void TextValueChangeCheck()
    {
        if (ValueChange != null)
        {
            Debug.Log("Value Changed");
            ValueChange(this, EventArgs.Empty);
        }
    }
    public void SliderValueChangeCheck()
    {
        if (ValueChange != null)
        {
            Debug.Log("Value Changed");
            ValueChange(this, EventArgs.Empty);
        }
    }
}

public class UIManager : MonoBehaviour
{
    public InputField mainInputField;
    public Slider mainSlider;

    public ValueChangeEvent valueChangeEvent = new ValueChangeEvent();

    public void Start()
    {

        //Adds a listener to the main input field and invokes a method when the value changes.
        if (mainInputField != null)
        {
            mainInputField.onValueChanged.AddListener(delegate { valueChangeEvent.TextValueChangeCheck(); });
        }
        if (mainSlider != null)
        {
            mainSlider.onValueChanged.AddListener(delegate { valueChangeEvent.SliderValueChangeCheck(); });
        }
    }

}