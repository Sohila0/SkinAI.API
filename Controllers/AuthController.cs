using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SkinAI.API.Data;
using SkinAI.API.Dtos;
using SkinAI.API.Dtos.Auth;
using SkinAI.API.Models;
using SkinAI.API.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;

        public AuthController(
            UserManager<User> userManager,
            IConfiguration config,
            ApplicationDbContext context,
            IEmailService emailService)
        {
            _userManager = userManager;
            _config = config;
            _context = context;
            _emailService = emailService;
        }

        // ========================= REGISTER (Patient) =========================
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { ok = false, error = "Invalid data", details = ModelState });

            var email = (dto.Email ?? "").Trim().ToLowerInvariant();

            var existing = await _userManager.FindByEmailAsync(email);
            if (existing != null)
                return BadRequest(new { ok = false, error = "Email already exists" });

            var user = new User
            {
                UserName = email,
                Email = email,
                FullName = dto.FullName?.Trim() ?? "",
                Address = dto.Address,        // الجديد
                Latitude = dto.Latitude,      // لو ضفتيهم
                Longitude = dto.Longitude,
                Role = "Patient",
                IsApproved = true,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
                return BadRequest(new { ok = false, error = "Failed", details = result.Errors.Select(e => e.Description) });

            await _userManager.AddToRoleAsync(user, "Patient");

            var patient = new Patient
            {
                UserId = user.Id,
                DateOfBirth = dto.DateOfBirth,
                CreatedAt = DateTime.UtcNow
            };

            _context.Patients.Add(patient);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                ok = true,
                message = "Registered Successfully",
                userId = user.Id,
                patientId = patient.Id
            });
        }

        // ========================= LOGIN =========================
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { ok = false, error = "Invalid data", details = ModelState });

            var email = (dto.Email ?? "").Trim().ToLowerInvariant();

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return Unauthorized(new { ok = false, error = "Invalid email or password" });

            if (!user.IsActive)
                return Unauthorized(new { ok = false, error = "Account is disabled" });

            var ok = await _userManager.CheckPasswordAsync(user, dto.Password);
            if (!ok)
                return Unauthorized(new { ok = false, error = "Invalid email or password" });

            if ((user.Role == "Doctor") && !user.IsApproved)
                return Unauthorized(new { ok = false, error = "Doctor account is pending approval" });

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? user.Role ?? "Patient";

            var token = GenerateJwtToken(user, role);

            return Ok(new
            {
                ok = true,
                token,
                userId = user.Id,
                role,
                isApproved = user.IsApproved,
                FullName = user.FullName,
                
                email = user.Email
            });
        }

        // ========================= FORGOT PASSWORD =========================
        // sends deep link to app: skinai://reset-password?email=...&token=...
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { ok = false, error = "Invalid data", details = ModelState });

            var email = (dto.Email ?? "").Trim().ToLowerInvariant();

            // Always return same response (security)
            var genericOk = new { ok = true, message = "If the email exists, a reset link will be sent." };

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return Ok(genericOk);

            if (!user.IsActive) return Ok(genericOk);

            // Generate token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            // URL-encode
            var tokenEncoded = Uri.EscapeDataString(token);
            var emailEncoded = Uri.EscapeDataString(email);

            var deepLinkBase = _config["Email:ResetDeepLinkBase"] ?? "skinai://reset-password";
            var deepLink = $"{deepLinkBase}?email={emailEncoded}&token={tokenEncoded}";

            var subject = "SkinAI - Reset Password";
            var htmlBody = $@"
                <div style='font-family:Arial'>
                  <h3>Reset your password</h3>
                  <p>Tap the button below to reset your password in the app.</p>
                  <p>
                    <a href='{deepLink}'
                       style='display:inline-block;padding:12px 18px;background:#2D6CDF;color:white;text-decoration:none;border-radius:8px'>
                      Reset Password
                    </a>
                  </p>
                  <p>If the button doesn't work, copy this link:</p>
                  <p>{deepLink}</p>
                  <hr/>
                  <p style='color:#666'>If you did not request this, ignore this email.</p>
                </div>";

            try
            {
                await _emailService.SendAsync(email, subject, htmlBody, ct);
            }
            catch
            {
                // Don't reveal internal smtp errors to user
                return Ok(genericOk);
            }

            return Ok(genericOk);
        }

        // ========================= RESET PASSWORD =========================
        // app sends: email + token + newPassword
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { ok = false, error = "Invalid data", details = ModelState });

            var email = (dto.Email ?? "").Trim().ToLowerInvariant();

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return BadRequest(new { ok = false, error = "Invalid request." });

            if (!user.IsActive)
                return BadRequest(new { ok = false, error = "Account is disabled." });

            // token may be encoded in query
            var token = Uri.UnescapeDataString(dto.Token ?? "");

            var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    ok = false,
                    error = "Reset failed",
                    details = result.Errors.Select(e => e.Description)
                });
            }

            return Ok(new { ok = true, message = "Password reset successfully." });
        }

        // ========================= JWT =========================
        private string GenerateJwtToken(User user, string role)
        {
            var jwt = _config.GetSection("Jwt");
            var key = jwt["Key"] ?? throw new Exception("Jwt:Key missing");
            var issuer = jwt["Issuer"] ?? throw new Exception("Jwt:Issuer missing");
            var audience = jwt["Audience"] ?? throw new Exception("Jwt:Audience missing");

            if (Encoding.UTF8.GetByteCount(key) < 32)
                throw new Exception("JWT Key must be at least 32 bytes (256 bits).");

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, role),
            };

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
