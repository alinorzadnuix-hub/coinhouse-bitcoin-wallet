using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class LoginUI : MonoBehaviour
{
    void Start()
    {
        // Go directly to app (Scene 1 = AppLock/MainWallet)
        SceneManager.LoadScene(1);
    }
}
