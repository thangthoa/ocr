# Màn hình mockup và giao diện Carbon

Tên hiển thị của ứng dụng là **OCR System** (`AppName` trong `SharedText*.resx`). Tên trong code
(`OppOcr.Web`, `OppOcr.slnx`…) giữ nguyên.

## Màn hình

| Route | Màn hình | Dữ liệu |
|---|---|---|
| `/login` | Đăng nhập (layout riêng, không có menu) | Giả: `admin` / `admin` |
| `/`, `/dashboard` | Báo cáo theo Ngày / Tuần / Tháng | Số liệu mẫu cố định, ngày theo hôm nay |
| `/capture` (và `/ocr` cũ) | Chọn loại tài liệu (mặc định *Printed / typed*), **One** (một ảnh hoặc PDF) / **Multi** (nhiều ảnh, hoặc cả folder ảnh) và tên file (bắt buộc), xem / sắp xếp trang rồi Upload; bên dưới là danh sách *Just uploaded*: chỉ các file vừa upload từ trang này (kể từ khi mở trang), cập nhật trạng thái / tiến độ trực tiếp | **Thật**: mỗi lần upload là một job trên dịch vụ OCR; nhiều ảnh được ghép thành một PDF nhiều trang (ADR 0005) |
| `/review` | Hàng đợi duyệt tay (vòng 2): document *Needs review* kèm lý do kiểm tra tự động đưa vào; document thật trước, rồi cũ nhất trước | Job thật + mẫu |
| `/approved` | Tài liệu đã duyệt: người duyệt, thời điểm duyệt, lọc / tìm, tải bản cuối (DOCX, TXT), xuất CSV | Job thật + mẫu |
| `/documents` | Danh sách document: job OCR thật + 14 tài liệu mẫu (nhãn *Sample*) | Job thật + phần lưu trong bộ nhớ |
| `/documents/{id}` | Preview: ảnh gốc cạnh trang Word (từng trang); bên dưới là metadata và lịch sử phiên bản; duyệt, sửa (lưu thành phiên bản mới) | Job thật + phần lưu trong bộ nhớ |
| `/admin` (`?tab=Roles\|Retention\|Quality\|Integrations\|Audit`) | Vai trò, phân loại & lưu giữ, cài đặt kiểm tra chất lượng tự động, hệ thống kết nối, nhật ký hoạt động | Vai trò và lưu giữ: mẫu. Cài đặt kiểm tra: dùng thật, trong bộ nhớ. Dịch vụ OCR: health check thật. Nhật ký: ghi thật, trong bộ nhớ |
| `/ocr/jobs`, `/ocr/jobs/{id}` | Không còn trang riêng: chuyển hướng sang `/documents`, `/documents/{id}` | — |

## Document = job OCR

Mỗi file upload là một job OCR và được hiển thị như một document. Trạng thái document:

| Job OCR | Document |
|---|---|
| Queued, Running | Processing (kèm tiến độ, trang chi tiết tự làm mới mỗi 3 giây) |
| Failed | Failed (kèm lỗi đã dịch) |
| Cancelled | Cancelled |
| Done | Kiểm tra tự động: đạt → Approved (tự duyệt); có vấn đề → Needs review → Approved (người duyệt). Sau đó → Archived (người dùng chuyển) |

Từ dịch vụ OCR: tên file, loại (in / viết tay), trạng thái, tiến độ, ảnh và text từng trang, cảnh báo,
file tải về (DOCX/TXT/MD/JSON, là kết quả OCR gốc, không gồm các lần sửa), xoá/huỷ job.

App lưu thêm **trong bộ nhớ** (`Features/Mockups/Data/DocumentStore.cs`): trạng thái duyệt, metadata, text
đã sửa theo từng trang và các phiên bản. Phiên bản 1.0 là text OCR, được tạo khi mở document đã OCR xong lần đầu.
Document upload từ Capture được gắn người upload và mã lần upload (hiện ở metadata và nhật ký); phòng ban không còn
được chọn lúc upload, nhập ở metadata của document.

**Nhật ký hoạt động** (`AuditLog`) ghi: đăng nhập (thành công/thất bại, không ghi mật khẩu), upload, sửa, khôi
phục phiên bản, đổi trạng thái, xoá, tải bản cuối, xuất CSV, kết quả kiểm tra tự động (người dùng `quality-check`)
và đổi cài đặt kiểm tra. Chỉ giữ 500 dòng gần nhất trong bộ nhớ; nhật ký thật (append-only, đẩy sang SIEM) cần database.

## Kiểm tra chất lượng tự động (duyệt vòng 1) và confidence

Code ở `Features/Mockups/Quality/`. Khi job OCR xong, app so kết quả với các quy tắc dưới đây. Document không có vấn
đề gì thì được **tự duyệt** (*Approved*, người duyệt `quality-check`). Document có vấn đề thì vào hàng đợi `/review`
kèm danh sách lý do.

| Quy tắc | Mặc định | Ghi chú |
|---|---|---|
| Confidence của document < ngưỡng | 85 % | Confidence document = trung bình các trang có text |
| Confidence của một trang < ngưỡng | 75 % | Một trang dưới ngưỡng là đủ để đưa vào review |
| Trang có cảnh báo của dịch vụ OCR | Bật | Cảnh báo lấy từ `result.pages[].warnings` |
| Trang không có text, hoặc thiếu trong kết quả | Bật | |
| Thiếu metadata bắt buộc (số hiệu, ngày, phòng ban, loại, từ khoá) | Không chọn trường nào | Job mới chỉ có tiêu đề, nên chọn trường nào thì **mọi** document mới đều vào review |
| Không có text nào | Luôn bật | Document trắng không bao giờ được tự duyệt |
| Document đạt: tự duyệt / vẫn đưa vào review | Tự duyệt | |

- **Cài đặt** ở `/admin?tab=Quality`: lưu trong bộ nhớ, về mặc định khi restart, có ghi nhật ký. Cài đặt mới
  chỉ áp dụng cho document kiểm tra sau đó. Kết quả cũ giữ nguyên, kèm ngưỡng đã dùng lúc kiểm tra.
- **Khi nào kiểm tra**: mỗi document chỉ kiểm tra một lần, lúc app đọc được job đã xong *kèm text* (tạo phiên bản 1.0).
  Job gửi từ instance này được kiểm tra ngay khi worker thấy job xong (`QualityCheckTrigger` nghe thông báo job),
  nên document đã được tự duyệt hoặc đã có lý do review trước khi có người mở. Job tạo từ nơi khác (danh sách job
  của dịch vụ không kèm text) được kiểm tra khi có người mở lần đầu. Trước đó hàng đợi ghi *Not checked yet*.
- **Trang document**: khung *Automatic quality check* (đạt / số vấn đề, thời điểm, ngưỡng). Mỗi vấn đề có số
  trang là link nhảy tới trang đó. Nút số trang có viền đỏ khi trang có vấn đề. Dưới preview có confidence của trang đang xem.
- **Hàng đợi `/review`**: thêm số document bị kiểm tra tự động gắn cờ và số document được tự duyệt, cùng cột
  *Why it is here* (lý do gộp theo loại). Confidence tô đỏ theo ngưỡng lúc kiểm tra, không còn hằng số 80 %.
- **Confidence là số giả lập** (`SimulatedConfidence`), vì dịch vụ OCR chưa trả confidence. Mỗi trang có một giá
  trị cố định tính từ job id và số trang (SHA-256), không đổi sau khi restart. Trang in thường nằm trong khoảng
  72–99 %, trang viết tay 60–95 %. Mỗi cảnh báo trừ 15 điểm, trang không có text thì không có confidence. UI gắn
  nhãn *Simulated*. Tài liệu mẫu chia confidence có sẵn ra từng trang, giữ nguyên trung bình. Khi dịch vụ trả
  confidence thật: thêm trường vào `OcrPage` (module Ocr), dùng nó trong `DocumentStore.CheckQuality` thay cho
  `SimulatedConfidence.ForJob`, rồi bỏ nhãn *Simulated*.
- Quy tắc kiểm tra là quy tắc nghiệp vụ. Hiện chúng nằm trong Web host như phần mockup còn lại. Khi có module
  Documents, `QualityCheck` và `QualitySettings` chuyển vào Domain/Application của module đó, cài đặt lưu PostgreSQL.

## Tài liệu đã duyệt

- Bấm **Approve** thì app lưu *người duyệt* và *thời điểm duyệt* (hiện ở metadata trang document và trang
  `/approved`). **Archive** giữ nguyên thông tin này; **Send back to review** thì xoá.
- Trang `/approved` chỉ liệt kê document *Approved*, document thật trước, rồi mới duyệt nhất. Lọc theo phòng
  ban, loại, mức mật, khoảng ngày duyệt (ngày theo giờ server) và tìm như trang Documents.
- **Bản cuối** (`/documents/{id}/final?format=docx|txt`, chỉ cho document *Approved* / *Archived*):
  - DOCX là file Word mới nhất nếu nó chứa đúng text hiện tại (lưu từ editor Word, hoặc DOCX OCR chưa sửa);
    nếu text đã được sửa sau đó (nút Edit, khôi phục phiên bản) thì app tạo DOCX từ text hiện tại.
  - TXT là text hiện tại (UTF-8 có BOM), các trang ngăn bằng ký tự form feed.
- **Xuất CSV** (`/approved/export.csv?…`, cùng bộ lọc đang chọn): UTF-8 có BOM để Excel đọc được tiếng Ả Rập,
  tiêu đề cột theo ngôn ngữ đang chọn, thời gian theo UTC (ISO 8601). Ô bắt đầu bằng `= + - @` được thêm
  dấu `'` để Excel không chạy như công thức. Khi không lấy được danh sách job OCR thì không cho xuất (trả 503),
  để file không thiếu document mà người dùng không biết.

## Sửa Word trực tiếp (OnlyOffice Docs)

Xem ADR 0004. Trên trang document (đang *Needs review* hoặc *Approved*), nút **Edit in Word** mở `/documents/{id}/word`
với editor OnlyOffice nhúng trong trang. Phiên bản Word đầu tiên là DOCX của dịch vụ OCR (hoặc tạo từ text với tài
liệu mẫu / khi dịch vụ OCR tắt). Bấm **Save** (Ctrl+S) hoặc đóng editor thì app lưu phiên bản Word mới, cập nhật text
và ghi nhật ký. Các phiên bản Word xem và tải ở tab Versions (mục *Word file*) và dòng *Word file, version n*.

- Biến cấu hình: `DOCS_EDITOR_PUBLIC_URL`, `DOCS_EDITOR_INTERNAL_URL`, `DOCS_EDITOR_APP_URL`, `DOCS_EDITOR_JWT_SECRET`,
  `DOCS_STORAGE_PATH` (hoặc `Documents:Editor:*`, `Documents:Storage:Path`). Thiếu thì nút Edit in Word ẩn đi.
- Sửa text bằng nút **Edit** không đổi file Word; sửa trong Word thì cập nhật text. Nên coi Word là bản chính.
- **Preview** trên trang document gồm 2 cột: *Original scan* và *Word file*. Cột Word là ảnh từng trang của file Word
  mới nhất, do OnlyOffice render qua Conversion API và lưu cạnh file. Nút *Word / Text* đổi sang xem text. Bấm
  vào ảnh để mở kích thước đầy đủ. Khi bấm **Edit**, cột phải thành ô sửa text, còn ảnh gốc vẫn hiện để đối chiếu.
  Không có document server thì cột phải luôn là text.
- Chạy local có editor: thêm `DOCS_EDITOR_JWT_SECRET` vào `.env` rồi
  `docker-compose -f devops/local/docker-compose.yml --profile word up -d --build`.
- Extension chặn quảng cáo/tracker (uBlock, AdBlock, AdGuard, Brave Shields…) chặn file `Analytics.js` của
  OnlyOffice. Khi file này bị chặn, editor dừng ở khung xám mà không báo lỗi. Sau 45 giây editor chưa báo sẵn sàng
  (`onAppReady`), trang hiện cảnh báo hướng dẫn tắt chặn cho trang (extension cho phép theo trang đang mở, không theo
địa chỉ editor) và nút *Try again*. Editor khởi động muộn thì cảnh báo tự biến mất.

## Khung giao diện

- Góc phải header chỉ có **avatar**; bấm vào mở menu tài khoản (tên, vai trò demo, chọn **ngôn ngữ**, **Sign out**).
  Menu là thẻ `<details>` nên chạy được cả khi chưa có kết nối Blazor; `wwwroot/js/account-menu.js` chỉ thêm việc
  đóng menu khi bấm ra ngoài hoặc nhấn Esc. Trang đăng nhập vẫn có chọn ngôn ngữ trên header.
- **Xoá** luôn hỏi lại bằng hộp thoại cảnh báo (`Components/Shared/ConfirmButton.razor`): focus vào *Cancel*,
  Esc hoặc bấm ra ngoài để đóng, chỉ nút *Delete* màu đỏ mới xoá.

## Giới hạn cần biết

- **Đăng nhập là giả.** Trang `/login` chỉ chuyển sang Dashboard khi nhập `admin` / `admin`. Không có
  cookie hay phiên đăng nhập, và **mọi trang vẫn mở cho tất cả**. Khi làm module Identity sẽ thay bằng
  ASP.NET Core Identity và policy phân quyền phía server.
- **Dữ liệu duyệt, metadata, phiên bản mất khi restart hoặc deploy lại**, dùng chung cho mọi người và chỉ đúng
  khi chạy một instance (app không có database, ADR 0003). Muốn giữ lâu dài cần module Documents có PostgreSQL.
- Danh sách Documents lấy **100 job mới nhất** rồi lọc trong bộ nhớ; tìm theo nội dung text chỉ áp dụng cho
  document đã mở (đã có text trong app) và tài liệu mẫu, vì danh sách job của dịch vụ không kèm text.
- Khi dịch vụ OCR không kết nối được, Documents báo lỗi nhưng vẫn hiện tài liệu mẫu; Capture báo lỗi ngay khi bấm Upload.
- Trang Approved và file CSV cũng chỉ gồm các job trong danh sách job mới nhất mà trang Documents lấy về. Link tải
  bản cuối và xuất CSV, như mọi trang khác, **chưa có phân quyền** (đăng nhập còn là giả).
- **Vai trò và lịch lưu giữ ở trang Quản trị là dữ liệu mẫu**: không có phân quyền thật, không có tự động
  huỷ hay legal hold. Các hệ thống AD, SharePoint, CMS, hệ thống nội bộ, SIEM chỉ ghi *Planned*.
- **Confidence của job thật là giả lập** (xem mục kiểm tra tự động). Không có overlay vùng confidence thấp trên
  ảnh vì dịch vụ OCR không trả toạ độ chữ, nên mức chi tiết nhỏ nhất là từng trang.
- Kiểm tra tự động chỉ chạy một lần. Chưa có nút kiểm tra lại sau khi sửa metadata, và chưa có yêu cầu OCR lại / scan
  lại (vòng 2). Job lỗi toàn bộ (*Failed*) vẫn chỉ hiện ở Documents, không vào hàng đợi review.
- **Multi** chỉ ghép được ảnh JPG/PNG, tối đa 20 ảnh và 25 MB tổng (giới hạn của dịch vụ OCR); trang sắp theo
  tên file và đổi được bằng nút mũi tên. Chọn file lần nữa sẽ thay danh sách, không cộng thêm.
- **Chọn folder** (chỉ ở Multi) dùng thuộc tính `webkitdirectory` của trình duyệt: các ảnh trong folder thành
  các trang theo thứ tự tên file; file khác (PDF, text…) bị bỏ qua, có thông báo số file bỏ qua.

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
