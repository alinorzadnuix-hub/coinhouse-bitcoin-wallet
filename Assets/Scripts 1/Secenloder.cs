using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
public class Secenloder : MonoBehaviour
{
        public static void Wallet()
    {
        GameSceneManager.Instance.LoadWalletScene("MainWallet");
    }

       public static void GoTorecoverreWallet()
    {
        GameSceneManager.Instance.LoadWalletScene("RecoverWallet");
    }

    public static void GoToCreatwalettWallet()
    {
        GameSceneManager.Instance.LoadWalletScene("CreateWallet");
    }
  


}
