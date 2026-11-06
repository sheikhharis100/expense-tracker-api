using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Api.DTOs;

// Sign-up input is checked in AuthController (AccountRules, PasswordPolicy) so every problem comes back as a
// plain { error } sentence.
public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
}

public class AuthOptionsResponse
{
    // False when the server can't send email: the app then hides sign-in links and explains password resets.
    public bool EmailEnabled { get; set; }
}

public class EmailRequest
{
    public string Email { get; set; } = string.Empty;
}

// The code from an emailed link.
public class TokenRequest
{
    public string Token { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangeEmailRequest
{
    public string NewEmail { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;
}

public class CurrentPasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
}

public class ProfileUpdateRequest
{
    public string DisplayName { get; set; } = string.Empty;
}
