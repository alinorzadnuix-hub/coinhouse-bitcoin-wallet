using UnityEngine;
using UnityEngine.UI;

namespace UnityBitcoinWallet
{
    public class WalletUI : MonoBehaviour
    {
        [Header("UI Elements")]
        public InputField addressField;
        public InputField privateKeyField;
        public InputField seedPhraseField;
        public Dropdown networkDropdown;
        public Button generateButton;
        public Button clearButton;
        public Button balanceButton;

        [Header("Recovery UI")]
        public InputField[] seedWords = new InputField[12];
        public InputField recoveredAddressField;
        public InputField recoveredKeyField;
        public Button recoverButton;

        private WalletManager walletManager;

        void Start()
        {
            walletManager = GetComponent<WalletManager>();
            if (walletManager == null)
                walletManager = gameObject.AddComponent<WalletManager>();

            generateButton.onClick.AddListener(OnGenerateWallet);
            clearButton.onClick.AddListener(OnClear);
            balanceButton.onClick.AddListener(OnCheckBalance);
            recoverButton.onClick.AddListener(OnRecoverWallet);
        }

        void OnGenerateWallet()
        {
            bool isMainnet = networkDropdown.value == 1;
            walletManager.GenerateNewWallet(isMainnet);

            addressField.text = walletManager.GetAddress(isMainnet ? 1 : 0);
            privateKeyField.text = walletManager.GetPrivateKey(isMainnet ? 1 : 0);
            seedPhraseField.text = walletManager.GetSeedPhrase().Replace(" ", "\n");

            Debug.Log($"Wallet generated on {(isMainnet ? "Mainnet" : "TestNet")}");
        }

        void OnClear()
        {
            addressField.text = "";
            privateKeyField.text = "";
            seedPhraseField.text = "";
        }

        async void OnCheckBalance()
        {
            if (string.IsNullOrEmpty(addressField.text))
            {
                Debug.LogWarning("Address is empty!");
                return;
            }

            try
            {
                decimal balance = await walletManager.GetBalance(addressField.text);
                Debug.Log($"Balance: {balance} BTC");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error: {ex.Message}");
            }
        }

        void OnRecoverWallet()
        {
            string seedPhrase = "";
            for (int i = 0; i < 12; i++)
            {
                seedPhrase += seedWords[i].text.Trim() + " ";
            }
            seedPhrase = seedPhrase.Trim();

            try
            {
                walletManager.RecoverWallet(seedPhrase, true);
                recoveredAddressField.text = walletManager.GetAddress(0);
                recoveredKeyField.text = walletManager.GetPrivateKey(0);
                Debug.Log("Wallet recovered successfully!");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Recovery failed: {ex.Message}");
            }
        }
    }
}
