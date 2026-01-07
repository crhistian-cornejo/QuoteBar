using System.Runtime.InteropServices;
using System.Text;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Secure credential storage using Windows Credential Manager
/// Similar to macOS Keychain used by CodexBar
/// </summary>
public static class SecureCredentialStore
{
    private const string TargetPrefix = "QuoteBar:";

    #region Windows Credential Manager P/Invoke

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);

    private const uint CRED_TYPE_GENERIC = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    #endregion

    /// <summary>
    /// Store a credential securely in Windows Credential Manager
    /// </summary>
    public static bool StoreCredential(string key, string? secret)
    {
        var target = $"{TargetPrefix}{key}";

        try
        {
            // If secret is null or empty, delete the credential
            if (string.IsNullOrWhiteSpace(secret))
            {
                return DeleteCredential(key);
            }

            var secretBytes = Encoding.Unicode.GetBytes(secret);
            var secretPtr = Marshal.AllocHGlobal(secretBytes.Length);

            try
            {
                Marshal.Copy(secretBytes, 0, secretPtr, secretBytes.Length);

                var credential = new CREDENTIAL
                {
                    Flags = 0,
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    Comment = "QuoteBar API Token",
                    CredentialBlobSize = (uint)secretBytes.Length,
                    CredentialBlob = secretPtr,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = key
                };

                var result = CredWriteW(ref credential, 0);

                if (!result)
                {
                    var error = Marshal.GetLastWin32Error();
                    Log($"CredWriteW failed for {key}: error {error}");
                }

                return result;
            }
            finally
            {
                // Clear and free sensitive data
                for (int i = 0; i < secretBytes.Length; i++)
                    secretBytes[i] = 0;
                Marshal.FreeHGlobal(secretPtr);
            }
        }
        catch (Exception ex)
        {
            Log($"StoreCredential error for {key}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Retrieve a credential from Windows Credential Manager
    /// </summary>
    public static string? GetCredential(string key)
    {
        var target = $"{TargetPrefix}{key}";

        try
        {
            if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out var credentialPtr))
            {
                // Credential not found is not an error
                return null;
            }

            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);

                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                {
                    return null;
                }

                var secretBytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, secretBytes, 0, (int)credential.CredentialBlobSize);

                var secret = Encoding.Unicode.GetString(secretBytes);

                // Clear sensitive data from memory
                for (int i = 0; i < secretBytes.Length; i++)
                    secretBytes[i] = 0;

                return string.IsNullOrWhiteSpace(secret) ? null : secret.Trim();
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }
        catch (Exception ex)
        {
            Log($"GetCredential error for {key}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Delete a credential from Windows Credential Manager
    /// </summary>
    public static bool DeleteCredential(string key)
    {
        var target = $"{TargetPrefix}{key}";

        try
        {
            var result = CredDeleteW(target, CRED_TYPE_GENERIC, 0);

            if (!result)
            {
                var error = Marshal.GetLastWin32Error();
                // Error 1168 = Element not found, which is OK
                if (error != 1168)
                {
                    Log($"CredDeleteW failed for {key}: error {error}");
                }
            }

            return true; // Return true even if not found
        }
        catch (Exception ex)
        {
            Log($"DeleteCredential error for {key}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a credential exists
    /// </summary>
    public static bool HasCredential(string key)
    {
        return GetCredential(key) != null;
    }

    private static void Log(string message)
    {
        DebugLogger.Log("SecureCredentialStore", message);
    }
}

/// <summary>
/// Credential keys used by NativeBar
/// </summary>
public static class CredentialKeys
{
    public const string ZaiApiToken = "zai-api-token";
    public const string ClaudeApiToken = "claude-api-token";
    public const string CopilotToken = "copilot-token";
    public const string MinimaxApiKey = "minimax-api-key";
    public const string MinimaxGroupId = "minimax-group-id";
    public const string AugmentCookie = "augment-cookie";
}
