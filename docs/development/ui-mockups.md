# Màn hình mockup và giao diện Carbon

Tên hiển thị của ứng dụng là **OCR System** (`AppName` trong `SharedText*.resx`). Tên trong code
(`OppOcr.Web`, `OppOcr.slnx`…) giữ nguyên.

## Màn hình

| Route | Màn hình | Dữ liệu |
|---|---|---|
| `/login` | Đăng nhập (layout riêng, không có menu) | Giả: `admin` / `admin` |
| `/`, `/dashboard` | Báo cáo theo Ngày / Tuần / Tháng | Số liệu mẫu cố định, ngày theo hôm nay |
| `/capture` (và `/ocr` cũ) | Phiếu batch (mã tự sinh, barcode thùng, phòng ban, số file dự kiến), upload nhiều file hoặc cả folder, đối soát sau khi upload; bên dưới là 10 file scan gần nhất kèm batch | **Thật**: mỗi file là một job trên dịch vụ OCR; batch lưu trong bộ nhớ |
| `/review` | Hàng đợi document *Needs review*: document thật trước, rồi cũ nhất trước | Job thật + mẫu |
| `/documents` | Danh sách document: job OCR thật + 14 tài liệu mẫu (nhãn *Sample*) | Job thật + phần lưu trong bộ nhớ |
| `/documents/{id}` | Xem trước, duyệt, sửa (lưu thành phiên bản mới), lịch sử phiên bản, metadata | Job thật + phần lưu trong bộ nhớ |
| `/admin` (`?tab=Roles\|Retention\|Integrations\|Audit`) | Vai trò, phân loại & lưu giữ, hệ thống kết nối, nhật ký hoạt động | Vai trò và lưu giữ: mẫu. Dịch vụ OCR: health check thật. Nhật ký: ghi thật, trong bộ nhớ |
| `/ocr/jobs`, `/ocr/jobs/{id}` | Không còn trang riêng: chuyển hướng sang `/documents`, `/documents/{id}` | — |

## Document = job OCR

Mỗi file upload là một job OCR và được hiển thị như một document. Trạng thái document:

| Job OCR | Document |
|---|---|
| Queued, Running | Processing (kèm tiến độ, trang chi tiết tự làm mới mỗi 3 giây) |
| Failed | Failed (kèm lỗi đã dịch) |
| Cancelled | Cancelled |
| Done | Needs review → Approved → Archived (người dùng chuyển) |

Từ dịch vụ OCR: tên file, loại (in / viết tay), trạng thái, tiến độ, ảnh và text từng trang, cảnh báo,
file tải về (DOCX/TXT/MD/JSON, là kết quả OCR gốc, không gồm các lần sửa), xoá/huỷ job.

App lưu thêm **trong bộ nhớ** (`Features/Mockups/Data/DocumentStore.cs`): trạng thái duyệt, metadata, text
đã sửa theo từng trang và các phiên bản. Phiên bản 1.0 là text OCR, được tạo khi mở document đã OCR xong lần đầu.
File upload kèm phiếu batch được gắn mã batch, người upload và phòng ban (làm metadata mặc định).

**Nhật ký hoạt động** (`AuditLog`) ghi: đăng nhập (thành công/thất bại, không ghi mật khẩu), upload, sửa, khôi
phục phiên bản, đổi trạng thái, xoá. Chỉ giữ 500 dòng gần nhất trong bộ nhớ; nhật ký thật (append-only, đẩy
sang SIEM) cần database.

## Giới hạn cần biết

- **Đăng nhập là giả.** Trang `/login` chỉ chuyển sang Dashboard khi nhập `admin` / `admin`. Không có
  cookie hay phiên đăng nhập, và **mọi trang vẫn mở cho tất cả**. Khi làm module Identity sẽ thay bằng
  ASP.NET Core Identity và policy phân quyền phía server.
- **Dữ liệu duyệt, metadata, phiên bản mất khi restart hoặc deploy lại**, dùng chung cho mọi người và chỉ đúng
  khi chạy một instance (app không có database, ADR 0003). Muốn giữ lâu dài cần module Documents có PostgreSQL.
- Danh sách Documents lấy **100 job mới nhất** rồi lọc trong bộ nhớ; tìm theo nội dung text chỉ áp dụng cho
  document đã mở (đã có text trong app) và tài liệu mẫu, vì danh sách job của dịch vụ không kèm text.
- Khi dịch vụ OCR không kết nối được, Documents báo lỗi nhưng vẫn hiện tài liệu mẫu; Capture báo lỗi ở phần lịch sử.
- **Vai trò và lịch lưu giữ ở trang Quản trị là dữ liệu mẫu**: không có phân quyền thật, không có tự động
  huỷ hay legal hold. Các hệ thống AD, SharePoint, CMS, hệ thống nội bộ, SIEM chỉ ghi *Planned*.
- Hàng đợi Review không có overlay vùng confidence thấp vì dịch vụ OCR không trả confidence; cột confidence chỉ
  có giá trị với tài liệu mẫu.
- **Upload folder** dùng thuộc tính `webkitdirectory` của trình duyệt. File không đúng định dạng trong folder bị
  bỏ qua (có thông báo số file bỏ qua). Giới hạn 20 file mỗi lần vẫn giữ như upload thường. Phải chọn loại tài
  liệu (in / viết tay) trước thì ô upload và nút chọn folder mới bật.

## Gỡ hoặc thay mockup

Phần mockup nằm trong `src/Hosts/Web/Features/Mockups/` (trang, dữ liệu mẫu, `DocumentStore`, `DocumentCatalog`,
chuỗi `MockupText*.resx`) cùng một dòng `builder.Services.AddMockups()` trong `Program.cs`. Khi có module
Documents thật (Domain, Application, Infrastructure riêng), thay `DocumentStore`/`DocumentCatalog` bằng các use
case của module đó; các trang Razor giữ bố cục, chỉ đổi nguồn dữ liệu.

## Giao diện Carbon

Toàn bộ app dùng phong cách lấy cảm hứng từ IBM Carbon, áp lên Bootstrap 5.3 bằng cách ghi đè biến CSS trong
`wwwroot/app.css`. Không thêm thư viện component nào.

- Màu chủ đạo `#0f62fe`, chữ `#161616`, nền phụ `#f4f4f4`, góc vuông (0px). Màu trạng thái dùng bảng màu tag
  của Carbon (độ tương phản đạt AA) và luôn đi kèm chữ, không dùng màu đơn lẻ để phân biệt.
- Font IBM Plex Sans và IBM Plex Sans Arabic (SIL Open Font License, xem `wwwroot/fonts/ibm-plex/OFL-*.txt`)
  được **lưu trong repo**, vì app chạy on-prem có thể không có internet. Chữ Latin có `letter-spacing: .16px`
  theo Carbon; chữ Ả Rập để mặc định để không làm hỏng nét nối.
- Đây là dự án cho khách hàng: màu chủ đạo là tạm thời, cần thay bằng màu thương hiệu của khách
  (biến `--app-primary` và các biến `--bs-primary*` trong `app.css`).

## Docker

`OppOcr.Web.csproj` đặt `RequiresAspNetWebAssets=true`. Không có cờ này, bản publish trong Docker thiếu
`_framework/blazor.web.js` (SDK chỉ tải package chứa file đó khi thấy file `.razor` lúc restore, mà Dockerfile
restore từ `.csproj` trước khi copy code), và không trang nào tương tác được.

## Xem nhanh

```bash
dotnet run --project src/Hosts/Web --launch-profile http
```

Mở `/login` và đăng nhập `admin` / `admin`. Đổi ngôn ngữ bằng English / العربية ở góc header.
