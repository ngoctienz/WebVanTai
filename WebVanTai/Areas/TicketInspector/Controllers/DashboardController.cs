using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebVanTai.Areas.TicketInspector.ViewModels;
using WebVanTai.Data;
using WebVanTai.Models;
using WebVanTai.Models.Enums;

namespace WebVanTai.Areas.TicketInspector.Controllers
{
    [Area("TicketInspector")]
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

        // GET: /TicketInspector/Dashboard
        public async Task<IActionResult> Index(int? tripId, DateTime? date, string filter = "today")
        {
            // Tự động đăng nhập tài khoản inspector nếu chưa đăng nhập (giúp truy cập trực tiếp thuận tiện)
            if (!User.Identity?.IsAuthenticated == true)
            {
                var inspectorUser = await _userManager.FindByEmailAsync("inspector@webvantai.vn");
                if (inspectorUser != null)
                {
                    await _signInManager.SignInAsync(inspectorUser, isPersistent: true);
                }
            }

            DateTime targetDate = DateTime.Today;
            if (filter == "tomorrow")
            {
                targetDate = DateTime.Today.AddDays(1);
            }
            else if (date.HasValue)
            {
                targetDate = date.Value.Date;
            }

            var query = _context.Trips
                .Include(t => t.Route)
                .Include(t => t.Bus)
                    .ThenInclude(b => b!.BusType)
                .Include(t => t.Tickets)
                .AsNoTracking();

            List<Trip> tripsList;

            if (filter == "all")
            {
                // Tất cả chuyến xe chưa hủy
                tripsList = await query
                    .Where(t => t.Status != TripStatus.Cancelled)
                    .OrderBy(t => t.DepartureTime)
                    .Take(30)
                    .ToListAsync();
            }
            else
            {
                // Lọc theo ngày
                var nextDay = targetDate.AddDays(1);
                tripsList = await query
                    .Where(t => t.DepartureTime >= targetDate && t.DepartureTime < nextDay && t.Status != TripStatus.Cancelled)
                    .OrderBy(t => t.DepartureTime)
                    .ToListAsync();

                // Nếu hôm nay không có chuyến nào, tự động tìm thêm chuyến ngày mai hoặc các chuyến gần nhất
                if (!tripsList.Any() && filter == "today" && !date.HasValue)
                {
                    tripsList = await query
                        .Where(t => t.Status != TripStatus.Cancelled && t.DepartureTime >= DateTime.Today)
                        .OrderBy(t => t.DepartureTime)
                        .Take(15)
                        .ToListAsync();
                    if (tripsList.Any())
                    {
                        targetDate = tripsList.First().DepartureTime.Date;
                    }
                }
            }

            var tripSummaryList = tripsList.Select(t => new TripSummaryItem
            {
                TripId = t.TripId,
                RouteName = $"{t.Route?.StartProvince ?? ""} ➔ {t.Route?.EndProvince ?? ""}",
                StartProvince = t.Route?.StartProvince ?? "",
                EndProvince = t.Route?.EndProvince ?? "",
                DepartureTime = t.DepartureTime,
                ArrivalTime = t.ArrivalTime,
                LicensePlate = t.Bus?.LicensePlate ?? "Chưa gán",
                BusTypeName = t.Bus?.BusType?.Name ?? "Tiêu chuẩn",
                BasePrice = t.BasePrice,
                TotalSeats = t.Bus?.BusType?.TotalSeats ?? 0,
                BookedSeats = t.Tickets.Count(tk => tk.Status != TicketStatus.Void),
                CheckedInSeats = t.Tickets.Count(tk => tk.Status == TicketStatus.CheckedIn),
                Status = t.Status
            }).ToList();

            var viewModel = new TicketInspectorDashboardViewModel
            {
                SelectedDate = targetDate,
                FilterMode = filter,
                Trips = tripSummaryList,
                SelectedTripId = tripId
            };

            // Nếu chưa chọn tripId, mặc định chọn chuyến đầu tiên trong danh sách
            if (!viewModel.SelectedTripId.HasValue && tripSummaryList.Any())
            {
                viewModel.SelectedTripId = tripSummaryList.First().TripId;
            }

            // Nếu đã có chuyến được chọn, load chi tiết sơ đồ ghế và danh sách vé
            if (viewModel.SelectedTripId.HasValue)
            {
                viewModel.SelectedTrip = tripSummaryList.FirstOrDefault(t => t.TripId == viewModel.SelectedTripId.Value);

                var selectedTripEntity = await _context.Trips
                    .Include(t => t.Route)
                    .Include(t => t.Bus)
                        .ThenInclude(b => b!.BusType)
                    .Include(t => t.Tickets)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TripId == viewModel.SelectedTripId.Value);

                if (selectedTripEntity != null)
                {
                    viewModel.SeatMap = BuildSeatMap(selectedTripEntity);

                    viewModel.Tickets = selectedTripEntity.Tickets
                        .Where(tk => tk.Status != TicketStatus.Void)
                        .OrderBy(tk => tk.SeatCode)
                        .Select(tk => new PassengerTicketDto
                        {
                            TicketId = tk.TicketId,
                            BookingId = tk.BookingId,
                            SeatCode = tk.SeatCode,
                            PassengerName = tk.PassengerName,
                            PassengerPhone = tk.PassengerPhone,
                            Status = tk.Status,
                            CheckedInAt = tk.CheckedInAt,
                            QrToken = tk.QrToken
                        }).ToList();
                }
            }

            return View(viewModel);
        }

        // POST: /TicketInspector/Dashboard/CheckIn
        // API kiểm tra QrToken theo TripId và cập nhật CheckedIn
        [HttpPost]
        public async Task<IActionResult> CheckIn([FromBody] CheckInRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.QrToken))
            {
                return Json(new CheckInResponseDto
                {
                    Success = false,
                    ResultType = "INVALID_REQUEST",
                    Message = "Dữ liệu mã QR trống! Vui lòng quét lại."
                });
            }

            string cleanToken = request.QrToken.Trim();

            // Hỗ trợ trường hợp QR chứa URL hoặc query param vd: ?token=xyz hoặc /ticket/xyz
            if (cleanToken.Contains("token="))
            {
                var parts = cleanToken.Split("token=");
                if (parts.Length > 1)
                {
                    cleanToken = parts[1].Split('&')[0];
                }
            }

            // Tìm kiếm vé
            var ticket = await _context.Tickets
                .Include(t => t.Trip)
                    .ThenInclude(tr => tr!.Route)
                .Include(t => t.Booking)
                .FirstOrDefaultAsync(t => t.QrToken == cleanToken || t.QrToken == request.QrToken.Trim());

            // Hỗ trợ quét mã dự phòng theo định dạng SeatCode hoặc TicketId
            if (ticket == null)
            {
                if (int.TryParse(cleanToken, out int tid))
                {
                    ticket = await _context.Tickets
                        .Include(t => t.Trip).ThenInclude(tr => tr!.Route)
                        .Include(t => t.Booking)
                        .FirstOrDefaultAsync(t => t.TicketId == tid);
                }
                else
                {
                    // Thử tìm theo mã ghế trong chuyến
                    ticket = await _context.Tickets
                        .Include(t => t.Trip).ThenInclude(tr => tr!.Route)
                        .Include(t => t.Booking)
                        .FirstOrDefaultAsync(t => t.TripId == request.TripId && t.SeatCode.ToUpper() == cleanToken.ToUpper() && t.Status != TicketStatus.Void);
                }
            }

            // 1. Không tìm thấy vé
            if (ticket == null)
            {
                return Json(new CheckInResponseDto
                {
                    Success = false,
                    ResultType = "NOT_FOUND",
                    Message = "Mã QR không hợp lệ hoặc không tồn tại trong hệ thống WebVanTai!"
                });
            }

            // 2. Sai chuyến xe
            if (ticket.TripId != request.TripId)
            {
                string routeInfo = ticket.Trip?.Route != null
                    ? $"{ticket.Trip.Route.StartProvince} ➔ {ticket.Trip.Route.EndProvince}"
                    : $"Chuyến #{ticket.TripId}";
                string timeInfo = ticket.Trip != null ? ticket.Trip.DepartureTime.ToString("HH:mm dd/MM/yyyy") : "";

                return Json(new CheckInResponseDto
                {
                    Success = false,
                    ResultType = "WRONG_TRIP",
                    Message = $"CẢNH BÁO SAI CHUYẾN! Vé của hành khách thuộc tuyến: {routeInfo} (Khởi hành: {timeInfo}). Không được phép lên chuyến này!",
                    PassengerName = ticket.PassengerName,
                    SeatCode = ticket.SeatCode,
                    PassengerPhone = ticket.PassengerPhone,
                    TicketId = ticket.TicketId
                });
            }

            // 3. Vé đã được soát trước đó
            if (ticket.Status == TicketStatus.CheckedIn)
            {
                string checkInTime = ticket.CheckedInAt.HasValue
                    ? ticket.CheckedInAt.Value.ToString("HH:mm:ss dd/MM/yyyy")
                    : "không xác định";

                return Json(new CheckInResponseDto
                {
                    Success = false,
                    ResultType = "ALREADY_CHECKED_IN",
                    Message = $"CẢNH BÁO: Vé ĐÃ ĐƯỢC SOÁT TRƯỚC ĐÓ vào lúc {checkInTime}! Vui lòng đối chiếu kỹ để tránh hành khách quét trùng.",
                    PassengerName = ticket.PassengerName,
                    SeatCode = ticket.SeatCode,
                    PassengerPhone = ticket.PassengerPhone,
                    CheckedInAt = checkInTime,
                    TicketId = ticket.TicketId,
                    Stats = await GetTripStatsAsync(request.TripId)
                });
            }

            // 4. Vé đã bị hủy (Void)
            if (ticket.Status == TicketStatus.Void)
            {
                return Json(new CheckInResponseDto
                {
                    Success = false,
                    ResultType = "VOID",
                    Message = "CẢNH BÁO: Vé này đã bị HỦY (Void) trong hệ thống! Không hợp lệ để lên xe.",
                    PassengerName = ticket.PassengerName,
                    SeatCode = ticket.SeatCode,
                    PassengerPhone = ticket.PassengerPhone,
                    TicketId = ticket.TicketId
                });
            }

            // 5. HỢP LỆ -> Cập nhật trạng thái CheckedIn và lưu thời gian
            ticket.Status = TicketStatus.CheckedIn;
            ticket.CheckedInAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var currentStats = await GetTripStatsAsync(request.TripId);

            return Json(new CheckInResponseDto
            {
                Success = true,
                ResultType = "SUCCESS",
                Message = "SOÁT VÉ THÀNH CÔNG! Chào mừng hành khách lên xe.",
                PassengerName = ticket.PassengerName,
                SeatCode = ticket.SeatCode,
                PassengerPhone = ticket.PassengerPhone,
                CheckedInAt = ticket.CheckedInAt.Value.ToLocalTime().ToString("HH:mm:ss dd/MM/yyyy"),
                TicketId = ticket.TicketId,
                Stats = currentStats
            });
        }

        // POST: /TicketInspector/Dashboard/ManualCheckIn
        // Soát vé thủ công theo TicketId hoặc SeatCode
        [HttpPost]
        public async Task<IActionResult> ManualCheckIn([FromBody] ManualCheckInRequestDto request)
        {
            if (request == null || request.TripId <= 0)
            {
                return Json(new CheckInResponseDto
                {
                    Success = false,
                    ResultType = "INVALID_REQUEST",
                    Message = "Yêu cầu không hợp lệ!"
                });
            }

            Ticket? ticket = null;
            if (request.TicketId.HasValue && request.TicketId.Value > 0)
            {
                ticket = await _context.Tickets
                    .Include(t => t.Trip).ThenInclude(tr => tr!.Route)
                    .FirstOrDefaultAsync(t => t.TicketId == request.TicketId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(request.SeatCode))
            {
                string seat = request.SeatCode.Trim().ToUpper();
                ticket = await _context.Tickets
                    .Include(t => t.Trip).ThenInclude(tr => tr!.Route)
                    .FirstOrDefaultAsync(t => t.TripId == request.TripId && t.SeatCode.ToUpper() == seat && t.Status != TicketStatus.Void);
            }

            if (ticket == null)
            {
                return Json(new CheckInResponseDto
                {
                    Success = false,
                    ResultType = "NOT_FOUND",
                    Message = "Không tìm thấy vé tương ứng!"
                });
            }

            // Kiểm tra chuyến
            if (ticket.TripId != request.TripId)
            {
                return Json(new CheckInResponseDto
                {
                    Success = false,
                    ResultType = "WRONG_TRIP",
                    Message = "Vé không thuộc chuyến xe đang chọn!",
                    PassengerName = ticket.PassengerName,
                    SeatCode = ticket.SeatCode
                });
            }

            if (ticket.Status == TicketStatus.CheckedIn)
            {
                return Json(new CheckInResponseDto
                {
                    Success = false,
                    ResultType = "ALREADY_CHECKED_IN",
                    Message = $"Vé đã soát trước đó vào lúc {ticket.CheckedInAt?.ToString("HH:mm:ss dd/MM/yyyy")}!",
                    PassengerName = ticket.PassengerName,
                    SeatCode = ticket.SeatCode,
                    CheckedInAt = ticket.CheckedInAt?.ToString("HH:mm:ss dd/MM/yyyy")
                });
            }

            ticket.Status = TicketStatus.CheckedIn;
            ticket.CheckedInAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var currentStats = await GetTripStatsAsync(request.TripId);

            return Json(new CheckInResponseDto
            {
                Success = true,
                ResultType = "SUCCESS",
                Message = "Xác nhận soát vé thủ công thành công!",
                PassengerName = ticket.PassengerName,
                SeatCode = ticket.SeatCode,
                PassengerPhone = ticket.PassengerPhone,
                CheckedInAt = ticket.CheckedInAt.Value.ToLocalTime().ToString("HH:mm:ss dd/MM/yyyy"),
                TicketId = ticket.TicketId,
                Stats = currentStats
            });
        }

        // GET: /TicketInspector/Dashboard/GetSeatMap?tripId=1
        // API lấy sơ đồ ghế và trạng thái thời gian thực
        [HttpGet]
        public async Task<IActionResult> GetSeatMap(int tripId)
        {
            var trip = await _context.Trips
                .Include(t => t.Route)
                .Include(t => t.Bus)
                    .ThenInclude(b => b!.BusType)
                .Include(t => t.Tickets)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null)
            {
                return NotFound(new { message = "Không tìm thấy chuyến xe" });
            }

            var response = BuildSeatMap(trip);
            return Json(response);
        }

        // POST: /TicketInspector/Dashboard/GenerateDemoTickets
        // Tạo vé mẫu cho chuyến để tiện kiểm thử quét QR và đổi màu ghế
        [HttpPost]
        public async Task<IActionResult> GenerateDemoTickets([FromBody] int tripId)
        {
            var trip = await _context.Trips
                .Include(t => t.Bus).ThenInclude(b => b!.BusType)
                .Include(t => t.Route).ThenInclude(r => r!.RouteStations)
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null)
            {
                return NotFound(new { message = "Không tìm thấy chuyến" });
            }

            var pickupStation = trip.Route?.RouteStations.OrderBy(rs => rs.StopOrder).FirstOrDefault()?.StationId ?? 1;
            var dropoffStation = trip.Route?.RouteStations.OrderByDescending(rs => rs.StopOrder).FirstOrDefault()?.StationId ?? 2;

            var existingSeats = await _context.Tickets
                .Where(t => t.TripId == tripId && t.Status != TicketStatus.Void)
                .Select(t => t.SeatCode)
                .ToListAsync();

            var demoPassengers = new[]
            {
                new { Seat = "A01", Name = "Nguyễn Văn An", Phone = "0901234567" },
                new { Seat = "A02", Name = "Trần Thị Mai", Phone = "0912345678" },
                new { Seat = "A03", Name = "Lê Hoàng Long", Phone = "0987654321" },
                new { Seat = "A05", Name = "Phạm Minh Đức", Phone = "0933445566" },
                new { Seat = "B01", Name = "Vũ Thu Trang", Phone = "0977889900" },
                new { Seat = "B02", Name = "Đặng Quang Huy", Phone = "0966112233" }
            };

            var user = await _context.Users.FirstOrDefaultAsync() ?? new ApplicationUser { Id = Guid.NewGuid().ToString(), FullName = "Khách Hàng Mẫu", Email = "demo@webvantai.vn" };

            int createdCount = 0;
            foreach (var p in demoPassengers)
            {
                if (!existingSeats.Contains(p.Seat))
                {
                    var booking = new Booking
                    {
                        UserId = user.Id,
                        TripId = tripId,
                        PickupStationId = pickupStation,
                        DropoffStationId = dropoffStation,
                        TotalAmount = trip.BasePrice,
                        Status = BookingStatus.Paid,
                        CreatedAt = DateTime.UtcNow,
                        ExpiredAt = DateTime.UtcNow.AddHours(24),
                        VnPayTxnRef = "DEMO_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                    };

                    _context.Bookings.Add(booking);
                    await _context.SaveChangesAsync();

                    var ticket = new Ticket
                    {
                        BookingId = booking.BookingId,
                        TripId = tripId,
                        SeatCode = p.Seat,
                        PassengerName = p.Name,
                        PassengerPhone = p.Phone,
                        QrToken = Guid.NewGuid().ToString("N"),
                        Status = TicketStatus.Valid
                    };

                    _context.Tickets.Add(ticket);
                    createdCount++;
                }
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, createdCount, message = $"Đã tạo thành công {createdCount} vé mẫu có mã QR!" });
        }

        // Helper: Thống kê soát vé của chuyến
        private async Task<TripCheckInStatsDto> GetTripStatsAsync(int tripId)
        {
            var trip = await _context.Trips
                .Include(t => t.Bus).ThenInclude(b => b!.BusType)
                .Include(t => t.Tickets)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null) return new TripCheckInStatsDto();

            int totalSeats = trip.Bus?.BusType?.TotalSeats ?? 0;
            int bookedSeats = trip.Tickets.Count(t => t.Status != TicketStatus.Void);
            int checkedInSeats = trip.Tickets.Count(t => t.Status == TicketStatus.CheckedIn);

            return new TripCheckInStatsDto
            {
                TotalSeats = totalSeats,
                BookedSeats = bookedSeats,
                CheckedInSeats = checkedInSeats
            };
        }

        // Helper: Xây dựng sơ đồ ghế theo JSON layout
        private SeatMapResponseDto BuildSeatMap(Trip trip)
        {
            var response = new SeatMapResponseDto
            {
                TripId = trip.TripId,
                RouteName = $"{trip.Route?.StartProvince ?? ""} ➔ {trip.Route?.EndProvince ?? ""}",
                DepartureTime = trip.DepartureTime.ToString("HH:mm dd/MM/yyyy"),
                LicensePlate = trip.Bus?.LicensePlate ?? "Chưa gán",
                BusTypeName = trip.Bus?.BusType?.Name ?? "Tiêu chuẩn",
                Stats = new TripCheckInStatsDto
                {
                    TotalSeats = trip.Bus?.BusType?.TotalSeats ?? 0,
                    BookedSeats = trip.Tickets.Count(t => t.Status != TicketStatus.Void),
                    CheckedInSeats = trip.Tickets.Count(t => t.Status == TicketStatus.CheckedIn)
                }
            };

            var activeTickets = trip.Tickets
                .Where(t => t.Status != TicketStatus.Void)
                .ToDictionary(t => t.SeatCode.Trim().ToUpper(), t => t);

            if (!string.IsNullOrWhiteSpace(trip.Bus?.BusType?.SeatLayoutJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trip.Bus.BusType.SeatLayoutJson);
                    if (doc.RootElement.TryGetProperty("decks", out var decksElement) && decksElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var deckJson in decksElement.EnumerateArray())
                        {
                            var deckDto = new SeatMapDeckDto
                            {
                                DeckNumber = deckJson.TryGetProperty("deckNumber", out var dn) ? dn.GetInt32() : 1,
                                DeckName = deckJson.TryGetProperty("deckName", out var dName) ? dName.GetString() ?? "Tầng" : "Tầng",
                                Rows = deckJson.TryGetProperty("rows", out var rw) ? rw.GetInt32() : 6,
                                Cols = deckJson.TryGetProperty("cols", out var cl) ? cl.GetInt32() : 3
                            };

                            if (deckJson.TryGetProperty("seats", out var seatsJson) && seatsJson.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var seatJson in seatsJson.EnumerateArray())
                                {
                                    string code = seatJson.TryGetProperty("code", out var cd) ? cd.GetString() ?? "" : "";
                                    int row = seatJson.TryGetProperty("row", out var r) ? r.GetInt32() : 1;
                                    int col = seatJson.TryGetProperty("col", out var c) ? c.GetInt32() : 1;
                                    string type = seatJson.TryGetProperty("type", out var tp) ? tp.GetString() ?? "Standard" : "Standard";

                                    var seatItem = new SeatMapItemDto
                                    {
                                        Code = code,
                                        Row = row,
                                        Col = col,
                                        Type = type,
                                        Status = "EMPTY"
                                    };

                                    if (!string.IsNullOrEmpty(code) && activeTickets.TryGetValue(code.ToUpper(), out var t))
                                    {
                                        seatItem.Status = t.Status == TicketStatus.CheckedIn ? "CHECKED_IN" : "BOOKED";
                                        seatItem.PassengerName = t.PassengerName;
                                        seatItem.PassengerPhone = t.PassengerPhone;
                                        seatItem.CheckedInAt = t.CheckedInAt?.ToLocalTime().ToString("HH:mm:ss dd/MM/yyyy");
                                        seatItem.TicketId = t.TicketId;
                                        seatItem.QrToken = t.QrToken;
                                    }

                                    deckDto.Seats.Add(seatItem);
                                }
                            }

                            response.Decks.Add(deckDto);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi parse SeatLayoutJson cho TripId {TripId}", trip.TripId);
                }
            }

            return response;
        }
    }
}
