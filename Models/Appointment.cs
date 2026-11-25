namespace AutoServiceBot.Models
{
    public enum AppointmentStatus
    {
        Pending,
        Confirmed,
        Rescheduled,
        Cancelled
    }

    public class Appointment
    {
        public int Id { get; set; }
        public long TelegramId { get; set; }
        public long ChatId { get; set; }
        public string ClientContact { get; set; } = string.Empty;
        public string CarInfo { get; set; } = string.Empty;
        public string CarPhotoFileId { get; set; } = string.Empty;
        public string ServiceType { get; set; } = string.Empty;
        public string AppointmentDate { get; set; } = string.Empty;
        public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    }
}