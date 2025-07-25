﻿using AuthAPI.Data;
using AuthAPI.Dtos;
using AuthAPI.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AuthAPI.Services
{
    public class AuthService : IAuthService
    {
        private readonly DataContext _context;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly ILogger<AuthService> _logger;

        public AuthService(DataContext context, IConfiguration configuration, IEmailService emailService, ILogger<AuthService> logger)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<string?> Login(UserForLoginDto userForLoginDto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userForLoginDto.Email);
            if (user == null)
                return null;

            if (!user.IsEmailVerified)
            {
                _logger.LogWarning("Login attempt with unverified email: {Email}", userForLoginDto.Email);
                throw new InvalidOperationException("Please verify your email address before logging in.");
            }

            if (!VerifyPasswordHash(userForLoginDto.Password, user.PasswordHash, user.PasswordSalt))
                return null;

            string token = CreateToken(user);
            return token;
        }

        public async Task<User?> Register(UserForRegisterDto userForRegisterDto)
        {
            if (await _context.Users.AnyAsync(u => u.Email == userForRegisterDto.Email))
                return null;

            CreatePasswordHash(userForRegisterDto.Password, out byte[] passwordHash, out byte[] passwordSalt);

            // Generate email verification token
            var verificationToken = GenerateRandomToken();

            var user = new User
            {
                Email = userForRegisterDto.Email,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                EmailVerificationToken = verificationToken,
                EmailVerificationTokenExpires = DateTime.UtcNow.AddHours(72), // 72 saat (3 gün)
                CreatedAt = DateTime.UtcNow
            };

            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();

            // Send verification email
            try
            {
                await _emailService.SendEmailVerificationAsync(user.Email, verificationToken);
                _logger.LogInformation("Verification email sent to {Email}", user.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send verification email to {Email}", user.Email);
                // Don't fail registration if email sending fails
            }

            return user;
        }

        public async Task<bool> VerifyEmail(string token)
        {
            _logger.LogInformation("VerifyEmail called with token: {Token}", token);
            
            // Token null veya boş kontrolü
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("VerifyEmail called with null or empty token");
                return false;
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => 
                u.EmailVerificationToken == token && 
                u.EmailVerificationTokenExpires > DateTime.UtcNow);

            if (user == null)
            {
                // Hangi durumda token bulunamadığını kontrol edelim
                var userWithToken = await _context.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == token);
                
                if (userWithToken == null)
                {
                    _logger.LogWarning("No user found with verification token: {Token}", token);
                }
                else
                {
                    _logger.LogWarning("User found with token but token expired. User: {Email}, Token expires: {Expires}, Current time: {Now}", 
                        userWithToken.Email, userWithToken.EmailVerificationTokenExpires, DateTime.UtcNow);
                }
                
                return false;
            }

            // Token zaten kullanılmış mı kontrol et
            if (user.IsEmailVerified)
            {
                _logger.LogWarning("User email already verified: {Email}", user.Email);
                return false;
            }

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpires = null;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Email verified successfully for user: {Email}", user.Email);
            return true;
        }

        public async Task<bool> ResendVerificationEmail(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            
            if (user == null || user.IsEmailVerified)
                return false;

            // Generate new verification token
            var verificationToken = GenerateRandomToken();
            user.EmailVerificationToken = verificationToken;
            user.EmailVerificationTokenExpires = DateTime.UtcNow.AddHours(72); // 72 saat (3 gün)

            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendEmailVerificationAsync(user.Email, verificationToken);
                _logger.LogInformation("Verification email resent to {Email}", user.Email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resend verification email to {Email}", user.Email);
                return false;
            }
        }

        public async Task<bool> ForgotPassword(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            
            if (user == null || !user.IsEmailVerified)
                return false;

            // Generate password reset token
            var resetToken = GenerateRandomToken();
            user.PasswordResetToken = resetToken;
            user.PasswordResetTokenExpires = DateTime.UtcNow.AddHours(1);

            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendPasswordResetAsync(user.Email, resetToken);
                _logger.LogInformation("Password reset email sent to {Email}", user.Email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", user.Email);
                return false;
            }
        }

        public async Task<bool> ResetPassword(string token, string newPassword)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => 
                u.PasswordResetToken == token && 
                u.PasswordResetTokenExpires > DateTime.UtcNow);

            if (user == null)
                return false;

            CreatePasswordHash(newPassword, out byte[] passwordHash, out byte[] passwordSalt);
            
            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpires = null;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Password reset successfully for user: {Email}", user.Email);
            return true;
        }

        public async Task<object> GetUserDebugInfo(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            
            if (user == null)
            {
                return new { message = "User not found", email = email };
            }

            return new
            {
                userId = user.Id,
                email = user.Email,
                isEmailVerified = user.IsEmailVerified,
                hasVerificationToken = !string.IsNullOrEmpty(user.EmailVerificationToken),
                verificationTokenExpires = user.EmailVerificationTokenExpires,
                tokenExpired = user.EmailVerificationTokenExpires < DateTime.UtcNow,
                currentUtcTime = DateTime.UtcNow,
                createdAt = user.CreatedAt,
                // Güvenlik için token'ın sadece ilk ve son 4 karakterini göster
                tokenPreview = user.EmailVerificationToken?.Length > 8 
                    ? user.EmailVerificationToken.Substring(0, 4) + "..." + user.EmailVerificationToken.Substring(user.EmailVerificationToken.Length - 4)
                    : "No token",
                fullTokenLength = user.EmailVerificationToken?.Length ?? 0
            };
        }

        public async Task<object> GetAllUsersDebugInfo()
        {
            var users = await _context.Users
                .Select(u => new
                {
                    userId = u.Id,
                    email = u.Email,
                    isEmailVerified = u.IsEmailVerified,
                    hasVerificationToken = !string.IsNullOrEmpty(u.EmailVerificationToken),
                    verificationTokenExpires = u.EmailVerificationTokenExpires,
                    tokenExpired = u.EmailVerificationTokenExpires < DateTime.UtcNow,
                    createdAt = u.CreatedAt,
                    tokenPreview = u.EmailVerificationToken != null && u.EmailVerificationToken.Length > 8 
                        ? u.EmailVerificationToken.Substring(0, 4) + "..." + u.EmailVerificationToken.Substring(u.EmailVerificationToken.Length - 4)
                        : "No token",
                    fullTokenLength = u.EmailVerificationToken != null ? u.EmailVerificationToken.Length : 0
                })
                .ToListAsync();

            return new
            {
                totalUsers = users.Count,
                verifiedUsers = users.Count(u => u.isEmailVerified),
                unverifiedUsers = users.Count(u => !u.isEmailVerified),
                usersWithTokens = users.Count(u => u.hasVerificationToken),
                expiredTokens = users.Count(u => u.hasVerificationToken && u.tokenExpired),
                users = users
            };
        }

        public async Task<EmailStatusResponseDto> CheckEmailStatus(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            
            if (user == null)
            {
                return new EmailStatusResponseDto
                {
                    Email = email,
                    IsRegistered = false,
                    IsEmailVerified = false,
                    Status = "NOT_REGISTERED",
                    Message = "Bu email adresi ile kayıtlı kullanıcı bulunamadı."
                };
            }

            string status;
            string message;

            if (user.IsEmailVerified)
            {
                status = "VERIFIED";
                message = "Email adresi doğrulanmış ve kullanıma hazır.";
            }
            else
            {
                // Token süresi kontrol et
                bool tokenExpired = user.EmailVerificationTokenExpires < DateTime.UtcNow;
                
                if (tokenExpired)
                {
                    status = "PENDING_EXPIRED";
                    message = "Email doğrulama token'ı süresi dolmuş. Yeni doğrulama email'i talep edin.";
                }
                else
                {
                    status = "PENDING_VERIFICATION";
                    message = "Email doğrulama bekleniyor. Email'inizi kontrol edin.";
                }
            }

            return new EmailStatusResponseDto
            {
                Email = email,
                IsRegistered = true,
                IsEmailVerified = user.IsEmailVerified,
                Status = status,
                Message = message,
                RegistrationDate = user.CreatedAt
            };
        }

        public async Task<bool> DeleteUserAccount(string email, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            
            if (user == null)
                return false;

            // Verify password before allowing deletion
            if (!VerifyPasswordHash(password, user.PasswordHash, user.PasswordSalt))
                return false;

            // Generate deletion confirmation token
            var deletionToken = GenerateRandomToken();
            user.AccountDeletionToken = deletionToken;
            user.AccountDeletionTokenExpires = DateTime.UtcNow.AddHours(24); // 24 saat
            user.IsMarkedForDeletion = true;
            user.DeletionScheduledAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendAccountDeletionConfirmationAsync(user.Email, deletionToken);
                _logger.LogInformation("Account deletion confirmation email sent to {Email}", user.Email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send account deletion confirmation email to {Email}", user.Email);
                return false;
            }
        }

        public async Task<bool> DeleteUserAccountByAdmin(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            
            if (user == null)
                return false;

            try
            {
                // Send notification email before deletion
                await _emailService.SendAccountDeletedNotificationAsync(user.Email);
                _logger.LogInformation("Account deletion notification sent to {Email}", user.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send account deletion notification to {Email}", user.Email);
                // Continue with deletion even if email fails
            }

            // Delete the user
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("User account deleted by admin: {Email}", user.Email);
            return true;
        }

        public async Task<string> GenerateAccountDeletionToken(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            
            if (user == null)
                throw new InvalidOperationException("User not found");

            var deletionToken = GenerateRandomToken();
            user.AccountDeletionToken = deletionToken;
            user.AccountDeletionTokenExpires = DateTime.UtcNow.AddHours(24);
            user.IsMarkedForDeletion = true;
            user.DeletionScheduledAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return deletionToken;
        }

        public async Task<bool> ConfirmAccountDeletion(string token)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => 
                u.AccountDeletionToken == token && 
                u.AccountDeletionTokenExpires > DateTime.UtcNow &&
                u.IsMarkedForDeletion);

            if (user == null)
            {
                _logger.LogWarning("Invalid or expired account deletion token: {Token}", token);
                return false;
            }

            var userEmail = user.Email; // Store email for logging

            try
            {
                // Send final notification email
                await _emailService.SendAccountDeletedNotificationAsync(userEmail);
                _logger.LogInformation("Final account deletion notification sent to {Email}", userEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send final account deletion notification to {Email}", userEmail);
                // Continue with deletion even if email fails
            }

            // Delete the user permanently
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("User account permanently deleted: {Email}", userEmail);
            return true;
        }

        private bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt)
        {
            using (var hmac = new HMACSHA512(passwordSalt))
            {
                var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
                return computedHash.SequenceEqual(passwordHash);
            }
        }

        private void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
        {
            using (var hmac = new HMACSHA512())
            {
                passwordSalt = hmac.Key;
                passwordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            }
        }

        private string CreateToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email)
            };

            // Get JWT secret from environment variable or configuration
            var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") 
                ?? _configuration.GetSection("AppSettings:Token").Value;

            if (string.IsNullOrEmpty(jwtSecret))
            {
                throw new InvalidOperationException("JWT secret key is not configured.");
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.Now.AddDays(1),
                SigningCredentials = creds
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }

        private string GenerateRandomToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        }
    }
}