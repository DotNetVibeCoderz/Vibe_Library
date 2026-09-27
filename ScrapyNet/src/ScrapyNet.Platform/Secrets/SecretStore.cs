using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScrapyNet.Platform;

public enum SecretKind
{
    UsernamePassword,
    ApiKey,
    BearerToken,
    Cookies,
    ProxyCredentials,
    Other,
}

/// <summary>A decrypted secret: a kind and named values (<c>username</c>/<c>password</c>, <c>value</c>, ...).</summary>
public sealed record Secret(string Name, SecretKind Kind, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>The single value, or the field named <c>value</c>.</summary>
    public string Value => Values.TryGetValue("value", out var v) ? v : Values.Values.First();
}

/// <summary>
/// Credentials for crawls — logins, API keys, bearer tokens, cookies, proxy passwords — encrypted at
/// rest with AES-256-GCM. The key comes from a master password (PBKDF2-SHA256, 210,000 iterations,
/// random salt) or is supplied directly. Settings reference secrets as <c>${secret:name}</c> or
/// <c>${secret:name:field}</c> and are resolved just before a job runs, so plaintext never lands in
/// spider definitions or job history.
/// </summary>
public sealed partial class SecretStore
{
    private sealed record Envelope(string Kind, string Nonce, string Ciphertext, string Tag);

    private sealed record FileModel(string Salt, Dictionary<string, Envelope> Secrets);

    private readonly byte[] _key;
    private readonly string? _path;
    private readonly byte[] _salt;
    private readonly Dictionary<string, Envelope> _secrets = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private SecretStore(byte[] key, byte[] salt, string? path)
    {
        _key = key;
        _salt = salt;
        _path = path;
    }

    /// <summary>Opens (or creates) a store protected by a master password.</summary>
    public static SecretStore Open(string masterPassword, string? path = null)
    {
        byte[] salt;
        FileModel? existing = null;
        if (path is not null && File.Exists(path))
        {
            existing = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path), ScrapyJson.Default)!;
            salt = Convert.FromBase64String(existing.Salt);
        }
        else
        {
            salt = RandomNumberGenerator.GetBytes(16);
        }
        var key = Rfc2898DeriveBytes.Pbkdf2(masterPassword, salt, 210_000, HashAlgorithmName.SHA256, 32);
        var store = new SecretStore(key, salt, path);
        if (existing is not null)
        {
            foreach (var (name, env) in existing.Secrets) store._secrets[name] = env;
            // Fail fast on a wrong password rather than on first use.
            if (store._secrets.Count > 0) store.Get(store._secrets.Keys.First());
        }
        return store;
    }

    /// <summary>An in-memory store with a random key (for tests and ephemeral runs).</summary>
    public static SecretStore CreateInMemory() => new(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(16), null);

    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_gate) return [.. _secrets.Keys.Order(StringComparer.Ordinal)];
        }
    }

    public void Set(string name, SecretKind kind, IReadOnlyDictionary<string, string> values)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(values, ScrapyJson.Default);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (var aes = new AesGcm(_key, tag.Length))
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(name));
        CryptographicOperations.ZeroMemory(plaintext);
        lock (_gate)
            _secrets[name] = new Envelope(kind.ToString(), Convert.ToBase64String(nonce), Convert.ToBase64String(ciphertext), Convert.ToBase64String(tag));
        Persist();
    }

    public void Set(string name, SecretKind kind, string value) => Set(name, kind, new Dictionary<string, string> { ["value"] = value });

    public void SetLogin(string name, string username, string password) =>
        Set(name, SecretKind.UsernamePassword, new Dictionary<string, string> { ["username"] = username, ["password"] = password });

    public Secret Get(string name)
    {
        Envelope env;
        lock (_gate)
        {
            if (!_secrets.TryGetValue(name, out env!)) throw new KeyNotFoundException($"Secret '{name}' not found.");
        }
        var ciphertext = Convert.FromBase64String(env.Ciphertext);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
            aes.Decrypt(Convert.FromBase64String(env.Nonce), ciphertext, Convert.FromBase64String(env.Tag), plaintext, Encoding.UTF8.GetBytes(name));
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new CryptographicException("Secret store: wrong master password or tampered data.");
        }
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext, ScrapyJson.Default)!;
        CryptographicOperations.ZeroMemory(plaintext);
        return new Secret(name, Enum.Parse<SecretKind>(env.Kind), values);
    }

    public bool Remove(string name)
    {
        bool removed;
        lock (_gate) removed = _secrets.Remove(name);
        Persist();
        return removed;
    }

    /// <summary>Replaces <c>${secret:name}</c> and <c>${secret:name:field}</c> in a string.</summary>
    public string ResolvePlaceholders(string text) =>
        Placeholder().Replace(text, m =>
        {
            var secret = Get(m.Groups["name"].Value);
            return m.Groups["field"].Success ? secret.Values[m.Groups["field"].Value] : secret.Value;
        });

    [GeneratedRegex(@"\$\{secret:(?<name>[A-Za-z0-9_.\-]+)(?::(?<field>[A-Za-z0-9_]+))?\}")]
    private static partial Regex Placeholder();

    private void Persist()
    {
        if (_path is null) return;
        FileModel model;
        lock (_gate) model = new FileModel(Convert.ToBase64String(_salt), new Dictionary<string, Envelope>(_secrets));
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(model, ScrapyJson.Indented));
    }
}
