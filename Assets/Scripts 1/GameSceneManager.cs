using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple manager that sets a flag before going to wallet
/// So UIManager can skip splash/titleUI when returning
/// </summary>
public class GameSceneManager : MonoBehaviour
{
    private static GameSceneManager instance;
    public static GameSceneManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("GameSceneManager");
                instance = go.AddComponent<GameSceneManager>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

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
    /// Load wallet scene and set flag for returning
    /// </summary>
    public void LoadWalletScene(string sceneName)
    {
        // Set flag so UIManager knows we're coming back
        PlayerPrefs.SetInt("ReturningFromWallet", 1);
        PlayerPrefs.Save();
        Debug.Log($"[GameSceneManager] Set flag, loading {sceneName}");
        
        // Normal scene load
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>
    /// Return to Scene 0
    /// </summary>
    public void ReturnToGame()
    {
        Debug.Log("[GameSceneManager] Returning to Scene 0");
        SceneManager.LoadScene(0);
    }
}

