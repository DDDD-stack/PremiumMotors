using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models.Dtos
{
    // ---------- Requests ----------

    public class RegisterRequest
    {
        [Required, StringLength(30, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Phone]
        public string Phone { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// The API had no terms checkbox at all, while both web forms refused to submit
        /// without one. An account is an account however it was created, so the same
        /// acceptance is required here.
        /// </summary>
        [MustBeTrue(ErrorMessage = "You must accept the Terms and Privacy Policy to create an account.")]
        public bool AcceptTerms { get; set; }

        /// <summary>Optional client label ("ios", "android", "web") shown in the session list.</summary>
        public string? Device { get; set; }
    }

    public class LoginRequest
    {
        /// <summary>Username or email — both are accepted.</summary>
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public string? Device { get; set; }
    }

    public class RefreshRequest
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;

        public string? Device { get; set; }
    }

    public class ChangePasswordRequest
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 6)]
        public string NewPassword { get; set; } = string.Empty;

        /// <summary>When true (default) every other session is signed out.</summary>
        public bool RevokeOtherSessions { get; set; } = true;
    }

    public class ForgotPasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 6)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class VerifyEmailRequest
    {
        [Required]
        public string Token { get; set; } = string.Empty;
    }

    public class DeleteAccountRequest
    {
        /// <summary>Current password, required so a stolen access token cannot erase an account.</summary>
        [Required]
        public string Password { get; set; } = string.Empty;

        /// <summary>Must be the literal string "DELETE", as a deliberate second confirmation.</summary>
        [Required]
        public string Confirm { get; set; } = string.Empty;
    }

    public class UpdateProfileRequest
    {
        [EmailAddress]
        public string? Email { get; set; }

        [Phone]
        public string? Phone { get; set; }
    }

    // ---------- Responses ----------

    public record UserDto(
        int Id,
        string Username,
        string Email,
        string Phone,
        string Role,
        bool EmailVerified,
        DateTime RegisteredUtc)
    {
        public static UserDto From(User u) =>
            new(u.Id, u.Username, u.Email, u.Phone, u.Role, u.IsEmailVerified, u.RegisteredUtc);
    }

    /// <summary>
    /// What every client (web, iOS, Android) receives on register / login / refresh.
    /// Clients store the refresh token in secure storage (Keychain / EncryptedSharedPreferences)
    /// and keep the short-lived access token in memory only.
    /// </summary>
    public record AuthResponse(
        string AccessToken,
        DateTime AccessTokenExpiresUtc,
        string RefreshToken,
        DateTime RefreshTokenExpiresUtc,
        UserDto User)
    {
        public string TokenType => "Bearer";
    }

    public record SessionDto(
        int Id,
        string? Device,
        DateTime CreatedUtc,
        DateTime ExpiresUtc,
        bool IsCurrent);

    /// <summary>Uniform error body so mobile clients can parse failures consistently.</summary>
    public record ApiError(string Error, string? Code = null);
}
