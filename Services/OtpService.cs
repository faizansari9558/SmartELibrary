using Microsoft.EntityFrameworkCore;
using SmartELibrary.Data;
using SmartELibrary.Models;

namespace SmartELibrary.Services;

public interface IOtpService
{
    Task<string> GenerateOtpAsync(string phoneNumber);
    Task<bool> VerifyOtpAsync(string phoneNumber, string otpCode);
    Task SendOtpSmsPlaceholderAsync(string phoneNumber, string otpCode);
}

public class OtpService(ApplicationDbContext dbContext) : IOtpService
{
    public async Task<string> GenerateOtpAsync(string phoneNumber)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();

        var entity = new OtpVerification
        {
            PhoneNumber = phoneNumber,
            OtpCode = code,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };

        dbContext.OtpVerifications.Add(entity);
        await dbContext.SaveChangesAsync();

        return code;
    }

    public async Task<bool> VerifyOtpAsync(string phoneNumber, string otpCode)
    {
        var otp = await dbContext.OtpVerifications
            .Where(x => x.PhoneNumber == phoneNumber && !x.IsUsed && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (otp is null || otp.OtpCode != otpCode)
        {
            return false;
        }

        otp.IsUsed = true;
        await dbContext.SaveChangesAsync();
        return true;
    }

    public Task SendOtpSmsPlaceholderAsync(string phoneNumber, string otpCode)
    {
        return Task.CompletedTask;
    }
}
