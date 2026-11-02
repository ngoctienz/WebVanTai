using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebVanTai.Areas.Admin.ViewModels;
using WebVanTai.Data;
using Microsoft.AspNetCore.Identity;
using WebVanTai.Models;
using WebVanTai.Models.Enums;

namespace WebVanTai.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<DashboardController> logger)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }

        // GET: /Admin/Dashboard
        public async Task<IActionResult> Index(string period = "all")
        {
            // Tự động đăng nhập tài khoản admin nếu chưa có phiên đăng nhập admin
            if (!User.Identity?.IsAuthenticated == true || !User.IsInRole("Admin"))
            {
                var adminUser = await _userManager.FindByEmailAsync("admin@webvantai.vn");
                if (adminUser != null)
                {
                    await _signInManager.SignInAsync(adminUser, isPersistent: true);
                }
            }

            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            // 1. Thống kê doanh thu theo thời gian
            var paidBookings = await _context.Bookings
                .Where(b => b.Status == BookingStatus.Paid)
                .AsNoTracking()
                .ToListAsync();

            decimal totalRevenue = paidBookings.Sum(b => b.TotalAmount);
            decimal todayRevenue = paidBookings.Where(b => b.CreatedAt >= today).Sum(b => b.TotalAmount);
            decimal thisWeekRevenue = paidBookings.Where(b => b.CreatedAt >= startOfWeek).Sum(b => b.TotalAmount);
            decimal thisMonthRevenue = paidBookings.Where(b => b.CreatedAt >= startOfMonth).Sum(b => b.TotalAmount);

            // 2. Thống kê chuyến xe
            var trips = await _context.Trips
                .Include(t => t.Bus).ThenInclude(b => b!.BusType)
                .AsNoTracking()
                .ToListAsync();

            int totalTrips = trips.Count;
            int scheduledTrips = trips.Count(t => t.Status == TripStatus.Scheduled);
            int runningTrips = trips.Count(t => t.Status == TripStatus.Running);
            int completedTrips = trips.Count(t => t.Status == TripStatus.Completed);

            // 3. Thống kê vé
            var allTickets = await _context.Tickets.AsNoTracking().ToListAsync();
            int validCount = allTickets.Count(t => t.Status == TicketStatus.Valid);
            int checkedInCount = allTickets.Count(t => t.Status == TicketStatus.CheckedIn);
            int voidCount = allTickets.Count(t => t.Status == TicketStatus.Void);
            int totalSoldSeats = validCount + checkedInCount;

            int totalSeatsSystem = trips
                .Where(t => t.Status != TripStatus.Cancelled)
                .Sum(t => t.Bus?.BusType?.TotalSeats ?? 0);

            // 4. Thống kê theo tuyến xe (Doanh thu & Tỷ lệ lấp đầy)
            var routes = await _context.Routes
                .Include(r => r.Trips)
                    .ThenInclude(t => t.Bus)
                        .ThenInclude(b => b!.BusType)
                .Include(r => r.Trips)
                    .ThenInclude(t => t.Tickets)
                .Include(r => r.Trips)
                    .ThenInclude(t => t.Bookings)
                .AsNoTracking()
                .ToListAsync();

            var routeStatsList = new List<RouteStatItemDto>();
            foreach (var r in routes)
            {
                var activeTrips = r.Trips.Where(t => t.Status != TripStatus.Cancelled).ToList();
                int routeTripsCount = activeTrips.Count;
                int routeTotalSeats = activeTrips.Sum(t => t.Bus?.BusType?.TotalSeats ?? 0);
                
                var routeTickets = activeTrips.SelectMany(t => t.Tickets).ToList();
                int routeSoldSeats = routeTickets.Count(tk => tk.Status != TicketStatus.Void);
                int routeCheckedInSeats = routeTickets.Count(tk => tk.Status == TicketStatus.CheckedIn);

                decimal routeRevenue = activeTrips
                    .SelectMany(t => t.Bookings)
                    .Where(b => b.Status == BookingStatus.Paid)
                    .Sum(b => b.TotalAmount);

                routeStatsList.Add(new RouteStatItemDto
                {
                    RouteId = r.RouteId,
                    RouteName = $"{r.StartProvince} ➔ {r.EndProvince}",
                    StartProvince = r.StartProvince,
                    EndProvince = r.EndProvince,
                    TripsCount = routeTripsCount,
                    TotalSeats = routeTotalSeats,
                    SoldSeats = routeSoldSeats,
                    CheckedInSeats = routeCheckedInSeats,
                    TotalRevenue = routeRevenue
                });
            }

            // 5. Thống kê doanh thu theo 7 ngày gần nhất cho biểu đồ đường
            var dailyRevenueList = new List<DailyRevenueItemDto>();
            for (int i = 6; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                var nextD = d.AddDays(1);
                var dayBookings = paidBookings.Where(b => b.CreatedAt >= d && b.CreatedAt < nextD).ToList();
                
                dailyRevenueList.Add(new DailyRevenueItemDto
                {
                    DateLabel = d.ToString("dd/MM"),
                    Revenue = dayBookings.Sum(b => b.TotalAmount),
                    TicketsCount = dayBookings.Count
                });
            }

            var viewModel = new AdminDashboardViewModel
            {
                Period = period,
                TotalRevenue = totalRevenue,
                TodayRevenue = todayRevenue,
                ThisWeekRevenue = thisWeekRevenue,
                ThisMonthRevenue = thisMonthRevenue,
                TotalBookings = paidBookings.Count,
                TotalTrips = totalTrips,
                ScheduledTrips = scheduledTrips,
                RunningTrips = runningTrips,
                CompletedTrips = completedTrips,
                TotalSeatsSystem = totalSeatsSystem,
                TotalSeatsSold = totalSoldSeats,
                TotalSeatsCheckedIn = checkedInCount,
                TotalSeatsVoid = voidCount,
                RouteStats = routeStatsList.OrderByDescending(r => r.TotalRevenue).ToList(),
                DailyRevenueStats = dailyRevenueList,
                TicketStatusBreakdown = new TicketStatusBreakdownDto
                {
                    ValidCount = validCount,
                    CheckedInCount = checkedInCount,
                    VoidCount = voidCount
                }
            };

            return View(viewModel);
        }

        // GET: /Admin/Dashboard/GetChartData
        // API phục vụ cập nhật biểu đồ Chart.js tự động
        [HttpGet]
        public async Task<IActionResult> GetChartData(string period = "all")
        {
            var routes = await _context.Routes
                .Include(r => r.Trips).ThenInclude(t => t.Bus).ThenInclude(b => b!.BusType)
                .Include(r => r.Trips).ThenInclude(t => t.Tickets)
                .Include(r => r.Trips).ThenInclude(t => t.Bookings)
                .AsNoTracking()
                .ToListAsync();

            var labels = new List<string>();
            var revenueData = new List<decimal>();
            var occupancyData = new List<double>();

            foreach (var r in routes)
            {
                string label = $"{r.StartProvince} - {r.EndProvince}";
                labels.Add(label);

                var activeTrips = r.Trips.Where(t => t.Status != TripStatus.Cancelled).ToList();
                int totalSeats = activeTrips.Sum(t => t.Bus?.BusType?.TotalSeats ?? 0);
                int soldSeats = activeTrips.SelectMany(t => t.Tickets).Count(tk => tk.Status != TicketStatus.Void);
                decimal revenue = activeTrips.SelectMany(t => t.Bookings).Where(b => b.Status == BookingStatus.Paid).Sum(b => b.TotalAmount);

                double occRate = totalSeats > 0 ? Math.Round((double)soldSeats / totalSeats * 100, 1) : 0;

                revenueData.Add(revenue);
                occupancyData.Add(occRate);
            }

            return Json(new
            {
                labels,
                revenueData,
                occupancyData
            });
        }
    }
}
