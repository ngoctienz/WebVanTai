using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebVanTai.Areas.Admin.ViewModels;
using WebVanTai.Models;

namespace WebVanTai.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ILogger<UsersController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _logger = logger;
        }

        // GET: /Admin/Users
        public async Task<IActionResult> Index(string? search, string? roleFilter, string? statusFilter)
        {
            // Tự động đăng nhập admin nếu chưa có phiên đăng nhập admin
            if (!User.Identity?.IsAuthenticated == true || !User.IsInRole("Admin"))
            {
                var adminUser = await _userManager.FindByEmailAsync("admin@webvantai.vn");
                if (adminUser != null)
                {
                    await _signInManager.SignInAsync(adminUser, isPersistent: true);
                }
            }

            var allUsers = await _userManager.Users.AsNoTracking().ToListAsync();
            var userList = new List<UserItemDto>();

            int countAdmins = 0;
            int countRouteManagers = 0;
            int countInspectors = 0;
            int countCustomers = 0;

            foreach (var user in allUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);
                bool isLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

                if (roles.Contains("Admin")) countAdmins++;
                if (roles.Contains("RouteManager")) countRouteManagers++;
                if (roles.Contains("TicketInspector")) countInspectors++;
                if (roles.Contains("Customer") || !roles.Any()) countCustomers++;

                userList.Add(new UserItemDto
                {
                    Id = user.Id,
                    FullName = user.FullName ?? "Chưa đặt tên",
                    Email = user.Email ?? "",
                    PhoneNumber = user.PhoneNumber ?? "",
                    Roles = roles.ToList(),
                    CreatedAt = user.CreatedAt,
                    IsLockedOut = isLocked,
                    LockoutEnd = user.LockoutEnd
                });
            }

            // Lọc theo từ khóa tìm kiếm
            if (!string.IsNullOrWhiteSpace(search))
            {
                string kw = search.Trim().ToLower();
                userList = userList.Where(u =>
                    u.FullName.ToLower().Contains(kw) ||
                    u.Email.ToLower().Contains(kw) ||
                    u.PhoneNumber.Contains(kw)
                ).ToList();
            }

            // Lọc theo vai trò
            if (!string.IsNullOrWhiteSpace(roleFilter) && roleFilter != "all")
            {
                userList = userList.Where(u => u.Roles.Contains(roleFilter)).ToList();
            }

            // Lọc theo trạng thái
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                if (statusFilter == "active")
                    userList = userList.Where(u => !u.IsLockedOut).ToList();
                else if (statusFilter == "locked")
                    userList = userList.Where(u => u.IsLockedOut).ToList();
            }

            var viewModel = new UserManagementViewModel
            {
                Users = userList.OrderByDescending(u => u.CreatedAt).ToList(),
                Search = search,
                RoleFilter = roleFilter,
                StatusFilter = statusFilter,
                TotalStaff = countAdmins + countRouteManagers + countInspectors,
                TotalAdmins = countAdmins,
                TotalRouteManagers = countRouteManagers,
                TotalTicketInspectors = countInspectors,
                TotalCustomers = countCustomers
            };

            return View(viewModel);
        }

        // POST: /Admin/Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromForm] CreateStaffViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Dữ liệu nhập vào chưa hợp lệ. Vui lòng kiểm tra lại.";
                return RedirectToAction(nameof(Index));
            }

            var existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing != null)
            {
                TempData["ErrorMessage"] = $"Email '{model.Email}' đã tồn tại trong hệ thống!";
                return RedirectToAction(nameof(Index));
            }

            var user = new ApplicationUser
            {
                UserName = model.Email.Trim(),
                Email = model.Email.Trim(),
                EmailConfirmed = true,
                FullName = model.FullName.Trim(),
                PhoneNumber = model.PhoneNumber?.Trim(),
                CreatedAt = DateTime.UtcNow,
                LockoutEnabled = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join("; ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            // Gán Role (RouteManager, TicketInspector, Admin, Customer)
            string roleToAssign = string.IsNullOrWhiteSpace(model.Role) ? "TicketInspector" : model.Role;
            if (!await _roleManager.RoleExistsAsync(roleToAssign))
            {
                await _roleManager.CreateAsync(new IdentityRole(roleToAssign));
            }

            await _userManager.AddToRoleAsync(user, roleToAssign);

            TempData["SuccessMessage"] = $"Tạo tài khoản nhân sự '{user.FullName}' với vai trò '{roleToAssign}' thành công!";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/ChangeRole
        // Cấp quyền RouteManager, TicketInspector, Admin...
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRole(string userId, string newRole)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(newRole))
            {
                return Json(new { success = false, message = "Thông tin không hợp lệ." });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Json(new { success = false, message = "Không tìm thấy người dùng." });
            }

            // Đảm bảo role tồn tại
            if (!await _roleManager.RoleExistsAsync(newRole))
            {
                await _roleManager.CreateAsync(new IdentityRole(newRole));
            }

            var currentRoles = await _userManager.GetRolesAsync(user);

            // Gỡ các vai trò cũ và gán vai trò mới
            if (currentRoles.Any())
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            }
            await _userManager.AddToRoleAsync(user, newRole);

            string msg = $"Đã cập nhật phân quyền thành công! Tài khoản '{user.FullName}' hiện có vai trò: {newRole}.";

            if (Request?.Headers != null && Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, message = msg, newRole });
            }

            if (TempData != null)
            {
                TempData["SuccessMessage"] = msg;
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/ToggleLockout
        // Khóa hoặc mở khóa tài khoản nhân sự
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLockout(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Json(new { success = false, message = "Mã người dùng không hợp lệ." });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Json(new { success = false, message = "Không tìm thấy người dùng." });
            }

            // Bật tính năng Lockout cho user nếu chưa bật
            if (!user.LockoutEnabled)
            {
                user.LockoutEnabled = true;
                await _userManager.UpdateAsync(user);
            }

            bool currentlyLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
            string msg;
            bool newLockedState;

            if (currentlyLocked)
            {
                // Mở khóa
                await _userManager.SetLockoutEndDateAsync(user, null);
                msg = $"Đã MỞ KHÓA tài khoản '{user.FullName}' ({user.Email}). Người dùng có thể đăng nhập bình thường.";
                newLockedState = false;
            }
            else
            {
                // Khóa tài khoản
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                await _userManager.UpdateSecurityStampAsync(user);
                msg = $"Đã KHÓA tài khoản '{user.FullName}' ({user.Email}). Tài khoản không thể đăng nhập cho đến khi được mở khóa.";
                newLockedState = true;
            }

            if (Request?.Headers != null && Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, isLocked = newLockedState, message = msg });
            }

            if (TempData != null)
            {
                TempData["SuccessMessage"] = msg;
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string userId, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                if (TempData != null) TempData["ErrorMessage"] = "Mật khẩu mới phải từ 6 ký tự trở lên.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                if (TempData != null) TempData["ErrorMessage"] = "Không tìm thấy người dùng.";
                return RedirectToAction(nameof(Index));
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

            if (result.Succeeded)
            {
                if (TempData != null) TempData["SuccessMessage"] = $"Đặt lại mật khẩu cho tài khoản '{user.FullName}' thành công!";
            }
            else
            {
                if (TempData != null) TempData["ErrorMessage"] = string.Join("; ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
