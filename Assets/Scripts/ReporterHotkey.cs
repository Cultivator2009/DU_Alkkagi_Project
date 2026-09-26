using UnityEngine;

// Opens the Unity-Logs-Viewer Reporter from a key, in the editor and
// development builds only. Its own trigger - drawing a circle with the
// mouse held - also fired while dragging stones and aiming, so the scene
// sets numOfCircleToShow out of reach and this replaces it. A release build
// has no way to open it, so there it goes altogether: it would only keep
// capturing every log line and sampling the frame rate.
[RequireComponent(typeof(Reporter))]
public class ReporterHotkey : MonoBehaviour
{
    public KeyCode key = KeyCode.F12;

    private Reporter reporter;

    private void Awake()
    {
        if (!Debug.isDebugBuild)
        {
            enabled = false; // gone at the end of the frame, after this frame's Update
            Destroy(gameObject);
            return;
        }
        reporter = GetComponent<Reporter>();
    }

    private void Update()
    {
        if (reporter.show || !Input.GetKeyDown(key)) return;
        reporter.SendMessage("doShow"); // private in Reporter
    }
}
