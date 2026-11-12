using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneNavigator : MonoBehaviour
{
    public static void GoToMainWallet()
    {
        SceneManager.LoadScene("MainWallet");
    }

    public static void GoToImportWallet()
    {
        SceneManager.LoadScene("ImportWallet");
    }

    public static void GoToCreateWallet()
    {
        SceneManager.LoadScene("CreateWallet");
    }

    public static void GoToMenu()
    {
        SceneManager.LoadScene("Menu");
    }

    public static void QuitApp()
    {
        Application.Quit();
    }
}

