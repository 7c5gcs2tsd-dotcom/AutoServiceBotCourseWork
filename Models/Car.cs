namespace AutoServiceBot.Models
{
    public class Car
    {
        public int Id { get; set; }
        public long TelegramId { get; set; }
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Year { get; set; }
        public string PhotoFileId { get; set; } = string.Empty;
    }
}