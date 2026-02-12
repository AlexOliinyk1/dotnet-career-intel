using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CareerIntel.Web.Services;

/// <summary>
/// Encrypts and decrypts credential values in notification config using
/// AES-256 encryption with a securely stored random key.
/// The key is generated once and persisted to a key file in the data directory.
/// </summary>
public sealed class CredentialProtector
{
    private const string EncryptedPrefix = "ENC:";
    private const string KeyFileName = ".credential-key";
    private readonly byte[] _key;

    public CredentialProtector(string dataDir)
    {
        _key = LoadOrCreateKey(dataDir);
    }

    /// <summary>
    /// Loads the encryption key from the key file, or generates a new cryptographically
    /// random 256-bit key and stores it. This replaces the previous deterministic
    /// key derivation (MachineName + dataDir) which was predictable.
    /// </summary>
    private static byte[] LoadOrCreateKey(string dataDir)
    {
        var keyPath = Path.Combine(dataDir, KeyFileName);

        if (File.Exists(keyPath))
        {
            try
            {
                var keyBase64 = File.ReadAllText(keyPath).Trim();
                var key = Convert.FromBase64String(keyBase64);
                if (key.Length == 32) // 256 bits
                    return key;
            }
            catch
            {
                // Corrupted key file — regenerate
            }
        }

        // Generate a cryptographically random 256-bit key
        var newKey = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyPath, Convert.ToBase64String(newKey));

        // Restrict file permissions where possible (best-effort on Windows)
        try
        {
            File.SetAttributes(keyPath, FileAttributes.Hidden);
        }
        catch
        {
            // Non-critical — file visibility is cosmetic
        }

        return newKey;
    }

    /// <summary>
    /// Returns true if the value is already encrypted.
    /// </summary>
    public static bool IsEncrypted(string? value) =>
        value?.StartsWith(EncryptedPrefix) == true;

    /// <summary>
    /// Encrypts a plaintext value. Returns the encrypted value with the ENC: prefix.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        if (IsEncrypted(plaintext))
            return plaintext; // Already encrypted

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Prepend IV to ciphertext
        var result = new byte[aes.IV.Length + ciphertext.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(ciphertext, 0, result, aes.IV.Length, ciphertext.Length);

        return EncryptedPrefix + Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts an encrypted value. Returns the plaintext.
    /// If the value is not encrypted (no ENC: prefix), returns it as-is.
    /// </summary>
    public string Decrypt(string encryptedValue)
    {
        if (string.IsNullOrEmpty(encryptedValue))
            return encryptedValue;

        if (!IsEncrypted(encryptedValue))
            return encryptedValue; // Not encrypted, return as-is

        var base64 = encryptedValue[EncryptedPrefix.Length..];
        var combined = Convert.FromBase64String(base64);

        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[aes.BlockSize / 8];
        var ciphertext = new byte[combined.Length - iv.Length];
        Buffer.BlockCopy(combined, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(combined, iv.Length, ciphertext, 0, ciphertext.Length);

        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// Encrypts sensitive fields in a notification config JSON and writes it back.
    /// </summary>
    public async Task EncryptConfigFileAsync(string configPath)
    {
        if (!File.Exists(configPath))
            return;

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        var modified = false;

        // Encrypt telegram bot token
        if (root.TryGetProperty("telegram", out var telegram))
        {
            if (telegram.TryGetProperty("botToken", out var botToken))
            {
                var tokenValue = botToken.GetString() ?? "";
                if (!string.IsNullOrEmpty(tokenValue) && !IsEncrypted(tokenValue))
                {
                    // We need to rewrite with encrypted values
                    modified = true;
                }
            }
        }

        // Encrypt email password
        if (root.TryGetProperty("email", out var email))
        {
            if (email.TryGetProperty("password", out var password))
            {
                var passValue = password.GetString() ?? "";
                if (!string.IsNullOrEmpty(passValue) && !IsEncrypted(passValue))
                {
                    modified = true;
                }
            }
        }

        if (modified)
        {
            // Read and rewrite the config with encrypted values
            var configObj = JsonSerializer.Deserialize<NotificationConfigRaw>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (configObj != null)
            {
                if (configObj.Telegram != null && !string.IsNullOrEmpty(configObj.Telegram.BotToken))
                    configObj.Telegram.BotToken = Encrypt(configObj.Telegram.BotToken);

                if (configObj.Email != null && !string.IsNullOrEmpty(configObj.Email.Password))
                    configObj.Email.Password = Encrypt(configObj.Email.Password);

                var encrypted = JsonSerializer.Serialize(configObj, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await File.WriteAllTextAsync(configPath, encrypted);
            }
        }
    }

    // Minimal models for config rewriting
    private sealed class NotificationConfigRaw
    {
        public TelegramConfigRaw? Telegram { get; set; }
        public EmailConfigRaw? Email { get; set; }
        public int MinScoreToNotify { get; set; }
        public bool NotifyOnNewHighPayingRoles { get; set; }
    }

    private sealed class TelegramConfigRaw
    {
        public string BotToken { get; set; } = "";
        public string ChatId { get; set; } = "";
        public bool Enabled { get; set; }
    }

    private sealed class EmailConfigRaw
    {
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; }
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromAddress { get; set; } = "";
        public string ToAddress { get; set; } = "";
        public bool Enabled { get; set; }
    }
}
