# ADR 0007: Giao diện theo UAE Design System và luồng tài liệu Scan → Recognizing → Reviewing → Archived

- Trạng thái: Chấp nhận
- Ngày: 2026-11-20

## Bối cảnh

App đang dùng phong cách lấy cảm hứng từ IBM Carbon. Khách hàng muốn giao diện theo **UAE Design System
4.0** (<https://designsystem.gov.ae/guidelines>), và đã duyệt một mockup với các yêu cầu:

- Menu dọc: Dashboard, Scan, Review Queue, All Documents, Document Archive, rồi Settings (tách riêng).
- Header một hàng: logo, nút đổi ngôn ngữ (thay chỗ ô search), menu tài khoản.
- Upload xong thì mở ngay trang chi tiết của tài liệu.
- Trên trang chi tiết có thanh tiến trình theo bước: **Scan › Recognizing › Reviewing › Archived**.
- Trạng thái tài liệu hiện thành một thanh ngang trên Dashboard và trang All Documents.

UAE Design System được phát hành dưới dạng gói `@aegov/designsystem` 4.0.4. Gói này gồm các web component
(Stencil) chạy trên Tailwind CSS v4. App dùng Blazor và Bootstrap 5.3.

## Quyết định

1. **Giữ Bootstrap, áp token của UAE Design System bằng CSS riêng** (`wwwroot/app.css` và các file
   `*.razor.css`).
   - Màu: thang 50–950 lấy từ `dist/styles/tailwind.css` của gói, chuyển từ OKLCH sang hex để các biến
     `-rgb` của Bootstrap vẫn dùng được. AE Gold 600 ra đúng `#90702a` như tài liệu công bố.
   - Font, bo góc, kích thước nút và ô nhập theo tài liệu và CSS component của gói.
   - Không thêm Tailwind, không thêm gói `@aegov`, không dùng web component của họ.
2. **Font tự host** (app chạy on-prem có thể không có internet): Roboto và Inter cho chữ Latin, Noto Kufi
   Arabic và Alexandria cho chữ Ả Rập. Tất cả theo SIL OFL 1.1; file giấy phép nằm cạnh font trong
   `wwwroot/fonts/`. Font IBM Plex bị xoá.
3. **Gộp `Approved` vào `Archived`** trong mô hình tài liệu (`DocumentStatus` của mockup):
   - Duyệt một tài liệu là lưu trữ nó. Thông tin ai duyệt và lúc nào (`DocumentApproval`) vẫn được giữ.
   - Tài liệu đạt kiểm tra tự động thì được lưu trữ luôn, người duyệt ghi là `quality-check`.
   - Tài liệu đã lưu trữ là bản cuối: không sửa được. Muốn sửa thì bấm **Send back to review**. Thao tác
     này xoá thông tin duyệt và đưa tài liệu về Review Queue.
   - Các bước hiển thị ánh xạ từ trạng thái có sẵn:

     | Trạng thái | Bước hiển thị |
     |---|---|
     | Upload | Scan |
     | `Processing` (job Queued/Running) | Recognizing |
     | `NeedsReview` | Reviewing |
     | `Archived` | Archived |

   - `Failed` và `Cancelled` dừng ở bước Recognizing. Component Timeline của Design System không có trạng
     thái lỗi, nên bước đó giữ dạng "đang chạy", kèm lý do và một callout báo lỗi.
4. **Đổi tên và đường dẫn, giữ đường dẫn cũ**:
   - Capture → Scan (`/scan`, vẫn nhận `/capture` và `/ocr`).
   - Approved → Document Archive (`/archive`, vẫn nhận `/approved`; link tải và CSV giữ nguyên đường dẫn).
   - Documents → All Documents.
   - Review → Review Queue.
5. **Upload xong mở trang tài liệu** (`/documents/{id}`). Danh sách *Just uploaded* trên trang Scan bị bỏ,
   vì người dùng đã rời trang ngay sau khi upload. Trang tài liệu có sẵn cơ chế tự làm mới khi OCR đang
   chạy.

## Các phương án đã cân nhắc

- **Dùng chính gói `@aegov/designsystem`**: đúng chuẩn nhất, nhưng cần thêm bước build Tailwind v4 vào
  dự án Blazor và viết lại markup bằng web component. Thay đổi lớn hơn nhiều so với yêu cầu hiện tại.
- **Giữ cả `Approved` và `Archived`**: hai trạng thái sẽ cùng hiển thị là "Archived", và thao tác
  "Archive" không còn ý nghĩa trong luồng 4 bước khách hàng đưa ra.
- **Giữ danh sách *Just uploaded*** (lấy từ `CaptureBatchStore`): thêm code và một lần gọi dịch vụ OCR mà
  không phục vụ luồng mới.

## Hệ quả

- Giao diện mới chỉ là CSS và markup trong Web host. Domain, Application và module OCR không đổi.
- Tài liệu mẫu từng là *Approved* (000102, 000104, 000109, 000112) giờ là *Archived*. Trang Document
  Archive liệt kê mọi tài liệu đã lưu trữ.
- **Giấy phép UAE Design System**:
  - File `LICENSE.md` trong gói (TDRA, 2026) chỉ cho phép dùng cho website hoặc app **do Chính phủ Liên
    bang UAE đặt làm, sở hữu hoặc vận hành**. Các trường hợp khác phải xin phép TDRA trước.
  - Cũng theo file này, không được sửa code lõi hoặc tạo bản phái sinh, chỉ được tuỳ biến qua cấu hình
    chính thức.
  - App chỉ dùng giá trị token (màu, font, kích thước) để tự viết CSS. Trước khi đưa vào production cần xác
    nhận khách hàng thuộc diện được dùng, hoặc đã có giấy phép, và xác nhận cách làm này được chấp nhận.
    Nếu không, nên chuyển sang dùng chính gói chính thức (phương án đầu).
- Không dùng logo, quốc huy hay tên cơ quan của Chính phủ UAE. App giữ tên và logo riêng.
