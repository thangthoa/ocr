# ADR 0002: OCR chạy theo job nền, gọi dịch vụ OCR ngoài qua server

- Trạng thái: Thay thế một phần bởi ADR 0003 (lưu trữ, hàng đợi gửi job)
- Ngày: 2026-11-18

## Bối cảnh

Module `Ocr` gửi ảnh/PDF tới dịch vụ OCR tiếng Ả Rập bên ngoài (tài liệu "API dịch vụ OCR tiếng Ả Rập
— tài liệu cho nhóm FE"). Mỗi trang mất từ ~1,5 tới 20 phút, dịch vụ chỉ có 1 GPU và xử lý lần lượt.
API cần khoá `X-API-Key`, chạy HTTP (không TLS) trên máy thuê của bên thứ ba.

## Quyết định

- **Gọi API từ server, không từ trình duyệt.** Khoá chỉ nằm trong cấu hình server; ảnh trang và file
  kết quả đi qua endpoint của app (`/ocr/jobs/{id}/...`), không lộ `?api_key=` ra ngoài.
- **Mỗi file upload là một job riêng.** Upload chỉ lưu file + tạo job `Pending` (nhanh, không phụ thuộc
  dịch vụ). `OcrJobWorker` (BackgroundService) định kỳ:
  1. gửi job `Pending` (cũ nhất trước) bằng `POST /v1/jobs`; lỗi tạm thời (mạng, 5xx, 429) thử lại với
     backoff luỹ thừa, tối đa 5 lần; lỗi vĩnh viễn (4xx) đánh dấu `Failed` với mã lỗi của dịch vụ;
  2. hỏi `GET /v1/jobs/{id}` cho job `Queued/Running`, lưu tiến độ và các trang đã xong.
  Polling được chọn thay vì SSE vì một vòng lặp phía server đơn giản, chịu được mất kết nối và
  khởi động lại.
- **Thông báo**: Application gọi port `IOcrJobNotifier`; Web cài đặt bằng bộ phát trong bộ nhớ tới các
  circuit Blazor đang mở (toast + tự làm mới danh sách/chi tiết).
- **Xung đột ghi**: worker và người dùng cùng sửa một job; dùng `xmin` của PostgreSQL làm concurrency
  token, Infrastructure đổi lỗi EF thành `ConcurrencyConflictException`.
- **Xoá job**: job còn `Queued` thì huỷ trên dịch vụ; job đang chạy không huỷ được (giới hạn của API),
  chỉ ngừng theo dõi.
- **Luôn gọi dịch vụ thật**: `BaseUrl` + `ApiKey` bắt buộc, kiểm tra khi khởi động. Không có chế độ
  giả lập (đã bỏ theo yêu cầu); test tự động dùng HTTP handler giả nên không gọi ra ngoài.

## Phương án đã cân nhắc

- **Gọi API thẳng từ trình duyệt** (như ví dụ JS trong tài liệu): lộ khoá; tài liệu cũng nói chỉ chấp nhận cho demo.
- **Gửi ngay trong request upload**: upload 25 MB tới dịch vụ chậm, và lỗi dịch vụ làm hỏng upload.
- **Hàng đợi ngoài (Hangfire, message broker)**: CLAUDE.md không cho thêm khi chưa có nhu cầu; một instance là đủ.

## Hệ quả

- Giả định **một instance**: thông báo trong bộ nhớ và worker không phối hợp giữa nhiều instance.
  Scale ngang cần kênh thông báo chung và cơ chế khoá job (ví dụ `SELECT ... FOR UPDATE SKIP LOCKED`).
- Thông báo hiện gửi tới mọi người đang mở app (chưa có đăng nhập).
- Kết quả trên dịch vụ demo có thể mất khi máy chủ đó reset; job sẽ chuyển `Failed` (`remote_job_lost`).
- Mọi upload đều gửi tài liệu qua HTTP không mã hoá tới bên thứ ba: chỉ dùng tài liệu được phép thử.
