using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class AppLockUI : MonoBehaviour
{
    private Text titleText;
    private Text pinDisplay;
    private Text statusText;
    
    private string currentPin = "";
    private bool isSetupMode = false;
    private bool isConfirming = false;
    private string setupFirstEntry = "";
    private string sessionToken;
    private float sessionStartTime;

    void Start()
    {
        // Ensure EventSystem exists (Critical for UI interaction)
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Generate CSRF token for this session
        sessionToken = System.Guid.NewGuid().ToString();
        sessionStartTime = Time.realtimeSinceStartup;
        PlayerPrefs.SetString("AppLockSessionToken", sessionToken);
        PlayerPrefs.SetFloat("AppLockSessionTime", sessionStartTime);
        PlayerPrefs.Save();

        BuildUI();
        
        // Determine Mode
        if(!AuthManager.Instance.IsEncrypted)
        {
            isSetupMode = true;
            titleText.text = isConfirming ? LocalizationManager.Instance.Get("LOCK_TITLE_CONFIRM") : LocalizationManager.Instance.Get("LOCK_TITLE_CREATE");
            statusText.text = isConfirming ? LocalizationManager.Instance.Get("LOCK_STATUS_REENTER") : LocalizationManager.Instance.Get("LOCK_STATUS_NEW");
        }
        else
        {
            isSetupMode = false;
            titleText.text = LocalizationManager.Instance.Get("LOCK_TITLE_ENTER");
            statusText.text = LocalizationManager.Instance.Get("LOCK_STATUS_CHECK");
        }
    }

    void OnKeyPress(string key)
    {
        if(currentPin.Length < 7)
        {
            currentPin += key;
            UpdateDisplay();
            
            if(currentPin.Length == 7)
            {
                StartCoroutine(ProcessPin());
            }
        }
    }

    void OnDelete()
    {
        if(currentPin.Length > 0)
        {
            currentPin = currentPin.Substring(0, currentPin.Length - 1);
            UpdateDisplay();
        }
    }

    void UpdateDisplay()
    {
        string dots = "";
        for(int i=0; i<currentPin.Length; i++) dots += "● ";
        pinDisplay.text = dots;
    }

    IEnumerator ProcessPin()
    {
        yield return new WaitForSeconds(0.2f);

        // Validate session token to prevent CSRF
        if (!ValidateSession())
        {
            statusText.text = "Session expired. Please restart.";
            statusText.color = Color.red;
            yield break;
        }

        if(isSetupMode)
        {
            if(string.IsNullOrEmpty(setupFirstEntry))
            {
                // First entry done, ask for confirm
                setupFirstEntry = currentPin;
                currentPin = "";
                UpdateDisplay();
                isConfirming = true; // Set confirming state
                titleText.text = LocalizationManager.Instance.Get("LOCK_TITLE_CONFIRM");
                statusText.text = LocalizationManager.Instance.Get("LOCK_STATUS_REENTER");
            }
            else
            {
                // Confirming
                if(currentPin == setupFirstEntry)
                {
                    AuthManager.Instance.SetPIN(currentPin);
                    AudioManager.Instance.PlayCreatePIN();
                    statusText.text = LocalizationManager.Instance.Get("LOCK_SUCCESS_SETUP");
                    statusText.color = Color.green;
                    ClearSession();
                    yield return new WaitForSeconds(0.5f);
                    SceneManager.LoadScene(4);
                }
                else
                {
                    // Mismatch
#if UNITY_ANDROID || UNITY_IOS
                    Handheld.Vibrate();
#endif
                    statusText.text = LocalizationManager.Instance.Get("LOCK_MISMATCH");
                    statusText.color = Color.red;
                    currentPin = "";
                    setupFirstEntry = ""; // Restart setup
                    isConfirming = false; // Reset confirming state
                    titleText.text = LocalizationManager.Instance.Get("LOCK_TITLE_CREATE");
                    UpdateDisplay();
                }
            }
        }
        else
        {
            // Auth Mode
            if(AuthManager.Instance.CheckPIN(currentPin))
            {
                statusText.text = LocalizationManager.Instance.Get("LOCK_SUCCESS");
                statusText.color = ThemeManager.Instance.GetSuccessColor();
                AudioManager.Instance.PlayUnlock();
                ClearSession();
                yield return new WaitForSeconds(0.3f);
                
                // Check if returning from transaction PIN verification
                if (PlayerPrefs.GetInt("PendingTxPIN", 0) == 1)
                {
                    // Clear the pending PIN flag so it doesn't trigger again
                    PlayerPrefs.DeleteKey("PendingTxPIN");
                    PlayerPrefs.Save();
                    
                    // Check if it's a TimeLock transaction (create or withdraw)
                    if (PlayerPrefs.GetString("PendingTL", "") == "1" || PlayerPrefs.GetString("PendingTLW", "") == "1")
                    {
                        SceneManager.LoadScene("MainWallet"); // TimeLock is in MainWallet scene
                    }
                    else
                    {
                        SceneManager.LoadScene(7); // Regular Send is scene 7
                    }
                }
                else
                {
                    SceneManager.LoadScene("MainWallet");
                }
            }
            else
            {
                statusText.text = LocalizationManager.Instance.Get("LOCK_FAIL");
                statusText.color = ThemeManager.Instance.GetErrorColor();
                currentPin = "";
                UpdateDisplay();
#if UNITY_ANDROID || UNITY_IOS
                Handheld.Vibrate();
#endif
            }
        }
    }

    bool ValidateSession()
    {
        string storedToken = PlayerPrefs.GetString("AppLockSessionToken", "");
        float storedTime = PlayerPrefs.GetFloat("AppLockSessionTime", 0);
        float elapsed = Time.realtimeSinceStartup - storedTime;
        
        // Session expires after 5 minutes
        return storedToken == sessionToken && elapsed < 300f;
    }

    void ClearSession()
    {
        PlayerPrefs.DeleteKey("AppLockSessionToken");
        PlayerPrefs.DeleteKey("AppLockSessionTime");
        PlayerPrefs.Save();
    }

    // --- UI BUILDER ---
    void BuildUI()
    {
        // Canvas
        GameObject canvasObj = new GameObject("Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.AddComponent<GraphicRaycaster>();

        // BG - Lock Screen Gradient/Solid
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(canvas.transform, false);
        bg.AddComponent<RectTransform>().sizeDelta = new Vector2(1080, 1920);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.12f, 0.2f); // Dark Blue Navy

        // Title
        float y = 600; // Initial Y for title
        titleText = MakeText(canvas.transform, LocalizationManager.Instance.Get("LOCK_TITLE_ENTER"), 0, y, 48, Color.white, FontStyle.Bold);
        y -= 70; // Adjust Y for next element
        
        // Status
        statusText = MakeText(canvas.transform, LocalizationManager.Instance.Get("LOCK_STATUS_CHECK"), 0, y, 32, new Color(0.7f, 0.7f, 0.8f), FontStyle.Normal);

        // PIN Dots
        pinDisplay = MakeText(canvas.transform, "", 0, 350, 80, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);
        // pinDisplay.letterSpacing = 20; // Not supported in standard Text

        // Keypad Base Y
        float startY = 100;
        float spacingY = 220;
        float spacingX = 250;

        // Rows
        // 1 2 3
        MakeKeyRow(canvas.transform, "1", "2", "3", startY);
        MakeKeyRow(canvas.transform, "4", "5", "6", startY - spacingY);
        MakeKeyRow(canvas.transform, "7", "8", "9", startY - spacingY * 2);
        
        // 0 and Back
        MakeKey(canvas.transform, "0", 0, startY - spacingY * 3);
        
        // Backspace
        MakeButton(canvas.transform, "⌫", 250, startY - spacingY * 3, 180, 150, OnDelete, Color.clear);
        
        // Cancel button - only show when verifying transaction PIN
        if (PlayerPrefs.GetInt("PendingTxPIN", 0) == 1)
        {
            string cancelText = "✕ " + (LocalizationManager.Instance?.Get("TX_BTN_CANCEL") ?? "Cancel");
            // Move button down to avoid collision with "0" button (was -140, now -220)
            MakeButton(canvas.transform, cancelText, 0, startY - spacingY * 3 - 220, 300, 100, OnCancelTransaction, new Color(0.8f, 0.2f, 0.2f, 0.9f));
        }
    }
    
    void OnCancelTransaction()
    {
        // Check what type of transaction was pending before clearing
        bool isTimeLock = PlayerPrefs.GetString("PendingTL", "") == "1" || PlayerPrefs.GetString("PendingTLW", "") == "1";
        
        // Clear all pending transaction flags
        PlayerPrefs.DeleteKey("PendingTxPIN");
        PlayerPrefs.DeleteKey("PendingTL");
        PlayerPrefs.DeleteKey("PendingTLW");
        PlayerPrefs.Save();
        ClearSession();
        
        AudioManager.Instance.PlayCloseMenu();
        
        // Return to appropriate scene
        if (isTimeLock)
        {
            SceneManager.LoadScene("MainWallet"); // TimeLock is in MainWallet
        }
        else
        {
            SceneManager.LoadScene(7); // Regular Send is scene 7
        }
    }

    void MakeKeyRow(Transform p, string k1, string k2, string k3, float y)
    {
        MakeKey(p, k1, -250, y);
        MakeKey(p, k2, 0, y);
        MakeKey(p, k3, 250, y);
    }

    void MakeKey(Transform p, string key, float x, float y)
    {
        GameObject k = MakeButton(p, key, x, y, 180, 180, () => OnKeyPress(key), new Color(1,1,1,0.05f));
        // Circle shape
        k.GetComponent<Image>().sprite = Resources.Load<Sprite>("CircleSprite"); // Assuming sprite exists or square
        Text t = k.GetComponentInChildren<Text>();
        t.fontSize = 64;
    }

    Text MakeText(Transform p, string t, float x, float y, int s, Color c, FontStyle st) {
        GameObject o = new GameObject("Txt"); o.transform.SetParent(p,false);
        RectTransform r = o.AddComponent<RectTransform>(); r.anchoredPosition = new Vector2(x,y); r.sizeDelta = new Vector2(800, 100);
        Text tx = o.AddComponent<Text>(); tx.text = t; tx.fontSize = s; tx.color = c; tx.fontStyle = st;

        tx.alignment = TextAnchor.MiddleCenter; tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tx.raycastTarget = false; // Prevent blocking clicks
        return tx;
    }

    GameObject MakeButton(Transform p, string t, float x, float y, float w, float h, UnityEngine.Events.UnityAction a, Color c) {
        GameObject o = new GameObject("Btn_" + t); o.transform.SetParent(p,false);
        RectTransform r = o.AddComponent<RectTransform>(); r.anchoredPosition = new Vector2(x,y); r.sizeDelta = new Vector2(w, h);
        Image img = o.AddComponent<Image>(); img.color = c;
        Button b = o.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(() => AudioManager.Instance.PlayPinButton()); // PIN key sound
        b.onClick.AddListener(a);
        
        GameObject tObj = new GameObject("Txt"); tObj.transform.SetParent(o.transform, false);
        tObj.AddComponent<RectTransform>().sizeDelta = new Vector2(w,h);
        Text tx = tObj.AddComponent<Text>(); tx.text = t; tx.fontSize = 48; tx.color = Color.white;

        tx.alignment = TextAnchor.MiddleCenter; tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tx.raycastTarget = false; // Prevent blocking clicks on button
        return o;
    }
}
