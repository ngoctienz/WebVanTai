using System;
using System.Collections.Generic;
using WebVanTai.Models.Enums;

namespace WebVanTai.Areas.TicketInspector.ViewModels
{
    public class CheckInRequestDto
    {
        public int TripId { get; set; }
        public string QrToken { get; set; } = string.Empty;
    }

    public class ManualCheckInRequestDto
    {
        public int TripId { get; set; }
        public int? TicketId { get; set; }
        public string? SeatCode { get; set; }
    }

    public class TripCheckInStatsDto
    {
        public int TotalSeats { get; set; }
        public int BookedSeats { get; set; }
        public int CheckedInSeats { get; set; }
        public int RemainingSeats => TotalSeats - BookedSeats;
        public int UncheckedBookedSeats => BookedSeats - CheckedInSeats;
        public double CheckInPercentage => BookedSeats > 0 ? Math.Round((double)CheckedInSeats / BookedSeats * 100, 1) : 0;
        public double OccupancyPercentage => TotalSeats > 0 ? Math.Round((double)BookedSeats / TotalSeats * 100, 1) : 0;
    }

    public class CheckInResponseDto
    {
        public bool Success { get; set; }
        public string ResultType { get; set; } = string.Empty; // SUCCESS, ALREADY_CHECKED_IN, WRONG_TRIP, NOT_FOUND, VOID
        public string Message { get; set; } = string.Empty;
        public string PassengerName { get; set; } = string.Empty;
        public string SeatCode { get; set; } = string.Empty;
        public string PassengerPhone { get; set; } = string.Empty;
        public string? CheckedInAt { get; set; }
        public int TicketId { get; set; }
        public TripCheckInStatsDto Stats { get; set; } = new();
    }

    public class SeatMapItemDto
    {
        public string Code { get; set; } = string.Empty;
        public int Row { get; set; }
        public int Col { get; set; }
        public string Type { get; set; } = "Standard";
        public string Status { get; set; } = "EMPTY"; // EMPTY, BOOKED, CHECKED_IN, VOID
        public string? PassengerName { get; set; }
        public string? PassengerPhone { get; set; }
        public string? CheckedInAt { get; set; }
        public int? TicketId { get; set; }
        public string? QrToken { get; set; }
    }

    public class SeatMapDeckDto
    {
        public int DeckNumber { get; set; }
        public string DeckName { get; set; } = string.Empty;
        public int Rows { get; set; }
        public int Cols { get; set; }
        public List<SeatMapItemDto> Seats { get; set; } = new();
    }

    public class SeatMapResponseDto
    {
        public int TripId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public string DepartureTime { get; set; } = string.Empty;
        public string LicensePlate { get; set; } = string.Empty;
        public string BusTypeName { get; set; } = string.Empty;
        public TripCheckInStatsDto Stats { get; set; } = new();
        public List<SeatMapDeckDto> Decks { get; set; } = new();
    }

    public class TripSummaryItem
    {
        public int TripId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public string StartProvince { get; set; } = string.Empty;
        public string EndProvince { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public string LicensePlate { get; set; } = string.Empty;
        public string BusTypeName { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public int TotalSeats { get; set; }
        public int BookedSeats { get; set; }
        public int CheckedInSeats { get; set; }
        public TripStatus Status { get; set; }
    }

    public class PassengerTicketDto
    {
        public int TicketId { get; set; }
        public int BookingId { get; set; }
        public string SeatCode { get; set; } = string.Empty;
        public string PassengerName { get; set; } = string.Empty;
        public string PassengerPhone { get; set; } = string.Empty;
        public TicketStatus Status { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public string? QrToken { get; set; }
    }

    public class TicketInspectorDashboardViewModel
    {
        public DateTime SelectedDate { get; set; } = DateTime.Today;
        public string FilterMode { get; set; } = "today"; // today, tomorrow, all
        public List<TripSummaryItem> Trips { get; set; } = new();
        public int? SelectedTripId { get; set; }
        public TripSummaryItem? SelectedTrip { get; set; }
        public SeatMapResponseDto? SeatMap { get; set; }
        public List<PassengerTicketDto> Tickets { get; set; } = new();
    }
}
