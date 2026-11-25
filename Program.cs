using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoServiceBot.Data;
using AutoServiceBot.Models;
using AutoServiceBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AutoServiceBot
{
    public enum UserState
    {
        None,
        WaitingForCarBrand,
        WaitingForCarModel,
        WaitingForCarYear,
        WaitingForCarPhoto,
        WaitingForAppointmentCar,
        WaitingForAppointmentService,
        WaitingForCustomService,
        WaitingForAppointmentDate,
        WaitingForClientContact,
        WaitingForAdminPin,
        WaitingForRescheduleDate
    }

    public class UserSession
    {
        public UserState State { get; set; } = UserState.None;
        public bool IsAdmin { get; set; } = false;
        public long? AdminChatId { get; set; }
        public string TempBrand { get; set; } = string.Empty;
        public string TempModel { get; set; } = string.Empty;
        public int TempYear { get; set; }
        public string TempCarInfo { get; set; } = string.Empty;
        public string TempCarPhotoId { get; set; } = string.Empty;
        public string TempService { get; set; } = string.Empty;
        public string TempDate { get; set; } = string.Empty;
        public int RescheduleAppointmentId { get; set; }
    }

    class Program
    {
        private const string AdminPin = "1234";
        private static readonly ServiceManager _serviceManager = new();
        private static readonly Dictionary<long, UserSession> _sessions = new();

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            using (var db = new AppDbContext())
            {
                db.Database.EnsureCreated();
            }

            var botClient = new TelegramBotClient("YOUR_BOT_TOKEN_HERE");

            using CancellationTokenSource cts = new();

            ReceiverOptions receiverOptions = new()
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMe();
            Console.WriteLine($"[ОК] Бот запущений як @{me.Username}! Пін-код майстра: {AdminPin}");
            Console.WriteLine("Натисніть Enter, щоб зупинити бота.");
            Console.ReadLine();

            cts.Cancel();
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is { } message)
            {
                long chatId = message.Chat.Id;
                long userId = message.From?.Id ?? chatId;

                if (!_sessions.TryGetValue(userId, out var session))
                {
                    session = new UserSession();
                    _sessions[userId] = session;
                }

                string text = message.Text?.Trim() ?? string.Empty;

                if (text == "/start")
                {
                    session.State = UserState.None;
                    await SendMainMenuAsync(botClient, chatId, "🚗 **Вітаємо у сервісі LUX-Service!**\n\nОберіть потрібну дію в меню нижче:", cancellationToken);
                    return;
                }

                if (text == "/admin")
                {
                    if (session.IsAdmin)
                    {
                        session.AdminChatId = chatId;
                        await SendAdminMenuAsync(botClient, chatId, cancellationToken);
                    }
                    else
                    {
                        session.State = UserState.WaitingForAdminPin;
                        await botClient.SendMessage(chatId, "🔐 Введіть PIN-код майстра для входу в панель сервісу:", cancellationToken: cancellationToken);
                    }
                    return;
                }

                if (session.State != UserState.None)
                {
                    await HandleUserStepAsync(botClient, chatId, userId, message, session, cancellationToken);
                    return;
                }

                await botClient.SendMessage(chatId, "⚠️ Скористайтеся кнопками меню або напишіть /start", cancellationToken: cancellationToken);
            }
            else if (update.CallbackQuery is { } callbackQuery && callbackQuery.Message is { } cbMessage)
            {
                long chatId = cbMessage.Chat.Id;
                long userId = callbackQuery.From.Id;
                string data = callbackQuery.Data ?? string.Empty;

                if (!_sessions.TryGetValue(userId, out var session))
                {
                    session = new UserSession();
                    _sessions[userId] = session;
                }

                try
                {
                    await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                }
                catch
                {

                }

                if (data == "menu_add_car")
                {
                    session.State = UserState.WaitingForCarBrand;
                    await botClient.SendMessage(chatId, "🚘 Введіть марку автомобіля (наприклад: *Skoda* або *Audi*):", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else if (data == "menu_my_cars")
                {
                    var cars = _serviceManager.GetUserCars(userId);
                    if (cars.Count == 0)
                    {
                        await botClient.SendMessage(chatId, "ℹ️ У вас ще немає доданих автомобілів.", cancellationToken: cancellationToken);
                        return;
                    }

                    foreach (var car in cars)
                    {
                        string caption = $"🚙 **{car.Brand} {car.Model}** ({car.Year} р.)";
                        var deleteBtn = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("🗑 Видалити авто", $"del_car_{car.Id}") }
                        });

                        if (!string.IsNullOrEmpty(car.PhotoFileId))
                        {
                            await botClient.SendPhoto(
                                chatId: chatId,
                                photo: new InputFileId(car.PhotoFileId),
                                caption: caption,
                                parseMode: ParseMode.Markdown,
                                replyMarkup: deleteBtn,
                                cancellationToken: cancellationToken
                            );
                        }
                        else
                        {
                            await botClient.SendMessage(
                                chatId: chatId,
                                text: caption,
                                parseMode: ParseMode.Markdown,
                                replyMarkup: deleteBtn,
                                cancellationToken: cancellationToken
                            );
                        }
                    }
                }
                else if (data.StartsWith("del_car_") && int.TryParse(data.AsSpan(8), out int carId))
                {
                    bool removed = _serviceManager.DeleteCar(carId, userId);
                    if (removed)
                    {
                        try
                        {
                            await botClient.EditMessageReplyMarkup(chatId, cbMessage.MessageId, replyMarkup: null, cancellationToken: cancellationToken);
                        }
                        catch { }
                        await botClient.SendMessage(chatId, "🗑 Автомобіль видалено зі списку.", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "⚠️ Не вдалося знайти це авто.", cancellationToken: cancellationToken);
                    }
                }
                else if (data == "menu_book")
                {
                    var cars = _serviceManager.GetUserCars(userId);
                    if (cars.Count == 0)
                    {
                        await botClient.SendMessage(chatId, "⚠️ Спочатку додайте автомобіль через **«➕ Додати авто»**!", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                        return;
                    }

                    session.State = UserState.WaitingForAppointmentCar;
                    var rows = cars.Select(c =>
                    {
                        string label = $"{c.Brand} {c.Model} ({c.Year})";
                        return new[] { InlineKeyboardButton.WithCallbackData(label, $"sel_car_{c.Id}") };
                    }).ToArray();

                    await botClient.SendMessage(chatId, "🛠 Оберіть автомобіль для запису:", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: cancellationToken);
                }
                else if (data.StartsWith("sel_car_") && int.TryParse(data.AsSpan(8), out int selectedCarId))
                {
                    var selectedCar = _serviceManager.GetCarById(selectedCarId, userId);
                    if (selectedCar == null)
                    {
                        await botClient.SendMessage(chatId, "⚠️ Авто не знайдено, спробуйте ще раз.", cancellationToken: cancellationToken);
                        return;
                    }

                    session.TempCarInfo = $"{selectedCar.Brand} {selectedCar.Model} ({selectedCar.Year})";
                    session.TempCarPhotoId = selectedCar.PhotoFileId;
                    session.State = UserState.WaitingForAppointmentService;

                    var serviceKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🛢 Заміна масла та фільтрів", "srv_oil") },
                        new[] { InlineKeyboardButton.WithCallbackData("🔍 Комп'ютерна діагностика", "srv_diag") },
                        new[] { InlineKeyboardButton.WithCallbackData("🛠 Ходова частина", "srv_susp") },
                        new[] { InlineKeyboardButton.WithCallbackData("⚙️ Двигун / КПП", "srv_eng") },
                        new[] { InlineKeyboardButton.WithCallbackData("✏️ Свій варіант", "srv_custom") }
                    });

                    await botClient.SendMessage(chatId, $"Авто: *{session.TempCarInfo}*\nОберіть послугу:", parseMode: ParseMode.Markdown, replyMarkup: serviceKeyboard, cancellationToken: cancellationToken);
                }
                else if (data.StartsWith("srv_"))
                {
                    if (data == "srv_custom")
                    {
                        session.State = UserState.WaitingForCustomService;
                        await botClient.SendMessage(chatId, "✏️ Напишіть потрібну послугу вручну:", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        session.TempService = data switch
                        {
                            "srv_oil" => "Заміна масла та фільтрів",
                            "srv_diag" => "Комп'ютерна діагностика",
                            "srv_susp" => "Ремонт ходової частини",
                            "srv_eng" => "Ремонт двигуна / КПП",
                            _ => "Обслуговування"
                        };

                        session.State = UserState.WaitingForAppointmentDate;
                        await botClient.SendMessage(chatId, "📅 Вкажіть бажану дату та час (наприклад: *25.11.2026 о 10:00* або *завтра ввечері*):", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    }
                }
                else if (data == "menu_my_bookings")
                {
                    var bookings = _serviceManager.GetUserAppointments(userId);
                    if (bookings.Count == 0)
                    {
                        await botClient.SendMessage(chatId, "ℹ️ У вас немає записів на сервіс.", cancellationToken: cancellationToken);
                        return;
                    }

                    string textOut = "📋 **Ваші заявки:**\n\n";
                    foreach (var b in bookings)
                    {
                        string statusUa = FormatStatus(b.Status);
                        textOut += $"• #{b.Id} | *{b.CarInfo}*\n  Послуга: {b.ServiceType}\n  Дата/час: {b.AppointmentDate}\n  Статус: {statusUa}\n\n";
                    }
                    await botClient.SendMessage(chatId, textOut, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else if (data == "admin_all_bookings")
                {
                    if (!session.IsAdmin) return;

                    var all = _serviceManager.GetAllAppointments();
                    if (all.Count == 0)
                    {
                        await botClient.SendMessage(chatId, "📭 Заявок у базі поки немає.", cancellationToken: cancellationToken);
                        return;
                    }

                    foreach (var b in all)
                    {
                        await SendAdminBookingCardAsync(botClient, chatId, b, cancellationToken);
                    }
                }
                else if (data == "admin_logout")
                {
                    session.IsAdmin = false;
                    session.AdminChatId = null;
                    session.State = UserState.None;
                    await SendMainMenuAsync(botClient, chatId, "Ви вийшли з панелі майстра.", cancellationToken);
                }
                else if (data.StartsWith("adm_conf_") && int.TryParse(data.AsSpan(9), out int confId))
                {
                    if (!session.IsAdmin) return;
                    var updated = _serviceManager.UpdateAppointmentStatus(confId, AppointmentStatus.Confirmed);
                    if (updated != null)
                    {
                        await botClient.SendMessage(chatId, $"✅ Заявку #{updated.Id} підтверджено.", cancellationToken: cancellationToken);
                        await botClient.SendMessage(updated.ChatId, $"✅ Ваш запис #{updated.Id} (*{updated.CarInfo}*, {updated.AppointmentDate}) **підтверджено** майстром!", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    }
                }
                else if (data.StartsWith("adm_resch_") && int.TryParse(data.AsSpan(10), out int reschId))
                {
                    if (!session.IsAdmin) return;
                    session.RescheduleAppointmentId = reschId;
                    session.State = UserState.WaitingForRescheduleDate;
                    await botClient.SendMessage(chatId, $"🕒 Введіть новий запропонований час для заявки #{reschId} (наприклад: *післязавтра о 13:00*):", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else if (data.StartsWith("adm_canc_") && int.TryParse(data.AsSpan(9), out int cancId))
                {
                    if (!session.IsAdmin) return;
                    var updated = _serviceManager.UpdateAppointmentStatus(cancId, AppointmentStatus.Cancelled);
                    if (updated != null)
                    {
                        await botClient.SendMessage(chatId, $"❌ Заявку #{updated.Id} скасовано.", cancellationToken: cancellationToken);
                        await botClient.SendMessage(updated.ChatId, $"❌ Ваш запис #{updated.Id} (*{updated.CarInfo}*, {updated.AppointmentDate}) **скасовано** сервісом.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    }
                }
            }
        }

        private static async Task HandleUserStepAsync(ITelegramBotClient botClient, long chatId, long userId, Message message, UserSession session, CancellationToken cancellationToken)
        {
            string text = message.Text?.Trim() ?? string.Empty;

            if (session.State == UserState.WaitingForAdminPin)
            {
                if (text == AdminPin)
                {
                    session.IsAdmin = true;
                    session.AdminChatId = chatId;
                    session.State = UserState.None;
                    await SendAdminMenuAsync(botClient, chatId, cancellationToken);
                }
                else
                {
                    session.State = UserState.None;
                    await botClient.SendMessage(chatId, "❌ Невірний PIN-код.", cancellationToken: cancellationToken);
                }
                return;
            }

            if (session.State == UserState.WaitingForRescheduleDate)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    await botClient.SendMessage(chatId, "⚠️ Вкажіть новий час:", cancellationToken: cancellationToken);
                    return;
                }

                var updated = _serviceManager.RescheduleAppointment(session.RescheduleAppointmentId, text);
                session.State = UserState.None;
                session.RescheduleAppointmentId = 0;

                if (updated != null)
                {
                    await botClient.SendMessage(chatId, $"🕒 Заявку #{updated.Id} перенесено на: *{updated.AppointmentDate}*.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    await botClient.SendMessage(updated.ChatId, $"🕒 Майстер запропонував **новий час** для заявки #{updated.Id} (*{updated.CarInfo}*): **{updated.AppointmentDate}**.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "⚠️ Заявку не знайдено.", cancellationToken: cancellationToken);
                }
                return;
            }

            if (session.State == UserState.WaitingForCarBrand)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    await botClient.SendMessage(chatId, "⚠️ Введіть назву марки:", cancellationToken: cancellationToken);
                    return;
                }
                session.TempBrand = text;
                session.State = UserState.WaitingForCarModel;
                await botClient.SendMessage(chatId, "🚙 Введіть модель автомобіля:", cancellationToken: cancellationToken);
            }
            else if (session.State == UserState.WaitingForCarModel)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    await botClient.SendMessage(chatId, "⚠️ Введіть модель авто:", cancellationToken: cancellationToken);
                    return;
                }
                session.TempModel = text;
                session.State = UserState.WaitingForCarYear;
                await botClient.SendMessage(chatId, "📅 Введіть рік випуску (наприклад: *2008*):", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            }
            else if (session.State == UserState.WaitingForCarYear)
            {
                if (int.TryParse(text, out int year) && year >= 1900 && year <= DateTime.Now.Year + 1)
                {
                    session.TempYear = year;
                    session.State = UserState.WaitingForCarPhoto;
                    await botClient.SendMessage(chatId, "📷 Надішліть фото авто або напишіть `пропустити`:", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "⚠️ Введіть коректний рік випуску числом:", cancellationToken: cancellationToken);
                }
            }
            else if (session.State == UserState.WaitingForCarPhoto)
            {
                string photoId = string.Empty;
                if (message.Photo is { Length: > 0 })
                {
                    photoId = message.Photo.Last().FileId;
                }
                else if (!text.Equals("пропустити", StringComparison.OrdinalIgnoreCase))
                {
                    await botClient.SendMessage(chatId, "⚠️ Надішліть фото або напишіть `пропустити`.", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    return;
                }

                _serviceManager.AddCar(userId, session.TempBrand, session.TempModel, session.TempYear, photoId);
                session.State = UserState.None;
                await SendMainMenuAsync(botClient, chatId, $"✅ Авто **{session.TempBrand} {session.TempModel} ({session.TempYear})** додано!", cancellationToken);
            }
            else if (session.State == UserState.WaitingForCustomService)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    await botClient.SendMessage(chatId, "⚠️ Напишіть назву послуги:", cancellationToken: cancellationToken);
                    return;
                }
                session.TempService = text;
                session.State = UserState.WaitingForAppointmentDate;
                await botClient.SendMessage(chatId, "📅 Вкажіть бажану дату та час:", cancellationToken: cancellationToken);
            }
            else if (session.State == UserState.WaitingForAppointmentDate)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    await botClient.SendMessage(chatId, "⚠️ Вкажіть дату та час:", cancellationToken: cancellationToken);
                    return;
                }
                session.TempDate = text;
                session.State = UserState.WaitingForClientContact;

                var contactKeyboard = new ReplyKeyboardMarkup(new[]
                {
                    new[] { KeyboardButton.WithRequestContact("📱 Поділитися номером") }
                })
                {
                    ResizeKeyboard = true,
                    OneTimeKeyboard = true
                };

                await botClient.SendMessage(
                    chatId,
                    "📞 Натисніть **«📱 Поділитися номером»** або напишіть ім'я та телефон вручну:",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: contactKeyboard,
                    cancellationToken: cancellationToken
                );
            }
            else if (session.State == UserState.WaitingForClientContact)
            {
                string contactInfo;
                if (message.Contact is { } contact)
                {
                    string fullName = $"{contact.FirstName} {contact.LastName}".Trim();
                    contactInfo = string.IsNullOrEmpty(fullName) ? contact.PhoneNumber : $"{fullName} ({contact.PhoneNumber})";
                }
                else if (!string.IsNullOrWhiteSpace(text))
                {
                    contactInfo = text;
                }
                else
                {
                    await botClient.SendMessage(chatId, "⚠️ Натисніть кнопку або введіть ім'я та номер телефону:", cancellationToken: cancellationToken);
                    return;
                }

                await botClient.SendMessage(
                    chatId,
                    "⏳ Оформлюємо заявку...",
                    replyMarkup: new ReplyKeyboardRemove(),
                    cancellationToken: cancellationToken
                );

                var created = _serviceManager.CreateAppointment(userId, chatId, contactInfo, session.TempCarInfo, session.TempCarPhotoId, session.TempService, session.TempDate);
                session.State = UserState.None;

                await SendMainMenuAsync(
                    botClient,
                    chatId,
                    $"🎉 **Заявку #{created.Id} створено!**\nАвто: {created.CarInfo}\nПослуга: {created.ServiceType}\nДата/час: {created.AppointmentDate}\nКонтакт: {created.ClientContact}",
                    cancellationToken
                );

                await NotifyAdminsAsync(botClient, created, cancellationToken);
            }
        }

        private static async Task NotifyAdminsAsync(ITelegramBotClient botClient, Appointment app, CancellationToken cancellationToken)
        {
            foreach (var s in _sessions.Values)
            {
                if (s.IsAdmin && s.AdminChatId.HasValue)
                {
                    await botClient.SendMessage(s.AdminChatId.Value, "🔔 **Нова заявка на сервіс!**", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    await SendAdminBookingCardAsync(botClient, s.AdminChatId.Value, app, cancellationToken);
                }
            }
        }

        private static async Task SendAdminBookingCardAsync(ITelegramBotClient botClient, long chatId, Appointment b, CancellationToken cancellationToken)
        {
            string card = $"🔧 **Заявка #{b.Id}** ({FormatStatus(b.Status)})\n" +
                          $"🚗 Авто: {b.CarInfo}\n" +
                          $"⚙️ Послуга: {b.ServiceType}\n" +
                          $"📅 Час: {b.AppointmentDate}\n" +
                          $"📞 Клієнт: {b.ClientContact}";

            var actions = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Підтвердити", $"adm_conf_{b.Id}"),
                    InlineKeyboardButton.WithCallbackData("🕒 Перенести", $"adm_resch_{b.Id}"),
                    InlineKeyboardButton.WithCallbackData("❌ Скасувати", $"adm_canc_{b.Id}")
                }
            });

            if (!string.IsNullOrEmpty(b.CarPhotoFileId))
            {
                await botClient.SendPhoto(
                    chatId: chatId,
                    photo: new InputFileId(b.CarPhotoFileId),
                    caption: card,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: actions,
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                await botClient.SendMessage(
                    chatId: chatId,
                    text: card,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: actions,
                    cancellationToken: cancellationToken
                );
            }
        }

        private static string FormatStatus(AppointmentStatus status) => status switch
        {
            AppointmentStatus.Confirmed => "✅ Підтверджено",
            AppointmentStatus.Rescheduled => "🕒 Запропоновано інший час",
            AppointmentStatus.Cancelled => "❌ Скасовано",
            _ => "⏳ Очікує підтвердження"
        };

        private static async Task SendMainMenuAsync(ITelegramBotClient botClient, long chatId, string text, CancellationToken cancellationToken)
        {
            var menu = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚘 Мої авто", "menu_my_cars"),
                    InlineKeyboardButton.WithCallbackData("➕ Додати авто", "menu_add_car")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🛠 Записатись", "menu_book"),
                    InlineKeyboardButton.WithCallbackData("📋 Мої записи", "menu_my_bookings")
                }
            });

            await botClient.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: menu, cancellationToken: cancellationToken);
        }

        private static async Task SendAdminMenuAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var adminMenu = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("📥 Усі заявки клієнтів", "admin_all_bookings") },
                new[] { InlineKeyboardButton.WithCallbackData("🚪 Вийти з адмінки", "admin_logout") }
            });

            await botClient.SendMessage(chatId, "🔧 **Панель майстра СТО**", parseMode: ParseMode.Markdown, replyMarkup: adminMenu, cancellationToken: cancellationToken);
        }

        private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[ПОМИЛКА]: {exception.Message}");
            return Task.CompletedTask;
        }
    }
}