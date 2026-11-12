using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// SIMPLIFIED scene navigator - just saves state and loads scenes normally
/// No more complex additive loading!
/// </summary>
public class SimpleSceneNavigator : MonoBehaviour
{
    public static void GoToWalletScene(string sceneName)
    {
        // Save current game state if we're in Scene 0
        if (SceneManager.GetActiveScene().buildIndex == 0)
        {
            GameStateManager.Instance.SaveGameState();
            Debug.Log($"[SimpleSceneNavigator] Saved game state, loading {sceneName}");
        }
        
        // Just load the scene normally
        SceneManager.LoadScene(sceneName);
    }

    public static void ReturnToGame()
    {
        Debug.Log("[SimpleSceneNavigator] Returning to Scene 0");
        
        // Simply load Scene 0
        // GameStateManager will restore state automatically
        SceneManager.LoadScene(0);
    }
}
