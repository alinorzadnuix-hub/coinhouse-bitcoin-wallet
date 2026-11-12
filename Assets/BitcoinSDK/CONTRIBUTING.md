# Contributing to Bitcoin Wallet SDK

Thank you for your interest in contributing to the Bitcoin Wallet SDK for Unity!

## How to Contribute

### Reporting Bugs

1. Check if the bug has already been reported
2. Use the bug report template
3. Include:
   - Unity version
   - SDK version
   - Network type (MainNet/TestNet)
   - Steps to reproduce
   - Expected behavior
   - Actual behavior
   - Error messages
   - Screenshots (if applicable)

### Suggesting Features

1. Check if the feature has been suggested
2. Describe the feature clearly
3. Explain the use case
4. Provide examples if possible

### Code Contributions

#### Before You Start

1. Fork the repository
2. Create a new branch
3. Test on TestNet first
4. Follow coding standards
5. Write clear commit messages

#### Coding Standards

- Use C# naming conventions
- Add XML documentation comments
- Keep methods focused and small
- Write self-documenting code
- Minimize comments (code should be clear)
- Follow existing code style

#### Testing Requirements

- Test all changes on TestNet
- Add unit tests for new features
- Ensure existing tests pass
- Test on multiple Unity versions
- Test on different platforms (if applicable)

#### Pull Request Process

1. Update documentation
2. Add tests
3. Update CHANGELOG.md
4. Ensure all tests pass
5. Request review
6. Address feedback
7. Wait for approval

### Documentation Contributions

- Fix typos and errors
- Improve clarity
- Add examples
- Update outdated information
- Translate documentation

## Development Setup

### Prerequisites

- Unity 2019.4 or higher
- .NET 4.x
- NBitcoin library
- Newtonsoft.Json library
- Git

### Setup Steps

1. Clone the repository
2. Open in Unity
3. Install dependencies
4. Configure TestNet
5. Run examples

## Code Style Guide

### Naming Conventions

```csharp
// Classes: PascalCase
public class BitcoinWallet { }

// Methods: PascalCase
public void SendBitcoin() { }

// Properties: PascalCase
public string Address { get; set; }

// Private fields: camelCase
private string seedPhrase;

// Constants: PascalCase
private const int MaxRetries = 3;

// Parameters: camelCase
public void Method(string parameterName) { }
```

### Documentation

```csharp
/// <summary>
/// Brief description of what the method does
/// </summary>
/// <param name="address">Description of parameter</param>
/// <returns>Description of return value</returns>
public async Task<decimal> GetBalance(string address)
{
    // Implementation
}
```

### Error Handling

```csharp
try
{
    // Code that might throw
}
catch (SpecificException ex)
{
    Debug.LogError($"Specific error: {ex.Message}");
    throw; // Re-throw if needed
}
catch (Exception ex)
{
    Debug.LogError($"Unexpected error: {ex.Message}");
    throw;
}
```

## Testing Guidelines

### TestNet Testing

```csharp
// Always use TestNet for testing
var sdk = new BitcoinWalletSDK(NetworkType.TestNet);

// Get free TestNet coins from faucets
// Test all features thoroughly
// Verify transactions on TestNet explorer
```

### Unit Testing

```csharp
[Test]
public void TestAddressValidation()
{
    var sdk = new BitcoinWalletSDK();
    
    // Valid address
    Assert.IsTrue(sdk.ValidateAddress("bc1q..."));
    
    // Invalid address
    Assert.IsFalse(sdk.ValidateAddress("invalid"));
}
```

## Security Guidelines

- Never commit private keys
- Never commit seed phrases
- Test on TestNet only
- Encrypt sensitive data
- Follow security best practices
- Report vulnerabilities privately

## Commit Message Format

```
type(scope): subject

body

footer
```

### Types

- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation
- `style`: Code style (formatting)
- `refactor`: Code refactoring
- `test`: Adding tests
- `chore`: Maintenance

### Examples

```
feat(wallet): add multi-signature support

Implement BIP45 multi-signature wallet creation
and transaction signing.

Closes #123
```

```
fix(api): handle rate limit errors

Add exponential backoff for rate limit errors
from BlockCypher API.

Fixes #456
```

## Review Process

1. Automated checks run
2. Code review by maintainers
3. Testing on TestNet
4. Documentation review
5. Approval and merge

## Release Process

1. Update version number
2. Update CHANGELOG.md
3. Create release branch
4. Final testing
5. Create release tag
6. Publish release

## Community Guidelines

### Be Respectful

- Be kind and courteous
- Respect different opinions
- Provide constructive feedback
- Help others learn

### Be Professional

- Stay on topic
- Use clear language
- Provide examples
- Be patient

### Be Collaborative

- Share knowledge
- Ask questions
- Offer help
- Work together

## Getting Help

- Read documentation first
- Check existing issues
- Ask in discussions
- Be specific in questions
- Provide context

## Recognition

Contributors will be:
- Listed in CONTRIBUTORS.md
- Mentioned in release notes
- Credited in documentation

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

## Questions?

If you have questions about contributing:
- Open a discussion
- Ask in issues
- Contact maintainers

---

Thank you for contributing to Bitcoin Wallet SDK! 🚀
