using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{
    public void Change()
    {
        SceneManager.LoadScene("GameScene");
    }
}