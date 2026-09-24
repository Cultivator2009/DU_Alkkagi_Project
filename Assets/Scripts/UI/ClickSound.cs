using UnityEngine;
using UnityEngine.UI;

// The interface click. The UI builders put one on every button they make.
[RequireComponent(typeof(Button))]
public class ClickSound : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() => GameAudio.PlayInterface(GameAudio.Bank.click));
    }
}
