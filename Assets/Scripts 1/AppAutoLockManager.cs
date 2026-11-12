using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Auto-locks the app if backgrounded for more than 10 seconds.
/// Attaches itself as a DontDestroyOnLoad singleton.
/// </summary>
public class AppAutoLockManager : MonoBehaviour
{
    private static AppAutoLockManager instance;
    public static AppAutoLockManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("AppAutoLockManager");
                instance = obj.AddComponent<AppAutoLockManager>();
            }
            return instance;
        }
    }

    private float pauseTime = 0f;
    private bool wasPaused = false;
    private const float AUTO_LOCK_SECONDS = 10f;

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

    void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            // App going to background - record time
            pauseTime = Time.realtimeSinceStartup;
            wasPaused = true;
            Debug.Log($"[AppAutoLockManager] App paused at {pauseTime:F1}s");
        }
        else
        {
            // App returning to foreground
            if (wasPaused)
            {
                float elapsed = Time.realtimeSinceStartup - pauseTime;
                Debug.Log($"[AppAutoLockManager] App resumed after {elapsed:F1}s");

                if (elapsed >= AUTO_LOCK_SECONDS)
                {
                    // Lock the app - force PIN re-entry
                    Debug.Log("[AppAutoLockManager] Auto-lock triggered! Requiring PIN.");
                    
                    if (AuthManager.Instance != null)
                    {
                        // Clear authentication so PIN is required
                        AuthManager.Instance.IsAuthenticated = false;
                    }
                    
                    // Don't lock if already on AppLock screen
                    string currentScene = SceneManager.GetActiveScene().name;
                    if (currentScene != "AppLock")
                    {
                        SceneManager.LoadScene("AppLock");
                    }
                }
                
                wasPaused = false;
            }
        }
    }
}
