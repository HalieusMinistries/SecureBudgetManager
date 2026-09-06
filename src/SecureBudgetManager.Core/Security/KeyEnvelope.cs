namespace SecureBudgetManager.Core.Security;

/// <summary>
/// An AES-GCM protected copy of the database key. Contains no plaintext secret,
/// so it is safe to store on disk next to the database.
/// </summary>
public sealed record KeyEnvelope(byte[] Salt, byte[] Nonce, byte[] Ciphertext)
{
    public void Validate()
    {
        if (Salt.Length < 16)
        {
            throw new InvalidOperationException("The key envelope salt is too short.");
        }

        if (Nonce.Length != 12)
        {
            throw new InvalidOperationException("AES-GCM requires a 12-byte nonce.");
        }

        // 32-byte key plus the 16-byte authentication tag.
        if (Ciphertext.Length != 48)
        {
            throw new InvalidOperationException("The wrapped key length is invalid.");
        }
    }
}
