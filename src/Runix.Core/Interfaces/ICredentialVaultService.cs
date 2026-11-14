namespace Runix.Core.Interfaces;

public interface ICredentialVaultService
{
    string Protect(string? plainText);
    string Unprotect(string? storedText);
    bool IsEncrypted(string? text);
}
