using UnityEngine;

/// <summary>
/// مدیریت مرکزی صداهای برنامه کیف پول بیتکوین.
/// تمام صداهای موجود در پوشه Resources بارگذاری و مدیریت می‌شوند.
/// </summary>
public class AudioManager : MonoBehaviour
{
    private static AudioManager _instance;
    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<AudioManager>();
                if (_instance == null)
                {
                    GameObject obj = new GameObject("AudioManager");
                    _instance = obj.AddComponent<AudioManager>();
                    DontDestroyOnLoad(obj);
                }
            }
            return _instance;
        }
    }

    private AudioSource audioSource;

    // ===== Audio Clips - همه صداهای موجود در Resources =====
    
    // دکمه‌های عمومی
    private AudioClip clipButtonPress;       // buttonpress.wav       - کلیک عمومی دکمه‌ها
    private AudioClip clipBackButton;        // backbutton.ogg        - دکمه بازگشت
    private AudioClip clipCloseMenu;         // closemenu.wav         - بستن منو/overlay
    private AudioClip clipNextPage;          // nextpage.wav          - رفتن به صفحه بعد
    
    // آدرس و کپی
    private AudioClip clipCopyAddress;       // copyaddressbutton.wav - کپی آدرس
    
    // PIN و امنیت
    private AudioClip clipPinButton;         // PIN button.ogg        - فشردن کلید PIN
    private AudioClip clipPinType;           // type_05.wav           - تایپ در فیلد PIN
    private AudioClip clipCreatePinComplete; // create new PIN complete.wav - ساخت PIN تکمیل شد
    private AudioClip clipUnlockWallet;      // unlockwallet.wav      - باز کردن کیف پول
    private AudioClip clipWelcomeBack;       // welcomeback.wav       - خوش‌آمد / اولین ورود
    
    // ماینینگ
    private AudioClip clipMiningButton;      // miningbutton.wav      - شروع/توقف ماینینگ
    
    // فاست
    private AudioClip clipBitcoinTap;        // bitcointap.wav        - صدای کلیک روی لوگوی faucet

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeAudio();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    void InitializeAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.volume = 1f;

        // ===== بارگذاری تمام صداها از پوشه Resources =====
        
        clipButtonPress       = Resources.Load<AudioClip>("buttonpress");
        clipBackButton        = Resources.Load<AudioClip>("backbutton");
        clipCloseMenu         = Resources.Load<AudioClip>("closemenu");
        clipNextPage          = Resources.Load<AudioClip>("nextpage");
        clipCopyAddress       = Resources.Load<AudioClip>("copyaddressbutton");
        clipPinButton         = Resources.Load<AudioClip>("PIN button");
        clipPinType           = Resources.Load<AudioClip>("type_05");
        clipCreatePinComplete = Resources.Load<AudioClip>("create new PIN complete");
        clipUnlockWallet      = Resources.Load<AudioClip>("unlockwallet");
        clipWelcomeBack       = Resources.Load<AudioClip>("welcomeback");
        clipMiningButton      = Resources.Load<AudioClip>("miningbutton");
        clipBitcoinTap        = Resources.Load<AudioClip>("bitcointap");

        // گزارش بارگذاری
        int loaded = 0;
        if (clipButtonPress != null)       { loaded++; } else { Debug.LogWarning("[AudioManager] buttonpress.wav not found"); }
        if (clipBackButton != null)        { loaded++; } else { Debug.LogWarning("[AudioManager] backbutton.ogg not found"); }
        if (clipCloseMenu != null)         { loaded++; } else { Debug.LogWarning("[AudioManager] closemenu.wav not found"); }
        if (clipNextPage != null)          { loaded++; } else { Debug.LogWarning("[AudioManager] nextpage.wav not found"); }
        if (clipCopyAddress != null)       { loaded++; } else { Debug.LogWarning("[AudioManager] copyaddressbutton.wav not found"); }
        if (clipPinButton != null)         { loaded++; } else { Debug.LogWarning("[AudioManager] PIN button.ogg not found"); }
        if (clipPinType != null)           { loaded++; } else { Debug.LogWarning("[AudioManager] type_05.wav not found"); }
        if (clipCreatePinComplete != null) { loaded++; } else { Debug.LogWarning("[AudioManager] create new PIN complete.wav not found"); }
        if (clipUnlockWallet != null)      { loaded++; } else { Debug.LogWarning("[AudioManager] unlockwallet.wav not found"); }
        if (clipWelcomeBack != null)       { loaded++; } else { Debug.LogWarning("[AudioManager] welcomeback.wav not found"); }
        if (clipMiningButton != null)      { loaded++; } else { Debug.LogWarning("[AudioManager] miningbutton.wav not found"); }
        if (clipBitcoinTap != null)        { loaded++; } else { Debug.LogWarning("[AudioManager] bitcointap.wav not found"); }
        
        Debug.Log($"[AudioManager] ✅ {loaded}/12 audio clips loaded from Resources.");
    }

    // ===== متد اجرای صدا =====
    private void PlayClip(AudioClip clip)
    {
        if (PlayerPrefs.GetInt("SoundEnabled", 1) != 1) return;
        if (audioSource == null || clip == null) return;
        audioSource.PlayOneShot(clip);
    }

    // ===== متدهای عمومی دکمه‌ها =====
    
    /// <summary>صدای کلیک عمومی برای همه دکمه‌ها</summary>
    public void PlayClick()
    {
        PlayClip(clipButtonPress);
    }

    /// <summary>صدای دکمه بازگشت (Back)</summary>
    public void PlayBack()
    {
        PlayClip(clipBackButton);
    }

    /// <summary>صدای بستن منو یا overlay</summary>
    public void PlayCloseMenu()
    {
        PlayClip(clipCloseMenu);
    }

    /// <summary>صدای رفتن به صفحه بعد</summary>
    public void PlayNextPage()
    {
        PlayClip(clipNextPage);
    }

    // ===== متدهای آدرس =====
    
    /// <summary>صدای کپی کردن آدرس</summary>
    public void PlayCopy()
    {
        PlayClip(clipCopyAddress);
    }

    // ===== متدهای PIN و امنیت =====
    
    /// <summary>صدای فشردن دکمه‌های صفحه کلید PIN</summary>
    public void PlayPinButton()
    {
        PlayClip(clipPinButton);
    }

    /// <summary>صدای تایپ در فیلدهای ورودی PIN</summary>
    public void PlayPinType()
    {
        PlayClip(clipPinType);
    }

    /// <summary>صدای تکمیل موفق ساخت PIN</summary>
    public void PlayCreatePIN()
    {
        PlayClip(clipCreatePinComplete);
    }

    /// <summary>صدای باز شدن کیف پول پس از ورود PIN صحیح</summary>
    public void PlayUnlock()
    {
        PlayClip(clipUnlockWallet);
    }

    /// <summary>صدای خوش‌آمدگویی / اولین ورود به برنامه</summary>
    public void PlayWelcomeBack()
    {
        PlayClip(clipWelcomeBack);
    }

    // ===== متدهای ماینینگ =====
    
    /// <summary>صدای شروع یا توقف ماینینگ</summary>
    public void PlayMining()
    {
        PlayClip(clipMiningButton);
    }
    
    /// <summary>صدای کلیک روی لوگوی faucet (Bitcoin Tap)</summary>
    public void PlayBitcoinTap()
    {
        PlayClip(clipBitcoinTap);
    }
}
