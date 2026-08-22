using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Ampersand.Mozart.WEB.Models;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Reads Web Deploy credentials: Mozart store first, then VS EncryptedPassword in *.pubxml.user (DPAPI).
/// </summary>
public sealed class PublishCredentialService(ILogger<PublishCredentialService> logger)
{
    public string? ResolvePassword(CicdProject project, PublishProfileInfo profile)
    {
        if (project.PublishSecrets.TryGetValue(profile.Name, out var stored) && !string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        var userFile = profile.FilePath + ".user";
        if (!File.Exists(userFile))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(userFile);
            var encrypted = document.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "EncryptedPassword")
                ?.Value;
            if (string.IsNullOrWhiteSpace(encrypted))
            {
                return null;
            }

            return Unprotect(encrypted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read EncryptedPassword from {File}", userFile);
            return null;
        }
    }

    private string? Unprotect(string base64)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        byte[][] entropies =
        [
            [],
            Encoding.Unicode.GetBytes("Publish"),
            Encoding.UTF8.GetBytes("Publish"),
        ];

        foreach (var entropy in entropies)
        {
            try
            {
                var bytes = ProtectedData.Unprotect(
                    blob,
                    entropy.Length == 0 ? null : entropy,
                    DataProtectionScope.CurrentUser);
                var text = Encoding.Unicode.GetString(bytes);
                if (string.IsNullOrEmpty(text))
                {
                    text = Encoding.UTF8.GetString(bytes);
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.TrimEnd('\0');
                }
            }
            catch (CryptographicException)
            {
                // Try the next entropy.
            }
        }

        logger.LogWarning("EncryptedPassword is present but DPAPI could not decrypt it for this Windows user.");
        return null;
    }
}
