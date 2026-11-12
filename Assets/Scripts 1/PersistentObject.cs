using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Add this script to important GameObjects in Scene 0 that you want to persist
/// For example: Player, GameManager, etc.
/// </summary>
public class PersistentObject : MonoBehaviour
{
    [Header("This object will persist across scenes")]
    [Tooltip("Should this object be destroyed when returning to Scene 0?")]
    public bool destroyOnSceneReturn = false;

    private static bool hasReturnedToGame = false;

    void Awake()
    {
        // Check if we're returning to Scene 0
        if (SceneManager.GetActiveScene().buildIndex == 0 && hasReturnedToGame && destroyOnSceneReturn)
        {
            // We've returned to Scene 0, destroy this object so a new one can be created
            Destroy(gameObject);
            return;
        }

        // Make this object persist
        DontDestroyOnLoad(gameObject);
        
        Debug.Log($"[PersistentObject] {gameObject.name} will persist across scenes");
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.buildIndex == 0)
        {
            hasReturnedToGame = true;
            Debug.Log($"[PersistentObject] Returned to Scene 0");
        }
        else
        {
            hasReturnedToGame = false;
        }
    }
}
