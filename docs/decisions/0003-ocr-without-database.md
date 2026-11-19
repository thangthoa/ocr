# ADR 0003: OCR gọi API ngay khi upload, không dùng database

- Trạng thái: Chấp nhận (thay thế phần lưu trữ và hàng đợi của ADR 0002)
- Ngày: 2026-11-18

## Bối cảnh

ADR 0002 lưu file và job vào PostgreSQL rồi để tác vụ nền gửi lên dịch vụ OCR. Thực tế `POST /v1/jobs`
trả về ngay (202), nên chờ tác vụ nền chỉ thêm độ trễ. Chủ sản phẩm yêu cầu gọi API ngay khi upload và
không lưu DB; dịch vụ OCR sẽ có API lấy danh sách job sau.

`CLAUDE.md` chọn PostgreSQL làm database chính. Quyết định này lệch khỏi điểm đó cho module OCR, theo
yêu cầu rõ ràng, nên được ghi lại ở đây.

## Quyết định

- **Upload gọi `POST /v1/jobs` ngay**, mỗi file một job. App vẫn kiểm tra đuôi, chữ ký file và giới hạn
  25 MB trước khi gửi. Dịch vụ từ chối hay không kết nối được thì báo lỗi ngay ở màn upload (không xếp
  hàng, không thử lại).
- **Dịch vụ OCR là nguồn dữ liệu duy nhất.** Xem chi tiết, ảnh trang, tải kết quả đều gọi thẳng API qua
  server (khoá API không ra trình duyệt). Trang chi tiết tự làm mới mỗi 3 giây khi job chưa xong.
- **Danh sách job**: `GET /v1/jobs?limit=&offset=&status=` (limit 1–200), trả `{total, limit, offset, jobs[]}`,
  mới nhất trước, không kèm văn bản. Nếu dịch vụ trả 404/405 (phiên bản cũ chưa có endpoint), dùng tạm
  danh sách **trong bộ nhớ** các job gửi từ instance này (tối đa `Ocr:Worker:RegistryCapacity`, mặc định 500).
- **Thông báo**: tác vụ nền chỉ hỏi trạng thái các job chưa xong trong danh sách bộ nhớ và phát toast.
- **Xoá job**: dịch vụ không có thao tác xoá; job còn `queued` thì huỷ (`DELETE /v1/jobs/{id}`), sau đó
  gỡ khỏi danh sách của app.
- Bỏ PostgreSQL, EF Core, migration, lưu file cục bộ, `xmin`, trạng thái `Pending` và cơ chế thử lại.

## Phương án đã cân nhắc

- **Giữ DB chỉ để lưu `job_id`**: danh sách bền vững qua restart, nhưng chủ sản phẩm chọn chờ API danh sách.
- **Lưu `job_id` trong localStorage trình duyệt** (như mục 7 tài liệu API): mỗi trình duyệt một danh sách,
  không có thông báo phía server.

## Hệ quả

- Thông báo (toast) chỉ có cho job gửi từ instance này kể từ khi khởi động.
- Dịch vụ không kết nối được thì không upload được.
- Danh sách lấy từ dịch vụ gồm mọi job trên dịch vụ, kể cả job không tạo từ app này (dịch vụ chưa
  phân biệt người dùng).
- Readiness (`/health/ready`) giờ kiểm tra dịch vụ OCR (`GET /v1/health`) thay cho database.
