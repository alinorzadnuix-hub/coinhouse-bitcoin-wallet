using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Debug helper to show current scene state in Unity Console
/// Attach this to any GameObject in your scene to see what's happening
/// </summary>
public class SceneDebugger : MonoBehaviour
{
    void Start()
    {
        LogSceneState("SceneDebugger Started");
    }

    void Update()
    {
        // Press D key to log current state
        if (Input.GetKeyDown(KeyCode.D))
        {
            LogSceneState("Manual Debug (D key pressed)");
        }
    }

    void LogSceneState(string context)
    {
        Debug.Log($"=== {context} ===");
        Debug.Log($"Total Scenes Loaded: {SceneManager.sceneCount}");
        
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            string activeMarker = SceneManager.GetActiveScene() == scene ? " [ACTIVE]" : "";
            Debug.Log($"  Scene {i}: '{scene.name}' (BuildIndex: {scene.buildIndex}){activeMarker}");
        }
        
        Debug.Log($"Time.timeScale: {Time.timeScale}");
        
        if (GameSceneManager.Instance != null)
        {
            Debug.Log($"GameSceneManager exists");
        }
        else
        {
            Debug.Log($"GameSceneManager is NULL!");
        }
        
        Debug.Log("===================");
    }
}
