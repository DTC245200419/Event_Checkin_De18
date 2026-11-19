using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Kho dữ liệu in-memory dùng chung cho toàn ứng dụng (Tuần 1 chưa dùng database).
builder.Services.AddSingleton<EventStore>();

var app = builder.Build();

// Endpoint kiểm tra dịch vụ còn sống hay không.
app.MapGet("/", () => Results.Ok(new { service = "Event.Api", status = "running" }));

// 1) Tra cứu danh sách sự kiện kèm số chỗ còn lại.
app.MapGet("/events", (EventStore store) =>
{
    var result = store.Events.Values
        .Select(e => store.ToEventDto(e))
        .OrderBy(e => e.Id);
    return Results.Ok(result);
});

// 2) Xem chi tiết một sự kiện.
app.MapGet("/events/{id:int}", (int id, EventStore store) =>
{
    return store.Events.TryGetValue(id, out var ev)
        ? Results.Ok(store.ToEventDto(ev))
        : Results.NotFound(new ApiError("EVENT_NOT_FOUND", "Không tìm thấy sự kiện."));
});

// 3) Đăng ký tham dự + sinh mã đăng ký.
app.MapPost("/events/{id:int}/registrations", (int id, CreateRegistrationRequest request, EventStore store) =>
{
    if (!store.Events.TryGetValue(id, out var ev))
        return Results.NotFound(new ApiError("EVENT_NOT_FOUND", "Không tìm thấy sự kiện."));

    if (string.IsNullOrWhiteSpace(request.ParticipantName))
        return Results.BadRequest(new ApiError("INVALID_REGISTRATION", "Thiếu tên người tham dự."));

    // Kiểm tra sức chứa còn lại (chỉ đếm các đăng ký đang hiệu lực).
    if (store.RemainingSlots(id) <= 0)
        return Results.Conflict(new ApiError("EVENT_FULL", "Sự kiện đã hết chỗ."));

    var registration = new Registration
    {
        Id = Guid.NewGuid(),
        EventId = id,
        ParticipantName = request.ParticipantName.Trim(),
        Email = request.Email?.Trim() ?? "",
        Code = store.GenerateCode(),
        Status = "registered",
        CheckedIn = false,
        RegisteredAt = DateTimeOffset.UtcNow
    };
    store.Registrations[registration.Id] = registration;

    // TODO (Tuần 3): publish sự kiện ParticipantRegistered lên RabbitMQ.
    return Results.Created($"/registrations/{registration.Id}", store.ToRegistrationDto(registration));
});

// 4) Hủy đăng ký.
app.MapDelete("/registrations/{id:guid}", (Guid id, EventStore store) =>
{
    if (!store.Registrations.TryGetValue(id, out var reg))
        return Results.NotFound(new ApiError("REGISTRATION_NOT_FOUND", "Không tìm thấy đăng ký."));

    if (reg.Status == "cancelled")
        return Results.BadRequest(new ApiError("ALREADY_CANCELLED", "Đăng ký đã bị hủy trước đó."));

    reg.Status = "cancelled";
    // TODO (Tuần 3): publish sự kiện ParticipantCancelled lên RabbitMQ.
    return Results.NoContent();
});

// 5) Check-in bằng đăng ký + ngăn check-in trùng lặp.
app.MapPost("/registrations/{id:guid}/checkin", (Guid id, EventStore store) =>
{
    if (!store.Registrations.TryGetValue(id, out var reg))
        return Results.NotFound(new ApiError("REGISTRATION_NOT_FOUND", "Không tìm thấy đăng ký."));

    if (reg.Status == "cancelled")
        return Results.BadRequest(new ApiError("REGISTRATION_CANCELLED", "Đăng ký đã bị hủy, không thể check-in."));

    if (reg.CheckedIn)
        return Results.Conflict(new ApiError("ALREADY_CHECKED_IN", "Người tham dự đã check-in rồi."));

    reg.CheckedIn = true;
    reg.CheckedInAt = DateTimeOffset.UtcNow;
    // TODO (Tuần 3): publish sự kiện ParticipantCheckedIn lên RabbitMQ.
    return Results.Ok(store.ToRegistrationDto(reg));
});

// 6) Tra cứu danh sách người tham dự + trạng thái check-in của một sự kiện.
app.MapGet("/events/{id:int}/registrations", (int id, EventStore store) =>
{
    if (!store.Events.ContainsKey(id))
        return Results.NotFound(new ApiError("EVENT_NOT_FOUND", "Không tìm thấy sự kiện."));

    var list = store.Registrations.Values
        .Where(r => r.EventId == id)
        .OrderBy(r => r.RegisteredAt)
        .Select(store.ToRegistrationDto);
    return Results.Ok(list);
});

app.Run();


// ====== Kho dữ liệu in-memory ======
public sealed class EventStore
{
    public ConcurrentDictionary<int, EventItem> Events { get; } = new();
    public ConcurrentDictionary<Guid, Registration> Registrations { get; } = new();

    public EventStore()
    {
        // Dữ liệu mẫu để test nhanh.
        Events[1] = new EventItem { Id = 1, Name = "Hội thảo .NET", Description = "Giới thiệu hệ phân tán", Capacity = 3 };
        Events[2] = new EventItem { Id = 2, Name = "Workshop Docker", Description = "Thực hành container", Capacity = 50 };
        Events[3] = new EventItem { Id = 3, Name = "Talkshow AI", Description = "Xu hướng AI 2026", Capacity = 100 };
    }

    // Số đăng ký đang hiệu lực (chưa hủy) của một sự kiện.
    public int ActiveRegistrations(int eventId) =>
        Registrations.Values.Count(r => r.EventId == eventId && r.Status == "registered");

    public int RemainingSlots(int eventId) =>
        Events.TryGetValue(eventId, out var ev) ? ev.Capacity - ActiveRegistrations(eventId) : 0;

    public string GenerateCode() => "REG-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public EventDto ToEventDto(EventItem e) =>
        new(e.Id, e.Name, e.Description, e.Capacity, ActiveRegistrations(e.Id), RemainingSlots(e.Id));

    public RegistrationDto ToRegistrationDto(Registration r) =>
        new(r.Id, r.EventId, r.ParticipantName, r.Email, r.Code, r.Status, r.CheckedIn, r.RegisteredAt, r.CheckedInAt);
}


// ====== Thực thể (domain) ======
public sealed class EventItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Capacity { get; set; }
}

public sealed class Registration
{
    public Guid Id { get; set; }
    public int EventId { get; set; }
    public string ParticipantName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Code { get; set; } = "";
    public string Status { get; set; } = "registered"; // registered | cancelled
    public bool CheckedIn { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
}


// ====== DTO (hợp đồng dữ liệu qua API) ======
public sealed record EventDto(int Id, string Name, string Description, int Capacity, int Registered, int Remaining);

public sealed record CreateRegistrationRequest(string ParticipantName, string? Email);

public sealed record RegistrationDto(
    Guid Id,
    int EventId,
    string ParticipantName,
    string Email,
    string Code,
    string Status,
    bool CheckedIn,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? CheckedInAt);

public sealed record ApiError(string Code, string Message);
