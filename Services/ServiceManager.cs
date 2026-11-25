using System.Collections.Generic;
using System.Linq;
using AutoServiceBot.Data;
using AutoServiceBot.Models;

namespace AutoServiceBot.Services
{
    public class ServiceManager
    {
        public void AddCar(long telegramId, string brand, string model, int year, string photoFileId)
        {
            using var db = new AppDbContext();
            db.Cars.Add(new Car
            {
                TelegramId = telegramId,
                Brand = brand,
                Model = model,
                Year = year,
                PhotoFileId = photoFileId
            });
            db.SaveChanges();
        }

        public List<Car> GetUserCars(long telegramId)
        {
            using var db = new AppDbContext();
            return db.Cars.Where(c => c.TelegramId == telegramId).ToList();
        }

        public Car? GetCarById(int carId, long telegramId)
        {
            using var db = new AppDbContext();
            return db.Cars.FirstOrDefault(c => c.Id == carId && c.TelegramId == telegramId);
        }

        public bool DeleteCar(int carId, long telegramId)
        {
            using var db = new AppDbContext();
            var car = db.Cars.FirstOrDefault(c => c.Id == carId && c.TelegramId == telegramId);
            if (car == null) return false;

            db.Cars.Remove(car);
            db.SaveChanges();
            return true;
        }

        public Appointment CreateAppointment(long telegramId, long chatId, string clientContact, string carInfo, string carPhotoFileId, string serviceType, string date)
        {
            using var db = new AppDbContext();
            var app = new Appointment
            {
                TelegramId = telegramId,
                ChatId = chatId,
                ClientContact = clientContact,
                CarInfo = carInfo,
                CarPhotoFileId = carPhotoFileId,
                ServiceType = serviceType,
                AppointmentDate = date,
                Status = AppointmentStatus.Pending
            };
            db.Appointments.Add(app);
            db.SaveChanges();
            return app;
        }

        public List<Appointment> GetUserAppointments(long telegramId)
        {
            using var db = new AppDbContext();
            return db.Appointments.Where(a => a.TelegramId == telegramId).OrderByDescending(a => a.Id).ToList();
        }

        public List<Appointment> GetAllAppointments()
        {
            using var db = new AppDbContext();
            return db.Appointments.OrderByDescending(a => a.Id).ToList();
        }

        public Appointment? UpdateAppointmentStatus(int appointmentId, AppointmentStatus newStatus)
        {
            using var db = new AppDbContext();
            var app = db.Appointments.FirstOrDefault(a => a.Id == appointmentId);
            if (app == null) return null;

            app.Status = newStatus;
            db.SaveChanges();
            return app;
        }

        public Appointment? RescheduleAppointment(int appointmentId, string newDate)
        {
            using var db = new AppDbContext();
            var app = db.Appointments.FirstOrDefault(a => a.Id == appointmentId);
            if (app == null) return null;

            app.AppointmentDate = newDate;
            app.Status = AppointmentStatus.Rescheduled;
            db.SaveChanges();
            return app;
        }
    }
}