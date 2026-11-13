using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace SourcingTenderManagement.Services.FaceEnrollment;

public sealed class FaceTemplateProtector(IDataProtectionProvider provider)
{
    public byte[] Protect(string userId, float[][] descriptors) =>
        Protector(userId).Protect(JsonSerializer.SerializeToUtf8Bytes(descriptors));

    public float[][] Unprotect(string userId, byte[] payload) =>
        JsonSerializer.Deserialize<float[][]>(Protector(userId).Unprotect(payload))
        ?? throw new CryptographicException("Invalid face template payload.");

    private IDataProtector Protector(string userId) =>
        provider.CreateProtector("SourcingTender.FaceEnrollment", "v1", userId);
}
