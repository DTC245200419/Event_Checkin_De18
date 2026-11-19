# Đề 18 — Hệ thống đăng ký sự kiện và check-in

Đồ án môn **Phát triển ứng dụng phân tán** (.NET 10 / ASP.NET Core).
Hệ thống quản lý đăng ký và check-in tại sự kiện, tập trung vào luồng sự kiện **pub/sub**.

## Kiến trúc mục tiêu

```
Participant Client → Event API → Database
                        │
                        ▼
                 RabbitMQ Topic Exchange
                   ┌────┴────┐
                   ▼         ▼
          Dashboard Service  Audit Worker
                   │
                 SignalR
                   ▼
            Dashboard Client
```

## Lộ trình 5 tuần

| Tuần | Nội dung | Trạng thái |
|------|----------|-----------|
| 1 | Event API (REST) — 6 endpoint, dữ liệu in-memory, chạy bằng Docker | ✅ Đang làm |
| 2 | Cơ sở dữ liệu (EF Core) + logic nghiệp vụ | ⬜ |
| 3 | RabbitMQ pub/sub + 2 subscriber (Dashboard, Audit) | ⬜ |
| 4 | SignalR realtime dashboard | ⬜ |
| 5 | Docker Compose + xử lý lỗi + báo cáo | ⬜ |

## API (Tuần 1)

| Method | Route | Chức năng |
|--------|-------|-----------|
| GET | `/events` | Danh sách sự kiện + số chỗ còn lại |
| GET | `/events/{id}` | Chi tiết một sự kiện |
| POST | `/events/{id}/registrations` | Đăng ký + sinh mã |
| DELETE | `/registrations/{id}` | Hủy đăng ký |
| POST | `/registrations/{id}/checkin` | Check-in (chặn trùng) |
| GET | `/events/{id}/registrations` | Danh sách người tham dự + trạng thái check-in |

## Chạy bằng Docker

```bash
cd src/Event.Api
docker build -t event-api:de18 .
docker run --rm -p 5018:8080 event-api:de18
```

Mở http://localhost:5018/events để kiểm tra.
