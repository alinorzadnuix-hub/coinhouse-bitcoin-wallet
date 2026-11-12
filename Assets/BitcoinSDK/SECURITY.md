# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |

## Security Best Practices

### For Developers Using This SDK

#### 1. Seed Phrase Security
- **NEVER** store seed phrases in plain text
- **ALWAYS** encrypt seed phrases before saving
- Use strong encryption (AES-256 or better)
- Consider hardware security modules for production
- Never log seed phrases or private keys

#### 2. Private Key Management
- Never expose private keys in code
- Never commit private keys to version control
- Use secure key derivation functions
- Implement proper key rotation policies

#### 3. Network Security
- Always use HTTPS for API calls
- Validate SSL certificates
- Implement certificate pinning for production
- Use secure random number generation

#### 4. Input Validation
- Validate all user inputs
- Sanitize addresses before use
- Check amount ranges
- Verify transaction details

#### 5. Testing
- **ALWAYS** test on TestNet first
- Never test with real funds during development
- Implement comprehensive unit tests
- Perform security audits before production

#### 6. User Confirmations
- Require user confirmation for transactions
- Display clear transaction details
- Implement transaction limits
- Add optional 2FA/PIN protection

#### 7. Error Handling
- Never expose sensitive data in error messages
- Log errors securely
- Implement proper exception handling
- Provide user-friendly error messages

#### 8. Code Security
- Keep dependencies updated
- Use code obfuscation for production
- Implement anti-tampering measures
- Regular security audits

## Known Security Considerations

### 1. API Rate Limiting
- SDK uses BlockCypher free tier
- Rate limits: 200 requests/hour, 3 requests/second
- Implement proper error handling for rate limits

### 2. Seed Phrase Storage
- SDK does NOT provide encryption
- Developers must implement their own encryption
- Consider using Unity's built-in encryption or third-party libraries

### 3. Network Type
- Ensure correct network (MainNet vs TestNet)
- Validate addresses match network type
- Prevent accidental MainNet transactions during testing

### 4. Transaction Verification
- Always verify transaction details before broadcasting
- Check recipient address carefully
- Verify amounts and fees
- Implement transaction preview

### 5. UTXO Management
- SDK automatically selects UTXOs
- Large wallets may have many UTXOs
- Consider UTXO consolidation strategies

## Reporting a Vulnerability

If you discover a security vulnerability in this SDK:

1. **DO NOT** open a public issue
2. **DO NOT** disclose the vulnerability publicly
3. Contact the maintainers privately
4. Provide detailed information about the vulnerability
5. Allow reasonable time for a fix before disclosure

### What to Include in Your Report

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)
- Your contact information

### Response Timeline

- Initial response: Within 48 hours
- Status update: Within 7 days
- Fix timeline: Depends on severity
  - Critical: 1-7 days
  - High: 7-14 days
  - Medium: 14-30 days
  - Low: 30-90 days

## Security Checklist for Production

Before deploying to production, ensure:

- [ ] Seed phrases are encrypted
- [ ] Private keys are never logged
- [ ] All testing done on TestNet
- [ ] Input validation implemented
- [ ] User confirmations required
- [ ] Error handling complete
- [ ] Security audit performed
- [ ] Code obfuscation applied
- [ ] SSL certificate validation enabled
- [ ] Rate limiting handled
- [ ] Backup and recovery tested
- [ ] Documentation reviewed
- [ ] Team trained on security practices

## Encryption Example

```csharp
// Example: Encrypting seed phrase (implement your own encryption)
public static string EncryptSeedPhrase(string seedPhrase, string password)
{
    // Use AES-256 or similar
    // This is just a placeholder - implement proper encryption!
    // Consider using:
    // - System.Security.Cryptography.Aes
    // - Third-party libraries like BouncyCastle
    // - Hardware security modules
    
    // NEVER use this example in production:
    // return Convert.ToBase64String(Encoding.UTF8.GetBytes(seedPhrase));
    
    throw new NotImplementedException("Implement proper encryption!");
}

public static string DecryptSeedPhrase(string encryptedSeed, string password)
{
    // Implement proper decryption
    throw new NotImplementedException("Implement proper decryption!");
}
```

## Additional Resources

- [Bitcoin Security Best Practices](https://bitcoin.org/en/secure-your-wallet)
- [OWASP Mobile Security](https://owasp.org/www-project-mobile-security/)
- [Unity Security Guidelines](https://docs.unity3d.com/Manual/Security.html)
- [Cryptocurrency Security Standards](https://www.ccss.info/)

## Disclaimer

This SDK is provided as-is without warranty. Users are responsible for implementing proper security measures. The authors and contributors are not liable for any security breaches or loss of funds.

**USE AT YOUR OWN RISK.**

---

**Last Updated:** 2024
**Version:** 1.0.0
