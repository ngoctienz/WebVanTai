using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WebVanTai.Areas.Admin.ViewModels
{
    public class RouteStatItemDto
    {
        public int RouteId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public string StartProvince { get; set; } = string.Empty;
        public string EndProvince { get; set; } = string.Empty;
        public int TripsCount { get; set; }
        public int TotalSeats { get; set; }
        public int SoldSeats { get; set; }
        public int CheckedInSeats { get; set; }
        public double OccupancyRate => TotalSeats > 0 ? Math.Round((double)SoldSeats / TotalSeats * 100, 1) : 0;
        public double CheckInRate => SoldSeats > 0 ? Math.Round((double)CheckedInSeats / SoldSeats * 100, 1) : 0;
        public decimal TotalRevenue { get; set; }
        public decimal AvgRevenuePerTrip => TripsCount > 0 ? Math.Round(TotalRevenue / TripsCount, 0) : 0;
    }

    public class DailyRevenueItemDto
    {
        public string DateLabel { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public int TicketsCount { get; set; }
    }

    public class TicketStatusBreakdownDto
    {
        public int ValidCount { get; set; }
        public int CheckedInCount { get; set; }
        public int VoidCount { get; set; }
        public int Total => ValidCount + CheckedInCount + VoidCount;
    }

    public class AdminDashboardViewModel
    {
        public string Period { get; set; } = "all"; // all, today, week, month
        public decimal TotalRevenue { get; set; }
        public decimal TodayRevenue { get; set; }
        public decimal ThisWeekRevenue { get; set; }
        public decimal ThisMonthRevenue { get; set; }
        
        public int TotalBookings { get; set; }
        public int TotalTrips { get; set; }
        public int ScheduledTrips { get; set; }
        public int RunningTrips { get; set; }
        public int CompletedTrips { get; set; }
        
        public int TotalSeatsSystem { get; set; }
        public int TotalSeatsSold { get; set; }
        public int TotalSeatsCheckedIn { get; set; }
        public int TotalSeatsVoid { get; set; }

        public double OverallOccupancyRate => TotalSeatsSystem > 0 ? Math.Round((double)TotalSeatsSold / TotalSeatsSystem * 100, 1) : 0;
        public double OverallCheckInRate => TotalSeatsSold > 0 ? Math.Round((double)TotalSeatsCheckedIn / TotalSeatsSold * 100, 1) : 0;

        public List<RouteStatItemDto> RouteStats { get; set; } = new();
        public List<DailyRevenueItemDto> DailyRevenueStats { get; set; } = new();
        public TicketStatusBreakdownDto TicketStatusBreakdown { get; set; } = new();
    }

    public class UserItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public bool IsLockedOut { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public string PrimaryRole => Roles.Count > 0 ? Roles[0] : "Customer";
    }

    public class UserManagementViewModel
    {
        public List<UserItemDto> Users { get; set; } = new();
        public string? Search { get; set; }
        public string? RoleFilter { get; set; }
        public string? StatusFilter { get; set; }
        
        public int TotalStaff { get; set; }
        public int TotalAdmins { get; set; }
        public int TotalRouteManagers { get; set; }
        public int TotalTicketInspectors { get; set; }
        public int TotalCustomers { get; set; }
    }

    public class CreateStaffViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập họ và tên")]
        [StringLength(100, ErrorMessage = "Họ tên không được vượt quá 100 ký tự")]
        [Display(Name = "Họ và tên")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Email")]
        [EmailAddress(ErrorMessage = "Địa chỉ Email không hợp lệ")]
        [Display(Name = "Địa chỉ Email")]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Số điện thoại không đúng định dạng")]
        [Display(Name = "Số điện thoại")]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải từ 6 ký tự trở lên")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Xác nhận mật khẩu")]
        [Compare("Password", ErrorMessage = "Mật khẩu xác nhận không khớp")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng chọn vai trò")]
        [Display(Name = "Vai trò phân quyền")]
        public string Role { get; set; } = "TicketInspector";
    }

    public class ChangeRoleViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string NewRole { get; set; } = string.Empty;
    }
}
