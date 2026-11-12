using UnityEngine;

/// <summary>
/// Simple game state manager that saves and restores Scene 0 state
/// Much simpler than additive scene loading!
/// </summary>
public class GameStateManager : MonoBehaviour
{
    private static GameStateManager instance;
    public static GameStateManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("GameStateManager");
                instance = go.AddComponent<GameStateManager>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    // Saved state
    private bool hasReturnState = false;
    private Vector3 savedPlayerPosition;
    private Quaternion savedPlayerRotation;
    private float savedGameTime;
    private int savedScore;
    
    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Call this before leaving Scene 0 to save current state
    /// </summary>
    public void SaveGameState()
    {
        // Find player or main camera to save position
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            savedPlayerPosition = player.transform.position;
            savedPlayerRotation = player.transform.rotation;
        }
        else
        {
            // Fallback to main camera
            if (Camera.main != null)
            {
                savedPlayerPosition = Camera.main.transform.position;
                savedPlayerRotation = Camera.main.transform.rotation;
            }
        }
        
        savedGameTime = Time.time;
        // Save score or other game data here
        // savedScore = GameManager.score; // example
        
        hasReturnState = true;
        Debug.Log($"[GameStateManager] State saved - Position: {savedPlayerPosition}");
    }

    /// <summary>
    /// Call this when Scene 0 loads to restore state
    /// </summary>
    public void RestoreGameState()
    {
        if (!hasReturnState)
        {
            Debug.Log("[GameStateManager] No saved state to restore");
            return;
        }

        // Wait one frame for scene to initialize
        StartCoroutine(RestoreAfterFrame());
    }

    private System.Collections.IEnumerator RestoreAfterFrame()
    {
        yield return null; // Wait one frame
        
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            player.transform.position = savedPlayerPosition;
            player.transform.rotation = savedPlayerRotation;
            Debug.Log($"[GameStateManager] Player position restored to: {savedPlayerPosition}");
        }
        else if (Camera.main != null)
        {
            Camera.main.transform.position = savedPlayerPosition;
            Camera.main.transform.rotation = savedPlayerRotation;
            Debug.Log($"[GameStateManager] Camera position restored to: {savedPlayerPosition}");
        }
        
        // Restore score or other data
        // GameManager.score = savedScore; // example
        
        hasReturnState = false; // Clear state
        Debug.Log("[GameStateManager] State restored successfully!");
    }

    public bool HasSavedState()
    {
        return hasReturnState;
    }
}
