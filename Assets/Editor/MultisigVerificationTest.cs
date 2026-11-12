using System;
using System.Collections.Generic;
using NBitcoin;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class MultisigVerificationTest
{
    [MenuItem("Bitcoin Wallet/Verify Multisig System", false, 50)]
    public static void RunTests()
    {
        Debug.Log("==================================================");
        Debug.Log("🔍 RUNNING COMPREHENSIVE MULTISIG VERIFICATION TESTS");
        Debug.Log("==================================================");

        int passed = 0;
        int failed = 0;

        List<MultisigVaultConfig> preExistingVaults = null;
        try
        {
            var loaded = MultisigCore.LoadAllVaults(true);
            if (loaded != null)
            {
                preExistingVaults = loaded.FindAll(v =>
                    v != null &&
                    v.vaultId != "vault-1" &&
                    v.vaultId != "vault-2" &&
                    v.vaultName != "Verification Test Vault" &&
                    v.vaultName != "Vault 1" &&
                    v.vaultName != "Vault 2"
                );
            }
        }
        catch { }

        try
        {

        // Test 1: Key Generation & Determinism (BIP67)
        try
        {
            List<Key> privKeys = new List<Key>();
            List<PubKey> pubKeys = new List<PubKey>();
            for (int i = 0; i < 5; i++)
            {
                var k = new Key();
                privKeys.Add(k);
                pubKeys.Add(k.PubKey);
            }

            // Shuffle keys
            List<PubKey> reversedKeys = new List<PubKey>(pubKeys);
            reversedKeys.Reverse();

            var res1 = MultisigCore.GenerateMultisigAddress(2, pubKeys, MultisigAddressType.P2WSH, true);
            var res2 = MultisigCore.GenerateMultisigAddress(2, reversedKeys, MultisigAddressType.P2WSH, true);

            if (res1.address == res2.address && res1.redeemScriptHex == res2.redeemScriptHex)
            {
                Debug.Log($"✅ [Test 1 Passed] BIP67 Deterministic Sorting: Keys in different orders produce IDENTICAL address: {res1.address}");
                passed++;
            }
            else
            {
                Debug.LogError("❌ [Test 1 Failed] BIP67 Sorting failed to produce identical addresses!");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [Test 1 Exception]: {ex.Message}");
            failed++;
        }

        // Test 2: Native SegWit (P2WSH) TestNet & MainNet Format Verification
        try
        {
            var k1 = new Key().PubKey;
            var k2 = new Key().PubKey;
            var k3 = new Key().PubKey;
            var testKeys = new List<PubKey> { k1, k2, k3 };

            var testnetP2wsh = MultisigCore.GenerateMultisigAddress(2, testKeys, MultisigAddressType.P2WSH, true);
            var mainnetP2wsh = MultisigCore.GenerateMultisigAddress(2, testKeys, MultisigAddressType.P2WSH, false);

            bool testnetValid = testnetP2wsh.address.StartsWith("tb1q") && testnetP2wsh.address.Length == 62;
            bool mainnetValid = mainnetP2wsh.address.StartsWith("bc1q") && mainnetP2wsh.address.Length == 62;

            if (testnetValid && mainnetValid)
            {
                Debug.Log($"✅ [Test 2 Passed] P2WSH Format Valid: TestNet={testnetP2wsh.address} | MainNet={mainnetP2wsh.address}");
                passed++;
            }
            else
            {
                Debug.LogError($"❌ [Test 2 Failed] Invalid P2WSH addresses: TestNet={testnetP2wsh.address}, MainNet={mainnetP2wsh.address}");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [Test 2 Exception]: {ex.Message}");
            failed++;
        }

        // Test 3: Nested SegWit (P2SH-P2WSH) Format Verification
        try
        {
            var k1 = new Key().PubKey;
            var k2 = new Key().PubKey;
            var testKeys = new List<PubKey> { k1, k2 };

            var testnetP2shP2wsh = MultisigCore.GenerateMultisigAddress(2, testKeys, MultisigAddressType.P2SH_P2WSH, true);
            var mainnetP2shP2wsh = MultisigCore.GenerateMultisigAddress(2, testKeys, MultisigAddressType.P2SH_P2WSH, false);

            bool testnetValid = testnetP2shP2wsh.address.StartsWith("2");
            bool mainnetValid = mainnetP2shP2wsh.address.StartsWith("3");

            if (testnetValid && mainnetValid)
            {
                Debug.Log($"✅ [Test 3 Passed] P2SH-P2WSH Format Valid: TestNet={testnetP2shP2wsh.address} | MainNet={mainnetP2shP2wsh.address}");
                passed++;
            }
            else
            {
                Debug.LogError($"❌ [Test 3 Failed] Invalid P2SH-P2WSH addresses: TestNet={testnetP2shP2wsh.address}, MainNet={mainnetP2shP2wsh.address}");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [Test 3 Exception]: {ex.Message}");
            failed++;
        }

        // Test 4: Legacy (P2SH) Format Verification
        try
        {
            var k1 = new Key().PubKey;
            var k2 = new Key().PubKey;
            var testKeys = new List<PubKey> { k1, k2 };

            var testnetP2sh = MultisigCore.GenerateMultisigAddress(2, testKeys, MultisigAddressType.P2SH, true);
            var mainnetP2sh = MultisigCore.GenerateMultisigAddress(2, testKeys, MultisigAddressType.P2SH, false);

            bool testnetValid = testnetP2sh.address.StartsWith("2");
            bool mainnetValid = mainnetP2sh.address.StartsWith("3");

            if (testnetValid && mainnetValid)
            {
                Debug.Log($"✅ [Test 4 Passed] Legacy P2SH Format Valid: TestNet={testnetP2sh.address} | MainNet={mainnetP2sh.address}");
                passed++;
            }
            else
            {
                Debug.LogError($"❌ [Test 4 Failed] Invalid Legacy P2SH addresses: TestNet={testnetP2sh.address}, MainNet={mainnetP2sh.address}");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [Test 4 Exception]: {ex.Message}");
            failed++;
        }

        // Test 5: Vault Config JSON Serialization & Persistence
        try
        {
            MultisigCore.DeleteVaultConfig(true);

            var sampleConfig = new MultisigVaultConfig
            {
                vaultName = "Verification Test Vault",
                requiredSignatures = 2,
                totalKeys = 5,
                addressTypeInt = 0,
                isTestNet = true,
                multisigAddress = "tb1qtestverificationaddress00000000000000000000000000000000000",
                redeemScriptHex = "522102aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa2102bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb52ae"
            };

            MultisigCore.SaveVaultConfig(sampleConfig);
            var loaded = MultisigCore.LoadVaultConfig(true);

            bool isValid = loaded != null && 
                           loaded.vaultName == sampleConfig.vaultName && 
                           loaded.requiredSignatures == sampleConfig.requiredSignatures &&
                           loaded.totalKeys == sampleConfig.totalKeys &&
                           loaded.multisigAddress == sampleConfig.multisigAddress;

            if (!isValid)
            {
                var allVaults = MultisigCore.LoadAllVaults(true);
                var found = allVaults?.Find(v => v != null && v.vaultName == sampleConfig.vaultName);
                if (found != null && 
                    found.requiredSignatures == sampleConfig.requiredSignatures &&
                    found.totalKeys == sampleConfig.totalKeys &&
                    found.multisigAddress == sampleConfig.multisigAddress)
                {
                    isValid = true;
                    loaded = found;
                }
            }

            if (isValid)
            {
                Debug.Log($"✅ [Test 5 Passed] Vault Config Save/Load and JSON Persistence works perfectly: {loaded.vaultName}");
                passed++;
            }
            else
            {
                Debug.LogError("❌ [Test 5 Failed] Failed to reload saved config.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [Test 5 Exception]: {ex.Message}");
            failed++;
        }

        // Test 6: Transaction Signing Simulation (2-of-3 Multisig Spend Verification)
        try
        {
            var net = Network.TestNet;
            var key1 = new Key();
            var key2 = new Key();
            var key3 = new Key();
            var pks = new List<PubKey> { key1.PubKey, key2.PubKey, key3.PubKey };

            var (msAddressStr, redeemScriptHex) = MultisigCore.GenerateMultisigAddress(2, pks, MultisigAddressType.P2WSH, true);
            var msAddress = BitcoinAddress.Create(msAddressStr, net);
            var redeemScript = new Script(NBitcoin.DataEncoders.Encoders.Hex.DecodeData(redeemScriptHex));

            // Simulate funding coin (0.01 BTC at txid: 1111... vout: 0)
            uint256 fakeFundingTxId = new uint256("1111111111111111111111111111111111111111111111111111111111111111");
            var outpoint = new OutPoint(fakeFundingTxId, 0);
            var txOut = new TxOut(Money.Coins(0.01m), msAddress.ScriptPubKey);
            var coin = new Coin(outpoint, txOut);
            var scriptCoin = new ScriptCoin(coin, redeemScript);

            // Create spending transaction sending 0.009 BTC to a destination
            var destKey = new Key();
            var destAddress = destKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, net);

            TransactionBuilder builder = net.CreateTransactionBuilder();
            var tx = builder
                .AddCoins(scriptCoin)
                .AddKeys(key1, key2) // Sign with Key 1 and Key 2 (2 of 3)
                .Send(destAddress, Money.Coins(0.009m))
                .SetChange(msAddress)
                .SendFees(Money.Coins(0.001m))
                .BuildTransaction(sign: true);

            bool verified = builder.Verify(tx);
            if (verified)
            {
                Debug.Log($"✅ [Test 6 Passed] Full Multisig Transaction Signing & Verification SUCCESS! TxId: {tx.GetHash()}");
                passed++;
            }
            else
            {
                Debug.LogError("❌ [Test 6 Failed] Multisig transaction verification failed.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [Test 6 Exception]: {ex.Message}");
            failed++;
        }

        // Test 7: Multi-Vault List & Dedicated 12-Word Seed Generation
        try
        {
            var (mnem, pubKey, privKey, zpub) = MultisigCore.GenerateVaultKeypair(true);
            var words = mnem.Split(' ');
            if (words.Length == 12 && pubKey != null && privKey != null && !string.IsNullOrEmpty(zpub))
            {
                var v1 = new MultisigVaultConfig { vaultId = "vault-1", vaultName = "Vault 1", vaultMnemonic = mnem, isTestNet = true };
                var v2 = new MultisigVaultConfig { vaultId = "vault-2", vaultName = "Vault 2", isTestNet = true };
                var list = new List<MultisigVaultConfig> { v1, v2 };
                MultisigCore.SaveAllVaults(list, true);
                var reloaded = MultisigCore.LoadAllVaults(true);

                if (reloaded.Count >= 2 && reloaded.Exists(x => x.vaultId == "vault-1" && !string.IsNullOrEmpty(x.vaultMnemonic)))
                {
                    Debug.Log($"✅ [Test 7 Passed] Multi-Vault Persistence & Dedicated 12-Word Passphrase Generator verified.");
                    passed++;
                }
                else
                {
                    Debug.LogError("❌ [Test 7 Failed] Failed to reload multi-vault list.");
                    failed++;
                }
            }
            else
            {
                Debug.LogError("❌ [Test 7 Failed] Mnemonic word count was not 12.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [Test 7 Exception]: {ex.Message}");
            failed++;
        }

        // Test 8: Redeem Script Hex Parser & Import Reconstitution
        try
        {
            var kA = new Key().PubKey;
            var kB = new Key().PubKey;
            var (origAddr, origRedeemScript) = MultisigCore.GenerateMultisigAddress(2, new List<PubKey> { kA, kB }, MultisigAddressType.P2WSH, true);

            var (m, n, pubKeys, parsedAddr) = MultisigCore.ParseRedeemScript(origRedeemScript, MultisigAddressType.P2WSH, true);
            if (m == 2 && n == 2 && parsedAddr == origAddr && pubKeys.Count == 2)
            {
                Debug.Log($"✅ [Test 8 Passed] Redeem Script Hex Parser accurately reconstituted 2-of-2 Multisig address: {parsedAddr}");
                passed++;
            }
            else
            {
                Debug.LogError($"❌ [Test 8 Failed] ParseRedeemScript mismatch: orig={origAddr}, parsed={parsedAddr}");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [Test 8 Exception]: {ex.Message}");
            failed++;
        }

        // Test 9: Zpub (BIP84) Parsing & Child Key Derivation
        try
        {
            var (_, _, _, testZpub) = MultisigCore.GenerateVaultKeypair(true);
            var (derivedPubKey, canonicalHex, _) = MultisigCore.ParseZpubOrPubKey(testZpub, true, 0);

            if (derivedPubKey != null && !string.IsNullOrEmpty(canonicalHex) && canonicalHex.Length == 66)
            {
                Debug.Log($"✅ [Test 9 Passed] Zpub parsed and derived compressed public key: {canonicalHex}");
                passed++;
            }
            else
            {
                Debug.LogError("❌ [Test 9 Failed] Failed to parse or derive public key from Zpub.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [Test 9 Exception]: {ex.Message}");
            failed++;
        }

        }
        finally
        {
            try
            {
                if (preExistingVaults != null && preExistingVaults.Count > 0)
                {
                    MultisigCore.SaveAllVaults(preExistingVaults, true);
                }
                else
                {
                    MultisigCore.DeleteVaultConfig(true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MultisigVerificationTest] Cleanup warning: {ex.Message}");
            }
        }

        Debug.Log("==================================================");
        Debug.Log($"🎯 MULTISIG TEST RESULTS: {passed} PASSED / {failed} FAILED");
        Debug.Log("==================================================");
    }
}
