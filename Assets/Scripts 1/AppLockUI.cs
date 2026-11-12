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
    private bool isProcessing = false; // prevent double-entry during coroutine
    private bool isDesktopPlatform = false;
    private static bool hasShownColdStartSplash = false;
    private static bool isSplashPlaying = false;
    private GameObject mainCanvasObj;

    void Start()
    {
        // Ensure EventSystem exists (Critical for UI interaction)
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
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

#if UNITY_STANDALONE && !UNITY_EDITOR
        isDesktopPlatform = true;
#elif UNITY_EDITOR
        isDesktopPlatform = (Screen.width > Screen.height);
#endif

        // Prevent screen recording and screenshots on Android while entering password
        ScreenSecurityManager.EnableProtection();

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

        if (!hasShownColdStartSplash)
        {
            hasShownColdStartSplash = true;
            StartCoroutine(ShowSplashIntroRoutine());
        }
    }

    // ── Keyboard input support (Windows / Desktop) ──────────────────────────
    void Update()
    {
        if (!isDesktopPlatform || isProcessing || isSplashPlaying) return;

        // Digit row and numpad 0-9
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + i)) ||
                Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad0 + i)))
            {
                OnKeyPress(i.ToString());
                break;
            }
        }

        // Backspace = delete last digit
        if (Input.GetKeyDown(KeyCode.Backspace))
            OnDelete();
    }

    void OnKeyPress(string key)
    {
        if (isProcessing || isSplashPlaying) return;
        if(currentPin.Length < 7)
        {
            currentPin += key;
            UpdateDisplay();
            
            if(currentPin.Length == 7)
            {
                isProcessing = true;
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
        isProcessing = true;
        yield return new WaitForSeconds(0.2f);

        // Validate session token to prevent CSRF
        if (!ValidateSession())
        {
            statusText.text = LocalizationManager.Instance.Get("APPLOCK_SESSION_EXPIRED");
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
                    ScreenSecurityManager.DisableProtection();
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
                ScreenSecurityManager.DisableProtection();
                
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
                isProcessing = false;
                UpdateDisplay();
#if UNITY_ANDROID || UNITY_IOS
                Handheld.Vibrate();
#endif
            }
        }
        isProcessing = false;
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
        mainCanvasObj = canvasObj;
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        
        bool isDesktop = isDesktopPlatform;

        if (isDesktop)
        {
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }
        else
        {
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
        }
        
        canvasObj.AddComponent<GraphicRaycaster>();

        // BG - Lock Screen Gradient/Solid
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(canvas.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.12f, 0.2f); // Dark Blue Navy

        if (isDesktop)
        {
            // Desktop Split Layout (Title, status, dots on left; Compact keypad on right)
            titleText = MakeText(canvas.transform, LocalizationManager.Instance.Get("LOCK_TITLE_ENTER"), -450, 220, 48, Color.white, FontStyle.Bold);
            statusText = MakeText(canvas.transform, LocalizationManager.Instance.Get("LOCK_STATUS_CHECK"), -450, 120, 32, new Color(0.7f, 0.7f, 0.8f), FontStyle.Normal);
            pinDisplay = MakeText(canvas.transform, "", -450, -50, 80, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);

            float startY = 220;
            float spacingY = 150;
            float centerX = 430;
            float spacingX = 160;
            float keySize = 130;

            MakeKeyRow(canvas.transform, "1", "2", "3", startY, centerX, spacingX, keySize);
            MakeKeyRow(canvas.transform, "4", "5", "6", startY - spacingY, centerX, spacingX, keySize);
            MakeKeyRow(canvas.transform, "7", "8", "9", startY - spacingY * 2, centerX, spacingX, keySize);
            
            MakeKey(canvas.transform, "0", centerX, startY - spacingY * 3, keySize);
            string deleteText = "Delete";
            GameObject deleteBtn = MakeButton(canvas.transform, deleteText, centerX + spacingX, startY - spacingY * 3, keySize, keySize, OnDelete, Color.clear);
            Text deleteBtnTxt = deleteBtn.GetComponentInChildren<Text>();
            if (deleteBtnTxt != null) deleteBtnTxt.fontSize = 24;

            // Keyboard hint label
            MakeText(canvas.transform, LocalizationManager.Instance.Get("APPLOCK_KEYBOARD_HINT"), centerX, startY - spacingY * 3 - 85, 22, new Color(0.5f, 0.6f, 0.8f, 0.7f), FontStyle.Italic);
            
            if (PlayerPrefs.GetInt("PendingTxPIN", 0) == 1)
            {
                string cancelText = "✕ " + (LocalizationManager.Instance?.Get("TX_BTN_CANCEL") ?? "Cancel");
                MakeButton(canvas.transform, cancelText, centerX, startY - spacingY * 3 - 140, 280, 80, OnCancelTransaction, new Color(0.8f, 0.2f, 0.2f, 0.9f));
            }
        }
        else
        {
            // Mobile portrait layout (original)
            float y = 600;
            titleText = MakeText(canvas.transform, LocalizationManager.Instance.Get("LOCK_TITLE_ENTER"), 0, y, 48, Color.white, FontStyle.Bold);
            y -= 70;
            
            statusText = MakeText(canvas.transform, LocalizationManager.Instance.Get("LOCK_STATUS_CHECK"), 0, y, 32, new Color(0.7f, 0.7f, 0.8f), FontStyle.Normal);
            pinDisplay = MakeText(canvas.transform, "", 0, 350, 80, ThemeManager.Instance.GetAccentColor(), FontStyle.Bold);

            float startY = 100;
            float spacingY = 220;
            float spacingX = 250;
            float keySize = 180;

            MakeKeyRow(canvas.transform, "1", "2", "3", startY, 0, spacingX, keySize);
            MakeKeyRow(canvas.transform, "4", "5", "6", startY - spacingY, 0, spacingX, keySize);
            MakeKeyRow(canvas.transform, "7", "8", "9", startY - spacingY * 2, 0, spacingX, keySize);
            
            MakeKey(canvas.transform, "0", 0, startY - spacingY * 3, keySize);
            
            string mDeleteText = "⌫";
#if UNITY_STANDALONE || UNITY_EDITOR
            mDeleteText = "Delete";
#endif
            GameObject mDeleteBtn = MakeButton(canvas.transform, mDeleteText, spacingX, startY - spacingY * 3, keySize, 150, OnDelete, Color.clear);
#if UNITY_STANDALONE || UNITY_EDITOR
            Text mDeleteBtnTxt = mDeleteBtn.GetComponentInChildren<Text>();
            if (mDeleteBtnTxt != null) mDeleteBtnTxt.fontSize = 28;
#endif
            
            if (PlayerPrefs.GetInt("PendingTxPIN", 0) == 1)
            {
                string cancelText = "✕ " + (LocalizationManager.Instance?.Get("TX_BTN_CANCEL") ?? "Cancel");
                MakeButton(canvas.transform, cancelText, 0, startY - spacingY * 3 - 220, 300, 100, OnCancelTransaction, new Color(0.8f, 0.2f, 0.2f, 0.9f));
            }
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
        ScreenSecurityManager.DisableProtection();
        
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

    void MakeKeyRow(Transform p, string k1, string k2, string k3, float y, float centerX, float spacingX, float size)
    {
        MakeKey(p, k1, centerX - spacingX, y, size);
        MakeKey(p, k2, centerX, y, size);
        MakeKey(p, k3, centerX + spacingX, y, size);
    }

    void MakeKey(Transform p, string key, float x, float y, float size)
    {
        GameObject k = MakeButton(p, key, x, y, size, size, () => OnKeyPress(key), new Color(1,1,1,0.05f));
        k.GetComponent<Image>().sprite = Resources.Load<Sprite>("CircleSprite");
        Text t = k.GetComponentInChildren<Text>();
        t.fontSize = size > 150 ? 64 : 48;
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

    private IEnumerator ShowSplashIntroRoutine()
    {
        isSplashPlaying = true;

        // 1. Create a Fullscreen Splash Overlay on top of AppLock canvas
        GameObject splashObj = new GameObject("ColdStartSplashIntro");
        splashObj.layer = 5;
        splashObj.transform.SetParent(mainCanvasObj != null ? mainCanvasObj.transform : transform, false);

        RectTransform splashRT = splashObj.AddComponent<RectTransform>();
        splashRT.anchorMin = Vector2.zero;
        splashRT.anchorMax = Vector2.one;
        splashRT.offsetMin = Vector2.zero;
        splashRT.offsetMax = Vector2.zero;

        Image splashBg = splashObj.AddComponent<Image>();
        splashBg.color = Color.black; // Solid black background matching Unity logo intro

        CanvasGroup rootCg = splashObj.AddComponent<CanvasGroup>();
        rootCg.alpha = 1f;

        // 2. Satolite Rainbow Logo in Center
        GameObject logoObj = new GameObject("SplashSatoliteLogo");
        logoObj.layer = 5;
        logoObj.transform.SetParent(splashObj.transform, false);

        RectTransform logoRT = logoObj.AddComponent<RectTransform>();
        logoRT.anchorMin = new Vector2(0.5f, 0.5f);
        logoRT.anchorMax = new Vector2(0.5f, 0.5f);
        logoRT.pivot = new Vector2(0.5f, 0.5f);
        logoRT.anchoredPosition = Vector2.zero;
        float logoSize = isDesktopPlatform ? 260f : 320f;
        logoRT.sizeDelta = new Vector2(logoSize, logoSize);

        Image logoImg = logoObj.AddComponent<Image>();
        Sprite satSprite = Resources.Load<Sprite>("splash_screen")
                        ?? Resources.Load<Sprite>("splash scren")
                        ?? Resources.Load<Sprite>("satolite_logo")
                        ?? Resources.Load<Sprite>("y");
        if (satSprite != null)
        {
            logoImg.sprite = satSprite;
            logoImg.color = Color.white;
            logoImg.preserveAspect = true;

            if (satSprite.name.ToLower().Contains("splash") || satSprite.rect.height > satSprite.rect.width * 1.3f)
            {
                logoRT.anchorMin = Vector2.zero;
                logoRT.anchorMax = Vector2.one;
                logoRT.offsetMin = Vector2.zero;
                logoRT.offsetMax = Vector2.zero;
            }
        }

        CanvasGroup logoCg = logoObj.AddComponent<CanvasGroup>();
        logoCg.alpha = 0f;

        splashObj.transform.SetAsLastSibling();

        // 1. Fade In Logo (0 to 1 over ~0.8s) against solid black
        float elapsed = 0f;
        float fadeInDuration = 0.8f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            logoCg.alpha = Mathf.Clamp01(elapsed / fadeInDuration);
            yield return null;
        }
        logoCg.alpha = 1f;

        // 2. Hold Logo for 0.8s
        yield return new WaitForSecondsRealtime(0.8f);

        // 3. Fade Out Logo (1 to 0 over ~0.6s)
        elapsed = 0f;
        float fadeOutLogoDuration = 0.6f;
        while (elapsed < fadeOutLogoDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            logoCg.alpha = Mathf.Clamp01(1f - (elapsed / fadeOutLogoDuration));
            yield return null;
        }
        logoCg.alpha = 0f;

        // 4. Fade Out Background (1 to 0 over ~0.4s) smoothly revealing AppLock UI
        elapsed = 0f;
        float fadeOutDuration = 0.4f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            rootCg.alpha = Mathf.Clamp01(1f - (elapsed / fadeOutDuration));
            yield return null;
        }
        rootCg.alpha = 0f;

        Destroy(splashObj);
        isSplashPlaying = false;
    }

    void OnDestroy()
    {
        ScreenSecurityManager.DisableProtection();
    }
}
