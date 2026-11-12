using UnityEngine;

public class AuthManager : MonoBehaviour
{
    // Config
    private const string PIN_KEY = "UserPIN";
    
    // State
    public bool IsEncrypted { get; private set; }
    public bool IsAuthenticated { get; set; } // Public setter for auto-lock

    private static AuthManager instance;
    public static AuthManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<AuthManager>();
                if (instance == null)
                {
                    GameObject obj = new GameObject("AuthManager");
                    instance = obj.AddComponent<AuthManager>();
                }
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            IsEncrypted = PlayerPrefs.HasKey(PIN_KEY);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    public bool CheckPIN(string pin)
    {
        string stored = PlayerPrefs.GetString(PIN_KEY);
        if (pin == stored)
        {
            IsAuthenticated = true;
            return true;
        }
        return false;
    }

    public void SetPIN(string pin)
    {
        if(pin.Length != 7) return;
        PlayerPrefs.SetString(PIN_KEY, pin);
        PlayerPrefs.Save();
        IsEncrypted = true;
        IsAuthenticated = true;
    }
    
    public void ResetAuth()
    {
        PlayerPrefs.DeleteKey(PIN_KEY);
        IsEncrypted = false;
        IsAuthenticated = false;
    }
}
