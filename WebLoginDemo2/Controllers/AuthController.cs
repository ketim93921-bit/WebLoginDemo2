using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using WebLoginDemo2.Data;
using WebLoginDemo2.Models;
using WebLoginDemo2.ViewModels;

namespace WebLoginDemo2.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IPasswordHasher<AppUser> _passwordHasher;

        public AuthController(AppDbContext db, IPasswordHasher<AppUser> passwordHasher)
        {
            _db = db;
            _passwordHasher = passwordHasher;
        }

        // ==============================
        // 登入頁面
        // GET: /Auth/Login
        // ==============================
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (TempData["SuccessMessage"] != null)
            {
                ViewData["SuccessMessage"] = TempData["SuccessMessage"];
            }

            return View(new LoginViewModel
            {
                ReturnUrl = returnUrl
            });
        }

        // ==============================
        // 登入處理
        // POST: /Auth/Login
        // ==============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // 目前使用明碼比對
            // 你的 PasswordHash 欄位暫時存的是明碼密碼
            var user = await _db.AppUsers
                .SingleOrDefaultAsync(u =>
                    u.UserName == model.UserName &&
                    u.PasswordHash == model.Password);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "帳號或密碼錯誤。");
                return View(model);
            }

            // 建立登入 Cookie
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName)
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme
            );

            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                }
            );

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Dashboard");
        }

        // ==============================
        // 註冊頁面
        // 如果你不用註冊，可以不放連結
        // ==============================
        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        // ==============================
        // 註冊處理
        // 目前暫時使用明碼儲存
        // ==============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            bool userExists = await _db.AppUsers
                .AnyAsync(u => u.UserName == model.UserName);

            if (userExists)
            {
                ModelState.AddModelError("UserName", "此帳號已被使用。");
                return View(model);
            }

            var newUser = new AppUser
            {
                UserName = model.UserName,
                Email = model.Email ?? string.Empty,

                // 暫時存明碼
                // 之後正式版建議改成 _passwordHasher.HashPassword()
                PasswordHash = model.Password,

                CreatedAt = DateTime.Now
            };

            _db.AppUsers.Add(newUser);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "帳號註冊成功，請使用您的新帳號登入。";

            return RedirectToAction(nameof(Login));
        }

        // ==============================
        // 登出
        // GET: /Auth/Logout
        // ==============================
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme
            );

            return RedirectToAction(nameof(Login));
        }
    }
}