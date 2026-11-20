# ADR 0006: Gửi file cho dịch vụ OCR bằng link, nhận kết quả qua callback

- Trạng thái: Chấp nhận (bổ sung ADR 0002 và 0003: cách gửi file và cách biết job đã xong)
- Ngày: 2026-11-20

## Bối cảnh

Bên AI cập nhật dịch vụ OCR (tài liệu "Gửi file bằng link + nhận kết quả qua callback — đặc tả cho FE",
chạy từ 20/11/2026). Gửi file thẳng từ mạng văn phòng sang máy chủ OCR chỉ đạt ~5–7 KB/s (ảnh 0,5 MB mất
khoảng 100 giây). Máy chủ OCR tự tải file từ `ocr.dc1.merket.io` đạt ~88 KB/s (khoảng 6 giây).

- `POST /v1/jobs` nhận thêm:
  - `file_url` (thay cho `file`). Máy chủ OCR tải file **ngay trong request đó** rồi mới trả 202. Tải lỗi
    thì trả 422 `file_download_failed`.
  - `callback_url`. Khi job `done` hoặc `failed`, máy chủ OCR POST đúng đối tượng Job tới địa chỉ này,
    ký `X-OCR-Signature = sha256=<hex HMAC-SHA256(secret, "<X-OCR-Timestamp>.<body thô>")>`. Không nhận
    được 2xx trong 10 giây thì gửi lại sau 10 giây, 1 phút, 5 phút, 30 phút, 2 giờ.
- Chỉ cho phép host `ocr.dc1.merket.io` và chỉ https, cho cả `file_url` lẫn `callback_url`.
- Cách cũ (gửi file trực tiếp, tự hỏi `GET /v1/jobs/{id}`) vẫn chạy song song.
- `DELETE /v1/jobs/{id}?purge=true` xoá file gốc, ảnh trang và file Word trên máy chủ OCR. Không xoá thì
  máy chủ tự xoá sau 7 ngày.

## Quyết định

- **Gửi file bằng link khi có `OCR_APP_URL`** (địa chỉ https máy chủ OCR dùng để gọi app, ví dụ
  `https://ocr.dc1.merket.io`). Adapter `OcrApiClient` giữ file trong bộ nhớ dưới một token ngẫu nhiên
  256 bit, rồi gửi `file_url = {OCR_APP_URL}/api/ocr-files/{token}/{tên file}`. Tên file ở cuối link chỉ để
  máy chủ OCR biết tên và loại file; token mới là thứ tìm ra file.
  - Link chỉ sống trong lúc request `POST /v1/jobs` chạy, vì máy chủ OCR tải file trước khi trả lời. Request
    xong (thành công hay lỗi) thì file bị xoá khỏi bộ nhớ. Hạn tối đa 15 phút
    (`Ocr:Api:FileLinkLifetimeMinutes`) phòng trường hợp không xoá được.
  - Không ghi file ra đĩa, không cần database. Giữ nguyên ADR 0003.
  - Use case upload không đổi. Gửi bằng link hay gửi thẳng là việc của adapter (port `IOcrApiClient`).
- **Không có `OCR_APP_URL` thì gửi file thẳng như trước.** Dùng cho máy dev, vì máy chủ OCR không gọi được
  `localhost`.
- **Callback khi có cả `OCR_APP_URL` và `OCR_CALLBACK_SECRET`.** App gửi kèm
  `callback_url = {OCR_APP_URL}/api/ocr-callback`. Endpoint này:
  - đọc body thô (tối đa 4 MB), kiểm tra chữ ký bằng so sánh thời gian cố định, và bỏ request có
    `X-OCR-Timestamp` lệch quá 5 phút. Sai thì trả 401, body không phải Job thì trả 400;
  - lưu job theo `job_id` vào danh sách trong bộ nhớ (`IOcrJobRegistry`). Nhận trùng thì ghi đè, không
    báo lại. Job của app nhưng gửi trước khi app khởi động lại thì được thêm lại vào danh sách;
  - **trả 200 trước, rồi mới thông báo** (`HttpResponse.OnCompleted`): toast, kiểm tra chất lượng, các
    trang đang mở tải lại job. Những việc này gọi lại dịch vụ OCR nên có thể quá 10 giây.
- Việc kiểm tra chữ ký và đọc định dạng Job nằm ở Infrastructure (`OcrCallbackReader`, port
  `IOcrCallbackReader`). Việc lưu và thông báo nằm ở Application (`ReceiveOcrJobCallbackHandler`). Web chỉ
  map hai endpoint (`OcrServiceEndpoints`), không đụng tới Infrastructure.
- **Vẫn giữ polling** (`OcrJobWorker`, 3 giây/lần cho job chưa xong). Polling cho thấy tiến độ từng trang
  (callback chỉ báo lúc kết thúc), và lấy bù khi lỡ callback (mục 4 của đặc tả). Job đã xong qua callback
  thì không còn bị hỏi nữa.
- **Chưa dùng `purge`.** App vẫn lấy ảnh trang, file DOCX và nội dung job trực tiếp từ dịch vụ OCR
  (ADR 0003), nên xoá trên dịch vụ sẽ làm hỏng trang xem chi tiết và trình sửa Word (phiên bản 1).

## Phương án đã cân nhắc

- **Lưu file ra đĩa/volume rồi phát link**: sống được qua restart, nhưng máy chủ OCR tải ngay trong request
  nên không cần. Lưu file đã upload ra đĩa cũng làm lệch ADR 0003.
- **Link có chữ ký HMAC + hạn dùng (như link OnlyOffice, ADR 0004)**: cũng an toàn, nhưng token ngẫu nhiên
  chỉ tồn tại vài giây là đủ và đơn giản hơn. Dùng `file_auth` (header Authorization) thì phụ thuộc vào
  cách máy chủ OCR chuyển header khi có redirect, không có lợi gì thêm.
- **Tự động gửi thẳng khi `file_download_failed`**: giấu lỗi cấu hình, và gửi thẳng file lớn qua đường chậm
  cũng sẽ quá thời gian chờ. Hiện chỉ báo lỗi cho người dùng và ghi log.
- **Thông báo ngay trong request callback**: đơn giản hơn, nhưng các trang đang mở gọi lại dịch vụ OCR, dễ
  vượt 10 giây và khiến máy chủ OCR gửi lại.
- **Bỏ polling, chỉ dùng callback**: mất tiến độ từng trang trên màn upload và mất cơ chế lấy bù.

## Hệ quả

- Phải cấu hình trên server mà `ocr.dc1.merket.io` trỏ tới: `OCR_APP_URL=https://ocr.dc1.merket.io`, và
  `OCR_CALLBACK_SECRET` (bên AI gửi riêng, không qua chat/email chung). Reverse proxy phải chuyển
  `/api/ocr-files/*` và `/api/ocr-callback` vào app. Hai đường này cho máy chủ OCR gọi từ Internet.
- Hai endpoint này không có đăng nhập. Link file được bảo vệ bằng token ngẫu nhiên và chỉ sống vài giây;
  callback được bảo vệ bằng chữ ký. App vẫn chưa có rate limiting (xem CLAUDE.md §9).
- Lúc upload, bộ nhớ giữ thêm một bản file (tối đa 25 MB) trong thời gian máy chủ OCR tải về.
- Với 88 KB/s, file 25 MB mất khoảng 5 phút để máy chủ OCR tải, sát với `Ocr:Api:RequestTimeoutSeconds`
  (300). Cần tăng giá trị này nếu thường upload file lớn.
- `file_name` của job do máy chủ OCR tự đặt (đặc tả không có trường tên file). App để tên ở cuối link (đã
  mã hoá URL) và trong `Content-Disposition`. Cần kiểm tra lần chạy đầu trên server thật rằng tên tiếng
  Việt/Ả Rập hiện đúng.
- **Dịch vụ OCR tự xoá file của job sau 7 ngày.** Vì app chưa lưu ảnh trang, DOCX và nội dung OCR (ADR 0003),
  tài liệu cũ hơn 7 ngày sẽ không còn ảnh trang và không mở được DOCX gốc. Muốn giữ lâu hơn (và dùng được
  `purge`) thì app phải tự lưu kết quả, tức là cần module có PostgreSQL và nơi lưu file. Việc này cần một
  ADR riêng.
- Thông báo vẫn chỉ trong một instance (ADR 0002).
