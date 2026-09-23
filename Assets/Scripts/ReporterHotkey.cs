using UnityEngine;

// Opens the Unity-Logs-Viewer Reporter from a key, in the editor and
// development builds only. Its own trigger - drawing a circle with the
// mouse held - also fired while dragging stones and aiming, so the scene
// sets numOfCircleToShow out of reach and this replaces it.
[RequireComponent(typeof(Reporter))]
public class ReporterHotkey : MonoBehaviour
{
    public KeyCode key = KeyCode.F12;

    private Reporter reporter;

    private void Awake()
    {
        reporter = GetComponent<Reporter>();
    }

    private void Update()
    {
        if (!Debug.isDebugBuild || reporter.show || !Input.GetKeyDown(key)) return;
        reporter.SendMessage("doShow"); // private in Reporter
    }
}
