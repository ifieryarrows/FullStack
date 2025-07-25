using AuthAPI.Dtos;
using AuthAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace AuthAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(UserForRegisterDto userForRegisterDto)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value.Errors.Count > 0)
                    .Select(x => new { 
                        Field = x.Key, 
                        Errors = x.Value.Errors.Select(e => e.ErrorMessage) 
                    });
                
                return BadRequest(new { 
                    message = "Validation failed.", 
                    errors = errors 
                });
            }

            try
            {
                var result = await _authService.Register(userForRegisterDto);

                if (result == null)
                {
                    return BadRequest(new { message = "Email already exists." });
                }

                return StatusCode(201, new { 
                    message = "User registered successfully. Please check your email to verify your account." 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for email: {Email}", userForRegisterDto.Email);
                return StatusCode(500, new { message = "An error occurred during registration." });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(UserForLoginDto userForLoginDto)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value.Errors.Count > 0)
                    .Select(x => new { 
                        Field = x.Key, 
                        Errors = x.Value.Errors.Select(e => e.ErrorMessage) 
                    });
                
                return BadRequest(new { 
                    message = "Validation failed.", 
                    errors = errors 
                });
            }

            try
            {
                var token = await _authService.Login(userForLoginDto);

                if (token == null)
                {
                    return Unauthorized(new { message = "Invalid email or password." });
                }

                return Ok(new { token });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for email: {Email}", userForLoginDto.Email);
                return StatusCode(500, new { message = "An error occurred during login." });
            }
        }

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Content(GetErrorVerificationPage("Invalid token", "", ""), "text/html");
            }

            try
            {
                // URL decode and fix + character issue
                var decodedToken = Uri.UnescapeDataString(token);
                
                // Fix + character that gets converted to space from URL
                var fixedToken = decodedToken.Replace(" ", "+");
                
                _logger.LogInformation("Original token: {OriginalToken}", token);
                _logger.LogInformation("Decoded token: {DecodedToken}", decodedToken);
                _logger.LogInformation("Fixed token: {FixedToken}", fixedToken);
                
                var result = await _authService.VerifyEmail(fixedToken);

                // Return modern HTML page
                var htmlPage = result ? GetSuccessVerificationPage() : GetErrorVerificationPage(token, decodedToken, fixedToken);
                return Content(htmlPage, "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during email verification with token: {Token}", token);
                var errorPage = GetErrorVerificationPage(token, "Error", "System error occurred");
                return Content(errorPage, "text/html");
            }
        }

        [HttpPost("resend-verification")]
        public async Task<IActionResult> ResendVerification(ResendVerificationDto resendVerificationDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _authService.ResendVerificationEmail(resendVerificationDto.Email);

                if (!result)
                {
                    return BadRequest(new { message = "Email not found or already verified." });
                }

                return Ok(new { message = "Verification email sent successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resending verification email to: {Email}", resendVerificationDto.Email);
                return StatusCode(500, new { message = "An error occurred while sending verification email." });
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordDto forgotPasswordDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _authService.ForgotPassword(forgotPasswordDto.Email);

                // Always return success to prevent email enumeration attacks
                return Ok(new { message = "If the email exists, a password reset link has been sent." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending password reset email");
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordDto resetPasswordDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _authService.ResetPassword(resetPasswordDto.Token, resetPasswordDto.NewPassword);

                if (!result)
                {
                    return BadRequest(new { message = "Invalid or expired reset token." });
                }

                return Ok(new { message = "Password reset successfully. You can now log in with your new password." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during password reset");
                return StatusCode(500, new { message = "An error occurred during password reset." });
            }
        }

        [HttpGet("protected")]
        [Authorize]
        public IActionResult GetProtectedData()
        {
            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            return Ok(new { 
                message = $"Welcome {userEmail}! This is protected data and you are authorized to view it.",
                timestamp = DateTime.UtcNow
            });
        }

        [HttpGet("debug-user")]
        public async Task<IActionResult> DebugUser([FromQuery] string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest(new { message = "Email parameter is required." });
            }

            try
            {
                // This endpoint is for debug purposes only - remove in production
                var user = await _authService.GetUserDebugInfo(email);
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in debug endpoint");
                return StatusCode(500, new { message = "Debug error" });
            }
        }

        [HttpPost("check-email-status")]
        public async Task<IActionResult> CheckEmailStatus(EmailStatusDto emailStatusDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _authService.CheckEmailStatus(emailStatusDto.Email);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email status for: {Email}", emailStatusDto.Email);
                return StatusCode(500, new { message = "An error occurred while checking email status." });
            }
        }

        [HttpGet("reset-password")]
        public IActionResult ResetPasswordPage([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return BadRequest(new { message = "Token is required." });
            }

            try
            {
                // URL decode and fix + character issue
                var decodedToken = Uri.UnescapeDataString(token);
                var fixedToken = decodedToken.Replace(" ", "+");
                
                _logger.LogInformation("Password reset requested with token: {Token}", fixedToken);
                
                var htmlForm = GetPasswordResetPage(fixedToken);
                return Content(htmlForm, "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during password reset page generation with token: {Token}", token);
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        [HttpPost("delete-account")]
        [Authorize]
        public async Task<IActionResult> DeleteAccount(DeleteAccountDto deleteAccountDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _authService.DeleteUserAccount(deleteAccountDto.Email, deleteAccountDto.Password);

                if (!result)
                {
                    return BadRequest(new { message = "Invalid email or password." });
                }

                return Ok(new { 
                    message = "Account deletion confirmation email sent. Please check your email to confirm the deletion.",
                    warning = "This action is irreversible. Please confirm by clicking the link in the email."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during account deletion request for email: {Email}", deleteAccountDto.Email);
                return StatusCode(500, new { message = "An error occurred during account deletion request." });
            }
        }

        [HttpDelete("admin/delete-account/{email}")]
        [Authorize] // In production, add admin role check
        public async Task<IActionResult> AdminDeleteAccount(string email)
        {
            try
            {
                var result = await _authService.DeleteUserAccountByAdmin(email);

                if (!result)
                {
                    return BadRequest(new { message = "User not found." });
                }

                return Ok(new { 
                    message = $"User account {email} has been permanently deleted by admin.",
                    warning = "This action is irreversible and the user has been notified."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during admin account deletion for email: {Email}", email);
                return StatusCode(500, new { message = "An error occurred during account deletion." });
            }
        }

        [HttpGet("confirm-account-deletion")]
        public async Task<IActionResult> ConfirmAccountDeletion([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Content(GetAccountDeletionErrorPage("Invalid token"), "text/html");
            }

            try
            {
                // URL decode and fix + character issue
                var decodedToken = Uri.UnescapeDataString(token);
                var fixedToken = decodedToken.Replace(" ", "+");
                
                _logger.LogInformation("Account deletion confirmation requested with token: {Token}", fixedToken);
                
                var result = await _authService.ConfirmAccountDeletion(fixedToken);

                var htmlPage = result ? GetAccountDeletionSuccessPage() : GetAccountDeletionErrorPage("Invalid or expired deletion token");
                return Content(htmlPage, "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during account deletion confirmation with token: {Token}", token);
                var errorPage = GetAccountDeletionErrorPage("System error occurred");
                return Content(errorPage, "text/html");
            }
        }

        private string GetSuccessVerificationPage()
        {
            return @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Email Verified Successfully - AuthAPI</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 20px;
        }
        .container { 
            max-width: 500px; width: 100%; background: #ffffff; border-radius: 20px;
            box-shadow: 0 20px 60px rgba(0, 0, 0, 0.1); overflow: hidden; animation: slideUp 0.8s ease-out;
        }
        @keyframes slideUp { from { opacity: 0; transform: translateY(50px); } to { opacity: 1; transform: translateY(0); } }
        .header { 
            background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%);
            padding: 50px 30px; text-align: center; color: white;
        }
        .success-icon { font-size: 80px; margin-bottom: 20px; }
        .header h1 { font-size: 32px; font-weight: 700; margin-bottom: 10px; }
        .header p { font-size: 18px; opacity: 0.9; }
        .content { padding: 50px 40px; text-align: center; }
        .success-message { font-size: 24px; color: #2c3e50; margin-bottom: 20px; font-weight: 600; }
        .description { font-size: 16px; color: #5a6c7d; margin-bottom: 35px; line-height: 1.6; }
        .btn { 
            padding: 15px 30px; border: none; border-radius: 12px; font-size: 16px; font-weight: 600;
            cursor: pointer; text-decoration: none; display: inline-block; transition: all 0.3s ease;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; margin: 5px;
        }
        .btn:hover { transform: translateY(-2px); box-shadow: 0 10px 25px rgba(0,0,0,0.1); }
        .footer { background: #f8f9fa; padding: 25px; text-align: center; color: #6c757d; font-size: 14px; }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <div class=""success-icon"">??</div>
            <h1>Email Verified!</h1>
            <p>Welcome to AuthAPI Platform</p>
        </div>
        <div class=""content"">
            <div class=""success-message"">Verification Successful!</div>
            <div class=""description"">
                Your email address has been successfully verified. You can now access all features of your account.
            </div>
            <button class=""btn"" onclick=""window.close()"">Close Window</button>
            <a href=""/"" class=""btn"">Go to Homepage</a>
        </div>
        <div class=""footer"">� 2024 AuthAPI - Secure Authentication Platform</div>
    </div>
    <script>
        setTimeout(() => {
            if (confirm('This window will close automatically. Click OK to close now.')) {
                window.close();
            }
        }, 5000);
    </script>
</body>
</html>";
        }

        private string GetErrorVerificationPage(string originalToken, string decodedToken, string fixedToken)
        {
            var debugToken1 = originalToken?.Substring(0, Math.Min(15, originalToken?.Length ?? 0)) + "...";
            var debugToken2 = decodedToken?.Substring(0, Math.Min(15, decodedToken?.Length ?? 0)) + "...";
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            
            return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Email Verification Failed - AuthAPI</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #ff9a9e 0%, #fecfef 100%);
            min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 20px;
        }}
        .container {{ 
            max-width: 500px; width: 100%; background: #ffffff; border-radius: 20px;
            box-shadow: 0 20px 60px rgba(0, 0, 0, 0.1); overflow: hidden;
        }}
        .header {{ 
            background: linear-gradient(135deg, #ff7675 0%, #fd79a8 100%);
            padding: 50px 30px; text-align: center; color: white;
        }}
        .error-icon {{ font-size: 80px; margin-bottom: 20px; }}
        .header h1 {{ font-size: 32px; font-weight: 700; margin-bottom: 10px; }}
        .content {{ padding: 50px 40px; text-align: center; }}
        .error-message {{ font-size: 24px; color: #e74c3c; margin-bottom: 20px; font-weight: 600; }}
        .description {{ font-size: 16px; color: #5a6c7d; margin-bottom: 35px; line-height: 1.6; }}
        .reasons {{ background: #fff3cd; padding: 20px; border-radius: 15px; margin: 25px 0; text-align: left; }}
        .reasons h4 {{ color: #856404; margin-bottom: 15px; }}
        .reasons ul {{ color: #856404; padding-left: 20px; }}
        .reasons li {{ margin-bottom: 8px; font-size: 14px; }}
        .btn {{ 
            padding: 15px 30px; border: none; border-radius: 12px; font-size: 16px; font-weight: 600;
            cursor: pointer; text-decoration: none; display: inline-block; transition: all 0.3s ease;
            background: linear-gradient(135deg, #ff7675 0%, #fd79a8 100%); color: white; margin: 5px;
        }}
        .btn:hover {{ transform: translateY(-2px); box-shadow: 0 10px 25px rgba(0,0,0,0.1); }}
        .debug-info {{ background: #f8f9fa; padding: 20px; border-radius: 10px; margin-top: 30px; text-align: left; }}
        .debug-info h4 {{ margin-bottom: 10px; color: #495057; }}
        .debug-info p {{ font-size: 12px; color: #6c757d; }}
        .footer {{ background: #f8f9fa; padding: 25px; text-align: center; color: #6c757d; font-size: 14px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <div class=""error-icon"">?</div>
            <h1>Verification Failed</h1>
            <p>Email verification could not be completed</p>
        </div>
        <div class=""content"">
            <div class=""error-message"">Invalid or Expired Token</div>
            <div class=""description"">
                We couldn't verify your email address. This might happen for several reasons listed below.
            </div>
            <div class=""reasons"">
                <h4>Possible Reasons:</h4>
                <ul>
                    <li>The verification link has expired (links are valid for 72 hours)</li>
                    <li>The link has been used already</li>
                    <li>The link was copied incorrectly</li>
                    <li>Your email client modified the link</li>
                </ul>
            </div>
            <button class=""btn"" onclick=""window.close()"">Request New Link</button>
            <a href=""/"" class=""btn"">Go to Homepage</a>
            <div class=""debug-info"">
                <h4>Debug Information (For Developers)</h4>
                <p><strong>Original Token:</strong> {debugToken1}</p>
                <p><strong>Decoded Token:</strong> {debugToken2}</p>
                <p><strong>Timestamp:</strong> {timestamp} UTC</p>
            </div>
        </div>
        <div class=""footer"">� 2024 AuthAPI - Secure Authentication Platform</div>
    </div>
</body>
</html>";
        }

        private string GetPasswordResetPage(string token)
        {
            return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Password Reset - AuthAPI</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh; padding: 20px; display: flex; align-items: center; justify-content: center;
        }}
        .container {{ 
            max-width: 450px; width: 100%; background: #ffffff; border-radius: 20px;
            box-shadow: 0 20px 60px rgba(0, 0, 0, 0.1); overflow: hidden;
        }}
        .header {{ 
            background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%);
            padding: 40px 30px; text-align: center; color: white;
        }}
        .logo {{ font-size: 48px; margin-bottom: 15px; }}
        .header h1 {{ font-size: 28px; font-weight: 700; margin-bottom: 8px; }}
        .header p {{ font-size: 16px; opacity: 0.9; }}
        .form-container {{ padding: 40px 35px; }}
        .welcome-text {{ text-align: center; font-size: 20px; color: #2c3e50; margin-bottom: 30px; font-weight: 600; }}
        .form-group {{ margin-bottom: 25px; }}
        .form-group label {{ 
            display: block; margin-bottom: 8px; font-weight: 600; color: #2c3e50; 
            font-size: 14px; text-transform: uppercase; letter-spacing: 0.5px;
        }}
        .form-group input {{ 
            width: 100%; padding: 15px 20px; border: 2px solid #e9ecef; border-radius: 12px;
            font-size: 16px; transition: all 0.3s ease; background: #f8f9fa;
        }}
        .form-group input:focus {{ 
            outline: none; border-color: #667eea; background: white;
            box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.1);
        }}
        .password-requirements {{ font-size: 12px; color: #6c757d; margin-top: 8px; line-height: 1.4; }}
        .submit-button {{ 
            width: 100%; padding: 16px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white; border: none; border-radius: 12px; font-size: 18px; font-weight: 600;
            cursor: pointer; transition: all 0.3s ease; text-transform: uppercase; letter-spacing: 1px; margin-top: 10px;
        }}
        .submit-button:hover {{ transform: translateY(-2px); box-shadow: 0 10px 30px rgba(102, 126, 234, 0.3); }}
        .submit-button:disabled {{ background: #6c757d; cursor: not-allowed; transform: none; box-shadow: none; }}
        .message {{ 
            padding: 15px 20px; border-radius: 12px; margin: 20px 0; font-weight: 500; text-align: center;
        }}
        .success {{ background: linear-gradient(135deg, #00b894 0%, #00a085 100%); color: white; }}
        .error {{ background: linear-gradient(135deg, #ff7675 0%, #fd79a8 100%); color: white; }}
        .strength-indicator {{ 
            margin-top: 10px; padding: 8px 12px; border-radius: 8px; font-size: 12px;
            font-weight: 600; text-align: center; transition: all 0.3s ease; display: none;
        }}
        .strength-weak {{ background: linear-gradient(135deg, #ff7675 0%, #fd79a8 100%); color: white; }}
        .strength-medium {{ background: linear-gradient(135deg, #fdcb6e 0%, #e84393 100%); color: white; }}
        .strength-strong {{ background: linear-gradient(135deg, #00b894 0%, #00a085 100%); color: white; }}
        .security-info {{ 
            background: linear-gradient(135deg, #74b9ff 0%, #0984e3 100%);
            padding: 20px; border-radius: 15px; margin: 25px 0; color: white; text-align: center;
        }}
        .security-info h4 {{ margin-bottom: 10px; font-size: 14px; }}
        .security-info p {{ font-size: 12px; opacity: 0.9; }}
        .footer {{ background: #f8f9fa; padding: 20px; text-align: center; color: #6c757d; font-size: 14px; }}
        .loading {{ 
            display: inline-block; width: 20px; height: 20px; border: 2px solid rgba(255,255,255,0.3);
            border-radius: 50%; border-top-color: #fff; animation: spin 1s ease-in-out infinite; margin-right: 10px;
        }}
        @keyframes spin {{ to {{ transform: rotate(360deg); }} }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <div class=""logo"">??</div>
            <h1>Password Reset</h1>
            <p>Create your new secure password</p>
        </div>
        <div class=""form-container"">
            <div class=""welcome-text"">Create a secure new password</div>
            <div id=""message""></div>
            <form id=""resetForm"">
                <input type=""hidden"" id=""token"" value=""{token}"">
                <div class=""form-group"">
                    <label for=""newPassword"">New Password</label>
                    <input type=""password"" id=""newPassword"" placeholder=""Enter a strong password"" required>
                    <div class=""password-requirements"">
                        At least 8 characters with uppercase, lowercase, number and special character (@$!%*?&)
                    </div>
                    <div id=""strengthIndicator"" class=""strength-indicator""></div>
                </div>
                <div class=""form-group"">
                    <label for=""confirmPassword"">Confirm Password</label>
                    <input type=""password"" id=""confirmPassword"" placeholder=""Re-enter your password"" required>
                </div>
                <button type=""submit"" class=""submit-button"" id=""submitBtn"">Reset My Password</button>
            </form>
            <div class=""security-info"">
                <h4>Security Tip</h4>
                <p>Change your password regularly and never share it with anyone. After this process, you may need to log in again on all your devices.</p>
            </div>
        </div>
        <div class=""footer"">� 2024 AuthAPI - Secure Authentication Platform</div>
    </div>
    <script>
        const newPasswordInput = document.getElementById('newPassword');
        const confirmPasswordInput = document.getElementById('confirmPassword');
        const strengthIndicator = document.getElementById('strengthIndicator');
        const submitBtn = document.getElementById('submitBtn');
        
        function checkPasswordStrength(password) {{
            const hasLower = /[a-z]/.test(password);
            const hasUpper = /[A-Z]/.test(password);
            const hasNumber = /[0-9]/.test(password);
            const hasSpecial = /[@$!%*?&]/.test(password);
            const isLongEnough = password.length >= 8;
            const score = [hasLower, hasUpper, hasNumber, hasSpecial, isLongEnough].filter(Boolean).length;
            
            if (score === 5) return {{ strength: 'Strong', className: 'strength-strong' }};
            else if (score >= 3) return {{ strength: 'Medium', className: 'strength-medium' }};
            else return {{ strength: 'Weak', className: 'strength-weak' }};
        }}
        
        newPasswordInput.addEventListener('input', function() {{
            const password = this.value;
            if (password.length > 0) {{
                const result = checkPasswordStrength(password);
                strengthIndicator.textContent = result.strength;
                strengthIndicator.className = 'strength-indicator ' + result.className;
                strengthIndicator.style.display = 'block';
            }} else {{
                strengthIndicator.style.display = 'none';
            }}
        }});
        
        document.getElementById('resetForm').addEventListener('submit', async function(e) {{
            e.preventDefault();
            const tokenVal = document.getElementById('token').value;
            const newPassword = newPasswordInput.value;
            const confirmPassword = confirmPasswordInput.value;
            const messageDiv = document.getElementById('message');
            
            if (newPassword !== confirmPassword) {{
                showMessage('Passwords do not match!', 'error');
                return;
            }}
            
            if (newPassword.length < 8) {{
                showMessage('Password must be at least 8 characters long!', 'error');
                return;
            }}
            
            const strengthResult = checkPasswordStrength(newPassword);
            if (strengthResult.className === 'strength-weak') {{
                showMessage('Please choose a stronger password!', 'error');
                return;
            }}
            
            submitBtn.disabled = true;
            submitBtn.innerHTML = '<span class=""loading""></span>Resetting Password...';
            
            try {{
                const response = await fetch('/api/auth/reset-password', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{ token: tokenVal, newPassword: newPassword }})
                }});
                
                const result = await response.json();
                
                if (response.ok) {{
                    showMessage('Password reset successfully! Redirecting...', 'success');
                    document.getElementById('resetForm').style.display = 'none';
                    setTimeout(() => {{ window.close(); }}, 3000);
                }} else {{
                    showMessage(result.message, 'error');
                    submitBtn.disabled = false;
                    submitBtn.innerHTML = 'Reset My Password';
                }}
            }} catch (error) {{
                showMessage('An error occurred. Please try again.', 'error');
                submitBtn.disabled = false;
                submitBtn.innerHTML = 'Reset My Password';
            }}
        }});
        
        function showMessage(text, type) {{
            const messageDiv = document.getElementById('message');
            messageDiv.innerHTML = '<div class=""message ' + type + '"">' + text + '</div>';
            if (type === 'error') {{
                setTimeout(() => {{ messageDiv.innerHTML = ''; }}, 5000);
            }}
        }}
    </script>
</body>
</html>";
        }

        private string GetAccountDeletionSuccessPage()
        {
            return @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Account Deleted Successfully - AuthAPI</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #2ecc71 0%, #27ae60 100%);
            min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 20px;
        }
        .container { 
            max-width: 500px; width: 100%; background: #ffffff; border-radius: 20px;
            box-shadow: 0 20px 60px rgba(0, 0, 0, 0.1); overflow: hidden; animation: slideUp 0.8s ease-out;
        }
        @keyframes slideUp { from { opacity: 0; transform: translateY(50px); } to { opacity: 1, transform: translateY(0); } }
        .header { 
            background: linear-gradient(135deg, #27ae60 0%, #229954 100%);
            padding: 50px 30px; text-align: center; color: white;
        }
        .success-icon { font-size: 80px; margin-bottom: 20px; }
        .header h1 { font-size: 32px; font-weight: 700; margin-bottom: 10px; }
        .header p { font-size: 18px; opacity: 0.9; }
        .content { padding: 50px 40px; text-align: center; }
        .success-message { font-size: 24px; color: #27ae60; margin-bottom: 20px; font-weight: 600; }
        .description { font-size: 16px; color: #5a6c7d; margin-bottom: 35px; line-height: 1.6; }
        .info-box { background: #f8f9fa; padding: 25px; border-radius: 15px; margin: 25px 0; border-left: 4px solid #27ae60; }
        .footer { background: #2c3e50; color: #95a5a6; padding: 30px; text-align: center; font-size: 14px; }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <div class=""success-icon"">?</div>
            <h1>Account Deleted</h1>
            <p>Your AuthAPI account has been permanently removed</p>
        </div>
        <div class=""content"">
            <div class=""success-message"">Account Successfully Deleted</div>
            <div class=""description"">
                Your AuthAPI account has been permanently deleted from our servers. All associated data has been removed as requested.
            </div>
            <div class=""info-box"">
                <h4 style=""color: #2c3e50; margin-bottom: 15px;"">What happened:</h4>
                <ul style=""text-align: left; color: #5a6c7d; padding-left: 20px;"">
                    <li>Your account and all personal data have been permanently deleted</li>
                    <li>All authentication tokens have been invalidated</li>
                    <li>Your email address is now available for future registration</li>
                    <li>This action cannot be undone</li>
                </ul>
            </div>
            <div style=""margin-top: 30px; padding: 20px; background: #e8f5e8; border-radius: 10px;"">
                <p style=""color: #2d5a2d; font-weight: 600;"">
                    Thank you for using AuthAPI. If you decide to return in the future, you're always welcome to create a new account.
                </p>
            </div>
        </div>
        <div class=""footer"">
            � 2024 AuthAPI - Secure Authentication Platform<br>
            This window will close automatically in 10 seconds.
        </div>
    </div>
    <script>
        setTimeout(() => {
            window.close();
        }, 10000);
    </script>
</body>
</html>";
        }

        private string GetAccountDeletionErrorPage(string errorMessage)
        {
            return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Account Deletion Failed - AuthAPI</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #ff9a9e 0%, #fecfef 100%);
            min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 20px;
        }}
        .container {{ 
            max-width: 500px; width: 100%; background: #ffffff; border-radius: 20px;
            box-shadow: 0 20px 60px rgba(0, 0, 0, 0.1); overflow: hidden;
        }}
        .header {{ 
            background: linear-gradient(135deg, #e74c3c 0%, #c0392b 100%);
            padding: 50px 30px; text-align: center; color: white;
        }}
        .error-icon {{ font-size: 80px; margin-bottom: 20px; }}
        .header h1 {{ font-size: 32px; font-weight: 700; margin-bottom: 10px; }}
        .content {{ padding: 50px 40px; text-align: center; }}
        .error-message {{ font-size: 24px; color: #e74c3c; margin-bottom: 20px; font-weight: 600; }}
        .description {{ font-size: 16px; color: #5a6c7d; margin-bottom: 35px; line-height: 1.6; }}
        .reasons {{ background: #fff3cd; padding: 20px; border-radius: 15px; margin: 25px 0; text-align: left; }}
        .reasons h4 {{ color: #856404; margin-bottom: 15px; }}
        .reasons ul {{ color: #856404; padding-left: 20px; }}
        .reasons li {{ margin-bottom: 8px; font-size: 14px; }}
        .footer {{ background: #f8f9fa; padding: 25px; text-align: center; color: #6c757d; font-size: 14px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <div class=""error-icon"">?</div>
            <h1>Deletion Failed</h1>
            <p>Account deletion could not be completed</p>
        </div>
        <div class=""content"">
            <div class=""error-message"">Account Deletion Failed</div>
            <div class=""description"">
                {errorMessage}
            </div>
            <div class=""reasons"">
                <h4>Possible Reasons:</h4>
                <ul>
                    <li>The deletion link has expired (links are valid for 24 hours)</li>
                    <li>The link has been used already</li>
                    <li>The link was copied incorrectly</li>
                    <li>The account has already been deleted</li>
                </ul>
            </div>
        </div>
        <div class=""footer"">� 2024 AuthAPI - Secure Authentication Platform</div>
    </div>
</body>
</html>";
        }
    }
}