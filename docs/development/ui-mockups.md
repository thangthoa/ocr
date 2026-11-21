# Màn hình mockup và giao diện (UAE Design System)

Tên hiển thị của ứng dụng là **OCR System** (`AppName` trong `SharedText*.resx`). Tên trong code
(`OppOcr.Web`, `OppOcr.slnx`…) giữ nguyên.

## Màn hình

| Route | Màn hình | Dữ liệu |
|---|---|---|
| `/login` | Đăng nhập 2 bước (layout riêng, không có menu): mật khẩu rồi mã OTP. Mọi trang khác chuyển về đây khi chưa đăng nhập | Giả: `admin` / `admin`, mã `123456`; phiên lưu trong localStorage |
| `/`, `/dashboard` | **Dashboard**: báo cáo theo Ngày / Tuần / Tháng. Trên cùng là **tiến độ dự án** so với kế hoạch, rồi trạng thái document là một thanh ngang (mỗi ô chú thích mở All Documents đã lọc theo trạng thái đó), rồi các **thẻ KPI** so với mục tiêu đặt ở Settings | Cộng từ mọi document: lịch sử giả lập + job thật (ADR 0008); mục tiêu KPI lấy từ Settings |
| `/scan` (và `/capture`, `/ocr` cũ) | **Scan**: một cột giữa trang; nhập tên (bắt buộc) trước, rồi chọn loại tài liệu (mặc định *Printed / typed*), **One** (một ảnh hoặc PDF) / **Multi** (nhiều ảnh, hoặc cả folder ảnh), xem / sắp xếp trang rồi Upload. Trên cùng là thanh tiến trình đang ở bước **Scan** (khi đang gửi file thì bước này hiện "Sending to the OCR service…"). Upload xong thì **mở trang của document**, thanh tiến trình chạy tiếp từ Recognizing | **Thật**: mỗi lần upload là một job trên dịch vụ OCR; nhiều ảnh được ghép thành một PDF nhiều trang (ADR 0005) |
| `/review` | **Review Queue**: hàng đợi duyệt tay (vòng 2), document *Reviewing* kèm lý do kiểm tra tự động đưa vào (không có lý do thì hiện —) và thời điểm cập nhật cuối; cũ nhất trước. Lọc và tìm như All Documents (thứ tự hàng đợi giữ nguyên) | Lịch sử giả lập + job thật |
| `/documents` | **All Documents**: các job OCR thật; thanh trạng thái ngang ở đầu trang là bộ lọc theo trạng thái; cột Version và Last updated tách riêng | Lịch sử giả lập + job thật + phần lưu trong bộ nhớ |
| `/archive` (và `/approved` cũ) | **Document Archive**: document đã duyệt (= đã lưu trữ), người duyệt, thời điểm duyệt, lọc / tìm, mỗi dòng một dropdown tải bản cuối (DOCX, PDF, TXT) | Lịch sử giả lập + job thật |
| `/documents/{id}` | Trang document: trên cùng là thanh tiến trình **Scan › Recognizing › Reviewing › Archived** (dính khi cuộn), cùng hàng là các nút **Approve and archive** (hoặc *Send back to review*), **Edit** (mở Word) và **Delete**; preview ảnh gốc cạnh trang Word (từng trang), bên phải số trang là ô chọn **DOCX / PDF / TXT** và nút **Download** (bản hiện tại; bản cuối khi đã lưu trữ); bên dưới là metadata (nút *Edit details* để sửa, lưu thành phiên bản mới) và lịch sử phiên bản | Job thật + phần lưu trong bộ nhớ |
| `/reports` (`?report=…&from=yyyy-MM-dd&to=yyyy-MM-dd`) | **Reports**: chọn một trong 6 báo cáo và khoảng ngày, xem dạng bảng, xuất CSV, in | Cùng số liệu với Dashboard; báo cáo hoạt động người dùng đọc nhật ký thật |
| `/users` | **Users › Users**: danh sách người dùng, thêm / sửa | Dùng thật, trong bộ nhớ; tài khoản ban đầu là mẫu |
| `/users/roles` | **Users › Roles & access**: ma trận vai trò × quyền | Dùng thật, trong bộ nhớ |
| `/users/activity` (`?user=&category=&result=&from=&to=&q=`) | **Users › Activity logs**: nhật ký hoạt động, kiểm tra toàn vẹn, lọc, xuất CSV | 30 ngày sinh từ lịch sử giả lập + nhật ký thật của app |
| `/admin` (`?tab=Retention\|Quality\|Kpi\|Integrations`) | **Settings** (mục cuối của menu): phân loại & lưu giữ, cài đặt kiểm tra chất lượng tự động, mục tiêu KPI, hệ thống kết nối. Link cũ `?tab=Users\|Roles\|Audit` chuyển sang trang mới | Lưu giữ: mẫu. Cài đặt kiểm tra, mục tiêu KPI: dùng thật, trong bộ nhớ. Dịch vụ OCR: health check thật |
| `/ocr/jobs`, `/ocr/jobs/{id}` | Không còn trang riêng: chuyển hướng sang `/documents`, `/documents/{id}` | — |

## Document = job OCR

Mỗi file upload là một job OCR và được hiển thị như một document. Luồng của một document là
**Scan › Recognizing › Reviewing › Archived** (ADR 0007). Trạng thái document:

| Job OCR | Document (nhãn trên giao diện) | Bước trên thanh tiến trình |
|---|---|---|
| — | — | Scan (luôn xong khi đã upload) |
| Queued, Running | `Processing` (*Recognizing*), kèm tiến độ; trang chi tiết tự làm mới mỗi 3 giây | Recognizing |
| Failed | `Failed`, kèm lỗi đã dịch | Dừng ở Recognizing, có lý do |
| Cancelled | `Cancelled` | Dừng ở Recognizing |
| Done | Kiểm tra tự động: đạt → `Archived` (tự duyệt); có vấn đề → `NeedsReview` (*Reviewing*) → `Archived` (người duyệt bấm **Approve and archive**) | Reviewing → Archived |

Duyệt là lưu trữ: không còn trạng thái *Approved* riêng. Document đã lưu trữ là bản cuối, không sửa được; nút
**Send back to review** đưa nó về Review Queue (xoá thông tin duyệt) để sửa rồi duyệt lại.

Từ dịch vụ OCR: tên file, loại (in / viết tay), trạng thái, tiến độ, ảnh và text từng trang, cảnh báo,
file tải về (DOCX/TXT/MD/JSON, là kết quả OCR gốc, không gồm các lần sửa), xoá/huỷ job.

App lưu thêm **trong bộ nhớ** (`Features/Mockups/Data/DocumentStore.cs`): trạng thái duyệt, metadata, text
đã sửa theo từng trang và các phiên bản. Phiên bản 1.0 là text OCR, được tạo khi mở document đã OCR xong lần đầu.
Document upload từ Scan được gắn người upload và mã lần upload (hiện ở metadata và nhật ký); phòng ban không còn
được chọn lúc upload, nhập ở metadata của document.

**Nhật ký hoạt động** (`AuditLog`) ghi những gì làm trong app:
- đăng nhập thành công / thất bại (không ghi mật khẩu), sai mã OTP;
- upload, sửa, khôi phục phiên bản, đổi trạng thái, xoá, tải bản cuối, xuất CSV;
- kết quả kiểm tra tự động (người dùng `quality-check`);
- đổi cài đặt kiểm tra, mục tiêu KPI, người dùng, quyền; xuất báo cáo và xuất nhật ký.

Chỉ giữ 500 dòng gần nhất trong bộ nhớ; nhật ký thật (append-only, đẩy sang SIEM) cần database. Trang xem nhật ký:
*Users › Activity logs* (xem mục bên dưới).

## Kiểm tra chất lượng tự động (duyệt vòng 1) và confidence

Code ở `Features/Mockups/Quality/`. Khi job OCR xong, app so kết quả với các quy tắc dưới đây. Document không có vấn
đề gì thì được **tự duyệt và lưu trữ** (*Archived*, người duyệt `quality-check`). Document có vấn đề thì vào hàng đợi `/review`
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

## Lịch sử dự án giả lập (ADR 0008)

Code ở `Features/Mockups/History/`. Mockup giả lập một dự án **đã xử lý 1.093.911 document** trong 45 ngày trước hôm nay
(`HistoryProfile.Documents`). File upload trong app được cộng thêm, nên tổng chỉ tăng khi có upload. Mọi màn hình đọc
cùng một nguồn: Dashboard, KPI, Reports, các danh sách, Settings › Retention, khung quyền trong Users và Activity logs.

- **Dây chuyền**: chạy mỗi ngày từ 07:00 đến 23:00, hai ca, 12 người scan (capture01–12). capture03 bị khoá 40 ngày trước
  nên chỉ scan trong 5 ngày đầu. Mấy ngày đầu chạy chậm hơn (đang lên tốc độ), sau đó khoảng 25.000 document mỗi ngày.
- **Document**: trung bình khoảng 1,1 trang, nên tổng khoảng 1,22 triệu trang trên kế hoạch 2.000.000 trang. Mỗi document
  có phòng ban, loại, mức mật, confidence theo phòng ban, số hiệu (`PV-2019-04213`…) không trùng, tiêu đề, từ khoá và text
  tiếng Ả Rập.
- **Kiểm tra tự động** dùng ngưỡng mặc định. Khoảng 3,7 % document vào review; 0,2 % lỗi OCR (*Failed*).
- **Duyệt**: 6 người duyệt (review01–06) làm ca 07:30–16:30, mỗi người một tốc độ, mỗi lần lấy document cũ nhất mà họ
  được xem (theo phòng ban và mức mật của tài khoản). Khoảng 60 % document được sửa text trong Word (phiên bản 1.1) trước
  khi duyệt. Những gì còn chờ ở cuối ngày hôm qua là hàng đợi review hiện tại (khoảng 370 document).
- **Hôm nay không có gì giả lập**: chế độ *Ngày* trên Dashboard chỉ có file upload hôm nay. Ngày của lịch sử trượt theo
  hôm nay; qua nửa đêm, cả lịch sử lùi một ngày.
- Document lịch sử có id `DOC-0000001`…`DOC-1093911`, nhãn *Sample* và cách xử lý như tài liệu mẫu: mở, duyệt, sửa đều
  được. Không có ảnh scan gốc. Khi bị thay đổi, `DocumentStore` giữ bản sao (mất khi restart). Tìm kiếm chỉ theo metadata,
  không theo nội dung.
- Lịch sử chỉ giữ khoảng 30 MB dữ liệu gọn trong bộ nhớ, dựng ở nền khi khởi động (khoảng 1 giây). Mỗi dòng chỉ được dựng
  đầy đủ khi hiển thị; các danh sách phân trang phía server.
- Tài liệu mẫu (khi bật) thay chỗ 14 document lịch sử, tổng vẫn là 1.093.911. Tắt lịch sử bằng
  `Documents__History__Enabled=false`; test component tắt sẵn, trừ các test về lịch sử và về số liệu.

## Mục tiêu KPI và Dashboard

Code ở `Features/Mockups/Kpi/`. Cài đặt ở `/admin?tab=Kpi`, lưu trong bộ nhớ (về mặc định khi restart), dùng chung cho mọi
người, mỗi lần lưu ghi một dòng nhật ký.

- **Dự án số hoá**: tổng số trang, ngày bắt đầu, ngày kết thúc. Mặc định 2.000.000 trang trong 100 ngày (20.000 trang/ngày,
  mục tiêu vận hành của hồ sơ thầu), bắt đầu 45 ngày trước hôm nay, trùng ngày đầu của lịch sử giả lập. Thẻ này cũng ghi
  số document từ ngày bắt đầu (1.093.911 cộng file upload).
  Dashboard so số trang đã scan từ ngày bắt đầu với kế hoạch chia đều theo ngày: đạt hoặc vượt kế hoạch là *On track*,
  chậm dưới 5 % là *At risk*, chậm hơn là *Off track*. Thẻ này không đổi theo nút Ngày / Tuần / Tháng.
- **KPI**: danh sách cố định, người dùng chỉ đặt **mục tiêu**, **mức cảnh báo** và có hiện trên Dashboard hay không.

  | KPI | Hướng | Mục tiêu / cảnh báo mặc định | Số đo trên Dashboard |
  |---|---|---|---|
  | Pages scanned per day | ≥ | 20.000 / 18.000 | Trung bình mỗi ngày của kỳ |
  | OCR accuracy | ≥ | 90 % / 88 % | Confidence trung bình (tender yêu cầu ≥ 90 %) |
  | Archived without manual review | ≥ | 80 % / 70 % | Tỉ lệ tự duyệt |
  | Review backlog | ≤ | 300 / 500 | Số document đang chờ duyệt (không theo kỳ) |
  | Time in review | ≤ | 24 / 48 giờ | Thời gian chờ duyệt trung bình |
  | OCR failure rate | ≤ | 1 % / 2 % | Tỉ lệ job lỗi |
  | Pages per operator per day | ≥ | 1.800 / 1.500 | Trang mỗi ngày chia cho số người đã upload trong kỳ |

  Trạng thái: đạt mục tiêu là *On track*; chưa đạt nhưng chưa vượt mức cảnh báo là *At risk*; vượt mức cảnh báo là
  *Off track*. Với KPI "≥", mức cảnh báo phải không cao hơn mục tiêu; với KPI "≤", không thấp hơn.
- Đường mục tiêu trên biểu đồ Dashboard là mục tiêu *Pages scanned per day* (chia đều cho các giờ khi xem theo Ngày),
  không còn là hằng số 26.000. Ba ô số liệu cũ (Pages scanned, Average OCR confidence, Waiting for review) được thay bằng
  các thẻ KPI. Nút *Start reviewing* và link số job lỗi nằm trong thẻ tương ứng.
- Số đo cộng từ mọi document (`ProjectFacts`, `ProjectReports`): kỳ *Ngày* là hôm nay, *Tuần* là 7 ngày, *Tháng* là 30 ngày,
  đều tính tới hôm nay. Kỳ không có gì để đo (vd. chưa có document nào được kiểm tra hôm nay) hiện `—` và *No data yet*.
  Mức so với kỳ trước chỉ hiện khi kỳ trước nằm trọn trong dự án. Dashboard tự tải lại khi có job OCR thay đổi. Khi có
  dữ liệu thật, các quy tắc trong `KpiRules` chuyển vào module báo cáo (Domain/Application), cài đặt lưu PostgreSQL.

## Báo cáo (Reports)

Code ở `Features/Mockups/Reports/`. Trang `/reports` liệt kê 6 báo cáo. Người dùng chọn báo cáo, khoảng ngày (hoặc nút
nhanh: Today, Last 7 days, Last 30 days, This month), bấm **View report**. Mặc định là *Digitization progress* của 7 ngày gần
nhất. Khoảng ngày không quá 366 ngày, không có ngày sau hôm nay.

| Báo cáo | Dòng | Cột chính |
|---|---|---|
| Digitization progress | Mỗi ngày | Trang, document, mục tiêu ngày (KPI), % mục tiêu, luỹ kế dự án, kế hoạch luỹ kế |
| OCR quality | Mỗi phòng ban | Document, trang, confidence, % trang dưới ngưỡng, % tự duyệt, % vào review, % lỗi |
| Operator productivity | Mỗi người đã scan hoặc duyệt trong kỳ (vai trò lấy từ Users) | Document và trang đã scan, trang/giờ (ca 8 giờ), document đã duyệt, phút/lần duyệt |
| Review and exceptions | Mỗi lý do chính | Số document, đã xử lý, còn mở, số giờ xử lý trung bình |
| Archive statistics | Mỗi phòng ban | Document và trang đã lưu trữ, theo mức mật, dung lượng (≈ 0,42 MB/trang) |
| User activity | Mỗi người dùng | Đăng nhập, upload, sửa, đổi trạng thái, xoá, tải/xuất, đổi cài đặt. **Dữ liệu thật** từ nhật ký |

- Mỗi báo cáo có dòng tổng. Năm báo cáo đầu cộng cùng số liệu với Dashboard (`ProjectFacts`), nên khớp nhau và khớp
  Dashboard: tổng 7 ngày của báo cáo tiến độ bằng Dashboard *Week*; tổng document ở báo cáo chất lượng và năng suất bằng
  báo cáo tiến độ; *còn mở* ở báo cáo ngoại lệ bằng hàng đợi review cộng document lỗi. Document chưa có phòng ban (file
  vừa upload) nằm ở dòng *Department not set*. Phút/lần duyệt chỉ có với lần duyệt giả lập (thời gian làm việc của lần duyệt
  thật không đo được).
- **Export CSV**: `/reports/export.csv?report=…&from=…&to=…`. UTF-8 có BOM, tiêu đề cột theo ngôn ngữ đang chọn, số
  không định dạng và ngày ISO để Excel đọc là số. Mỗi lần xuất ghi một dòng nhật ký. Khoảng ngày sai trả 400.
- **Print**: dùng lệnh in của trình duyệt; CSS in (`@media print` trong `app.css`) bỏ header, menu, danh sách báo cáo và các
  nút, chỉ còn tiêu đề, kỳ báo cáo và bảng.
- Link tới một báo cáo cụ thể: `reports?report=Quality&from=2026-11-01&to=2026-11-20`. Thẻ tiến độ dự án trên Dashboard
  link tới báo cáo *Digitization progress*.

## Đăng nhập (mockup)

- **Bước 1**: `admin` / `admin`. Sai thì báo lỗi và ghi nhật ký *Sign-in failed* (không ghi mật khẩu).
- **Bước 2**: mã OTP 6 số. Mockup chấp nhận `123456`; sau này mã gửi qua e-mail hoặc lấy từ authenticator app. Có
  *Send a new code* (chỉ báo đã gửi) và *Use another account* (quay lại bước 1).
  - *Send a new code* bị khoá 30 giây sau mỗi lần gửi mã (kể cả lần đầu), nút hiện số giây còn lại. Server cũng từ chối
    gửi lại trước khi hết 30 giây.
  - Mã nhập vào **6 ô**, mỗi ô một số, luôn trái sang phải (cả tiếng Ả Rập), chia 2 nhóm 3 số.
  - `wwwroot/js/otp-input.js`: gõ số thì sang ô sau; Backspace ở ô trống thì về ô trước; phím mũi tên để di chuyển.
    Dán cả mã (hoặc điện thoại tự điền từ tin nhắn) thì mã điền vào đủ 6 ô.
  - Script chép các số vào ô ẩn `login-otp`; trang đọc mã từ ô đó. Đủ 6 số thì tự gửi.
  - Mã sai hoặc gửi mã mới thì 6 ô được làm trống và con trỏ về ô đầu.
  - Mã sai ghi nhật ký *Entered a wrong one-time code*.
  - Sai 5 lần thì phải nhập lại mật khẩu.
  - Chỉ khi mã đúng mới ghi *Signed in*.
- **Phiên**: `wwwroot/js/session.js` lưu `{ user, expires }` vào localStorage key `ocr.session`: 8 giờ, hoặc 30 ngày nếu
  chọn *Remember me*. Script nạp trong `<head>`, chạy trước khi trang hiện ra:
  - Không có phiên (hoặc hết hạn) thì chuyển ngay sang `/login?returnUrl=<trang đang mở>`.
  - Kiểm tra lại sau mỗi lần chuyển trang kiểu enhanced navigation và khi tab khác đăng xuất.
  - Đăng nhập xong thì quay về `returnUrl`, chỉ khi đó là đường dẫn trong site (`/…`, không phải `//…`); còn lại về
    Dashboard.
- **Sign out** (menu tài khoản) xoá phiên rồi mở `/login`.

## Người dùng và phân quyền

Code ở `Features/Mockups/Access/` (`UserDirectory`, `AccessRules`) và `Components/UsersPanel`, `Components/RolesPanel`.
Lưu trong bộ nhớ, mỗi thay đổi ghi một dòng nhật ký.

Menu **Users** (dưới vạch phân cách, trên Settings) là một nhóm có 3 mục con: *Users*, *Roles & access*, *Activity logs*.
Nhóm là thẻ `<details>` nên chạy được không cần Blazor. Nhóm tự mở khi đang ở một trang `/users…`, và chữ của nhóm đổi
màu như mục đang chọn kể cả khi đã thu gọn.

- **Users** (`/users`):
  - Danh sách 25 tài khoản mẫu: `admin` (tài khoản demo, Local), quản trị IT, 12 người scan và 6 người duyệt của lịch sử
    giả lập (đăng nhập lần cuối là ca hôm qua; capture03 bị khoá), records manager và 4 người đọc. Tìm theo tên / user name / e-mail, lọc theo vai trò và trạng thái, phân trang.
  - **Thêm / sửa**: tên, user name, e-mail, vai trò, phòng ban được xem (hoặc *All departments*), mức mật cao nhất được
    xem, bắt buộc MFA, tài khoản hoạt động.
  - Tài khoản AD không sửa được tên và e-mail (lấy từ AD). User name không đổi sau khi tạo.
  - Kiểm tra: bắt buộc tên, user name, e-mail; user name 2–64 ký tự `a-z 0-9 . _ -` và không trùng; e-mail hợp lệ; chọn
    ít nhất một phòng ban. Không được hạ vai trò hoặc khoá **administrator cuối cùng** đang hoạt động.
  - Bên cạnh form là khung **What this user can do**: các quyền của vai trò đang chọn và số document người đó thấy
    trên tổng số document (cả lịch sử giả lập). Thấy được khi: tài khoản hoạt động, vai trò có quyền xem, document thuộc phòng ban được
    xem, mức mật không vượt mức của người dùng. Document chưa có phòng ban chỉ người xem mọi phòng ban mới thấy.
- **Roles & access** (`/users/roles`): ma trận 5 vai trò × 12 quyền, mỗi cột ghi nhóm AD và số người dùng đang
  hoạt động. Administrator luôn giữ quyền *Manage users and roles* và *Change settings* (ô bị khoá), để không ai bị khoá
  ngoài hệ thống.
- Đây là màn quản trị. Quyền **chưa được áp** lên menu, trang hay link tải, vì đăng nhập vẫn chỉ là tài khoản demo.

## Nhật ký hoạt động (Activity logs)

Code ở `Features/Mockups/Activity/` (`SampleActivity`, `ActivityTrail`, `ActivityEndpoints`) và `Pages/ActivityLogPage`.
Trang `/users/activity` trả lời các câu hỏi 60, 66, 69, 70, 106 và 110 của khách.

- **Dữ liệu**: gộp hai nguồn, mới nhất trước.
  - **30 ngày mẫu** trước hôm nay (khoảng 80.000 dòng), sinh từ lịch sử giả lập nên nêu đúng document, người và giờ
    như các danh sách. Tạo lại giống hệt mỗi lần chạy:
    - mỗi ca của người scan: đăng nhập, mỗi lô upload (một người, một giờ) một dòng ghi số document của lô, đăng xuất;
    - dịch vụ OCR: job lỗi, và vài job lỗi rồi gửi lại thành công;
    - kiểm tra tự động: các document bị đưa vào review, kèm số vấn đề (không ghi từng document đạt, để không lấn hết log);
    - người duyệt: sửa trong Word (phiên bản Word 2) rồi duyệt, đúng các document và giờ duyệt của lịch sử;
    - ngày thường: records manager tìm, tải, xuất danh sách / báo cáo; người đọc tìm, mở, tải, in document đã lưu trữ và
      đôi khi bị **từ chối truy cập** (khác phòng ban hoặc vượt mức mật); admin sửa người dùng, quyền, cài đặt, xuất nhật ký;
    - các sự cố bảo mật: gõ sai mật khẩu / OTP, và 3 đêm có IP bên ngoài dò mật khẩu (một lần làm **khoá tài khoản**).
    - Mỗi dòng có IP và trình duyệt / dịch vụ. Tài khoản demo `admin` không có trong dữ liệu mẫu. Document có link khi
      lịch sử đang bật.
  - **Nhật ký thật** (`AuditLog`) của lần chạy này, có nhãn *Live* và nền xanh nhạt. Document của nó có link.
- **Toàn vẹn**: các dòng được nối bằng SHA-256 (mỗi hash gồm hash trước và nội dung dòng).
  - Mỗi lần tải trang hoặc bấm *Verify again*, app băm lại toàn bộ chuỗi. Khung xanh báo số dòng và thời điểm kiểm tra.
  - Nếu một dòng bị sửa, khung đỏ chỉ ra dòng đầu tiên bị sửa.
  - Khung này cũng ghi chính sách: chỉ ghi thêm, giữ 7 năm, đẩy sang SIEM (syslog / CEF); phần SIEM là giả lập.
- **Số liệu nhanh** theo bộ lọc: tổng số dòng, đăng nhập, đăng nhập / OTP thất bại, truy cập bị từ chối, mở / tải /
  in / xuất. Bấm 3 ô cuối để lọc theo đúng loại đó.
- **Lọc**: tìm (người dùng, document, chi tiết, IP), người dùng, nhóm (bảo mật, truy cập document, thay đổi document,
  quản trị, hệ thống), kết quả (thành công, thất bại, bị từ chối), khoảng ngày. Bộ lọc đọc được từ địa chỉ trang.
- Mỗi dòng có nút *Details*: số thứ tự, mã sự kiện SIEM, trình duyệt, nguồn (mẫu / thật), hash và hash trước.
- **Export CSV** (`/users/activity/export.csv`, cùng bộ lọc): UTF-8 có BOM, giờ UTC, có hash. Mỗi lần xuất ghi một dòng
  nhật ký.

## Tài liệu đã duyệt (Document Archive)

- Bấm **Approve and archive** thì app lưu *người duyệt* và *thời điểm duyệt* (hiện ở metadata trang document,
  trên thanh tiến trình và trang `/archive`) và chuyển document sang *Archived*. **Send back to review** thì xoá.
- Trang `/archive` chỉ liệt kê document *Archived*, document thật trước, rồi mới duyệt nhất. Lọc theo phòng
  ban, loại, mức mật, khoảng ngày duyệt (ngày theo giờ server) và tìm như trang Documents.
- **Bản cuối** (`/documents/{id}/final?format=docx|pdf|txt`, chỉ cho document *Archived*):
  - DOCX là file Word mới nhất nếu nó chứa đúng text hiện tại (lưu từ editor Word, hoặc DOCX OCR chưa sửa);
    nếu text đã đổi sau đó (khôi phục phiên bản) thì app tạo DOCX từ text hiện tại. Sửa riêng metadata
    không làm file Word lỗi thời.
  - Trang document dùng cùng cách đó cho bản hiện tại (`/documents/{id}/file?format=…`, document *Reviewing* hoặc
    *Archived*).
  - PDF là chính DOCX đó, do document server (OnlyOffice, cùng engine với editor Word) chuyển sang PDF, nên chữ Ả
    Rập hiển thị như trong Word. Chỉ có khi đã cấu hình document server; không có thì dropdown chỉ còn DOCX và TXT.
    - Với DOCX tạo từ text, document server đọc file qua link `/documents/{id}/text.docx`, có chữ ký HMAC và
      hết hạn như link file Word.
    - Document server lỗi hoặc không trả về PDF thì trả 503 và không ghi vào nhật ký tải.
  - TXT là text hiện tại (UTF-8 có BOM), các trang ngăn bằng ký tự form feed.
- **Xuất CSV** (`/approved/export.csv?…`, cùng bộ lọc): trang Archive không còn nút xuất, nhưng địa chỉ này vẫn dùng được. UTF-8 có BOM để Excel đọc được tiếng Ả Rập,
  tiêu đề cột theo ngôn ngữ đang chọn, thời gian theo UTC (ISO 8601). Ô bắt đầu bằng `= + - @` được thêm
  dấu `'` để Excel không chạy như công thức. Khi không lấy được danh sách job OCR thì không cho xuất (trả 503),
  để file không thiếu document mà người dùng không biết. Tối đa 50.000 document mỗi file: nhiều hơn thì trả 400 và yêu
  cầu lọc hẹp lại (cả kho lưu trữ có hơn một triệu document).

## Sửa Word trực tiếp (OnlyOffice Docs)

Xem ADR 0004. Trên trang document đang *Reviewing*, nút **Edit** mở `/documents/{id}/word`
với editor OnlyOffice nhúng trong trang. Phiên bản Word đầu tiên là DOCX của dịch vụ OCR (hoặc tạo từ text với tài
liệu mẫu / khi dịch vụ OCR tắt). Bấm **Save** (Ctrl+S) hoặc đóng editor thì app lưu phiên bản Word mới, cập nhật text
và ghi nhật ký. Các phiên bản Word xem và tải ở tab Versions (mục *Word file*) và dòng *Word file, version n*.

- Biến cấu hình: `DOCS_EDITOR_PUBLIC_URL`, `DOCS_EDITOR_INTERNAL_URL`, `DOCS_EDITOR_APP_URL`, `DOCS_EDITOR_JWT_SECRET`,
  `DOCS_STORAGE_PATH` (hoặc `Documents:Editor:*`, `Documents:Storage:Path`). Thiếu thì nút Edit ẩn đi, và không còn
  cách sửa text trên giao diện (sửa metadata vẫn được).
- Text chỉ sửa trong Word; Word là bản chính. Metadata sửa ở tab Metadata (*Edit details*).
- **Preview** trên trang document gồm 2 cột: *Original scan* và *Word file*. Cột Word là ảnh từng trang của file Word
  mới nhất, do OnlyOffice render qua Conversion API và lưu cạnh file. Nút *Word / Text* đổi sang xem text. Bấm
  vào ảnh để mở kích thước đầy đủ.
  Không có document server thì cột phải luôn là text.
- Chạy local có editor: thêm `DOCS_EDITOR_JWT_SECRET` vào `.env` rồi
  `docker-compose -f devops/local/docker-compose.yml --profile word up -d --build`.
- Extension chặn quảng cáo/tracker (uBlock, AdBlock, AdGuard, Brave Shields…) chặn file `Analytics.js` của
  OnlyOffice, khiến editor dừng ở khung xám. Vì vậy document server chạy image đã vá (`devops/docker/documentserver.Dockerfile`,
  ADR 0004): file đó được nạp dưới tên khác. Nếu extension vẫn chặn một file khác, sau 45 giây editor chưa báo sẵn sàng
  (`onAppReady`), trang hiện cảnh báo hướng dẫn tắt chặn cho trang (extension cho phép theo trang đang mở, không theo
địa chỉ editor) và nút *Try again*. Editor khởi động muộn thì cảnh báo tự biến mất.

## Khung giao diện

- Mọi bảng danh sách (Review Queue, All Documents, Document Archive, Users, Activity logs) có thanh phân trang
  ở dưới (`Components/TablePager.razor`): chọn 10 / 20 / 50 / 100 dòng mỗi trang, dòng đang xem (vd. *11–20 of 103*),
  các nút trang. Đổi số dòng thì vẫn giữ dòng đầu của trang đang xem. Các bảng cấu hình cố định vài dòng (vai trò, lưu giữ,
  hệ thống kết nối) và bảng tóm tắt trên Dashboard không phân trang.

- Góc phải header có hai dropdown:
  - **Ngôn ngữ**: nút hiện ngôn ngữ đang dùng; bấm vào là danh sách English / العربية, ngôn ngữ đang dùng có dấu
    tích. Chọn ngôn ngữ khác thì tải lại trang. Trang đăng nhập cũng có dropdown này.
  - **Avatar**: menu tài khoản (tên, vai trò demo, **Sign out**).

  Các dropdown (hai menu này và nút tải trên trang Archive) là thẻ `<details class="popup-menu">`, nên chạy được cả khi
  chưa có kết nối Blazor. `wwwroot/js/popup-menu.js` thêm hai việc:
  - Đóng menu khi bấm ra ngoài hoặc nhấn Esc; mở menu này thì menu kia đóng.
  - Với menu có class `is-floating` (menu tải nằm trong bảng có thanh cuộn), đặt khung menu nổi theo cửa sổ, dưới nút,
    hoặc trên nút khi phía dưới không đủ chỗ. Nhờ vậy khung cuộn của bảng không cắt mất menu. Menu này đóng khi trang cuộn.
- **Xoá** luôn hỏi lại bằng hộp thoại cảnh báo (`Components/Shared/ConfirmButton.razor`): focus vào *Cancel*,
  Esc hoặc bấm ra ngoài để đóng, chỉ nút *Delete* màu đỏ mới xoá.

## Giới hạn cần biết

- **Đăng nhập là giả, chỉ chặn ở trình duyệt.** Phiên nằm trong localStorage, server không kiểm tra gì: ai ghi key
  `ocr.session` cũng vào được, và các địa chỉ không phải trang (tải file, CSV, `/api/*`) vẫn mở. Quyền của vai trò chưa
  được áp lên các trang. Khi làm module Identity sẽ thay bằng ASP.NET Core Identity (cookie, MFA) và policy phân quyền
  phía server (CLAUDE.md §8.3, §9).
- **Dữ liệu duyệt, metadata, phiên bản mất khi restart hoặc deploy lại**, dùng chung cho mọi người và chỉ đúng
  khi chạy một instance (app không có database, ADR 0003). Muốn giữ lâu dài cần module Documents có PostgreSQL.
- Danh sách Documents lấy **100 job mới nhất** rồi lọc trong bộ nhớ; tìm theo nội dung text chỉ áp dụng cho
  document đã mở (đã có text trong app) và tài liệu mẫu, vì danh sách job của dịch vụ không kèm text. Document của lịch sử
  giả lập chỉ tìm theo metadata.
- **Tài liệu mẫu** (14 document có nhãn *Sample*, `Data/SampleDocumentSeed.cs`) **tắt mặc định**: các danh sách chỉ có job
  OCR thật. Muốn demo khi không có dịch vụ OCR thì bật bằng `Documents__Samples__Enabled=true`
  (`Documents:Samples:Enabled`). Test component bật sẵn và dùng chúng làm dữ liệu kiểm thử. Người dùng ban đầu và lịch
  lưu giữ ở Settings vẫn là dữ liệu mẫu.
- Khi dịch vụ OCR không kết nối được, All Documents báo lỗi (và vẫn hiện tài liệu mẫu nếu đã bật); Scan báo lỗi ngay khi bấm Upload (và ở lại trang).
- Trang Document Archive và file CSV cũng chỉ gồm các job trong danh sách job mới nhất mà trang Documents lấy về. Link tải
  bản cuối và xuất CSV, như mọi trang khác, **chưa có phân quyền** (đăng nhập còn là giả).
- **Mục tiêu KPI về mặc định khi restart.** Số đo trên Dashboard và Reports (trừ *User activity*) gồm lịch sử giả lập (nhãn
  *Sample data*) và job thật.
- **Người dùng và quyền về mặc định khi restart.** Lịch lưu giữ là dữ liệu mẫu: không có tự động huỷ hay legal hold. Các hệ thống AD, SharePoint, CMS, hệ thống nội bộ, SIEM chỉ ghi *Planned*.
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

## Giao diện: UAE Design System 4.0

Xem ADR 0007. App giữ Bootstrap 5.3 và áp token của UAE Design System (gói `@aegov/designsystem` 4.0.4) bằng CSS
riêng: biến `--ae-*` và `--app-*` trong `wwwroot/app.css`, phần còn lại trong các file `*.razor.css`. Không thêm
Tailwind hay thư viện component nào.

- **Khung app**:
  - Header một hàng, cao 72px, dính trên cùng: logo và tên app (thẳng cột với menu), nút đổi ngôn ngữ
    (`LanguageSwitch`), menu tài khoản.
  - Menu dọc rộng 288px, theo thứ tự: Dashboard · Scan · Review Queue · All Documents · Document Archive · Reports, rồi
    tách riêng nhóm Users (Users · Roles & access · Activity logs) và Settings. Mục đang chọn: chữ AE Gold 700, gạch 3px, nền gold 50.
  - Nội dung rộng tối đa 1600px, canh giữa vùng bên phải menu.
- **Màu**:
  - Màu chính có hai vai trò, đều khai báo trong `wwwroot/app.css`:
    - **Nền có chữ** (nút chính, số trang đang chọn, ô *Pages scanned*): vàng nhạt, chữ nâu đậm Gold 950.
      - Nút: nền Gold 300 (`--app-primary-fill`), hover Gold 400. Tương phản chữ 7.3:1, khi hover 5.7:1.
      - Ô *Pages scanned*: nền Gold 100, viền Gold 300.
      - Khách chọn cách này thay cho chữ trắng trên nền vàng đậm, vì chữ trắng trên màu nhạt hơn Gold 600 không
        đạt 4.5:1 (WCAG AA).
    - **Đồ hoạ và viền** (thanh biểu đồ, chấm tiến trình, checkbox, viền ô nhập, logo): `--app-primary` = `#9b7a33`,
      nhạt hơn AE Gold 600 (`#90702a`) nửa bậc theo yêu cầu của khách. Đạt 4.0:1 trên nền trắng, trên mức 3:1 cho
      đồ hoạ. Không đặt chữ trên màu này.
  - Màu phụ là Camel. Viền focus 2px màu Camel 500.
  - Màu trạng thái dùng badge "soft" của DS: Recognizing xanh biển, Reviewing camel, Archived xanh lá, Failed
    đỏ, Cancelled xám. Luôn đi kèm chữ, không dùng màu đơn lẻ để phân biệt.
- **Font**: Roboto (chữ thường) và Inter (tiêu đề) cho chữ Latin; Noto Kufi Arabic và Alexandria cho chữ Ả Rập.
  - Font được **lưu trong repo** (`wwwroot/fonts/*/`, kèm file giấy phép SIL OFL), vì app chạy on-prem có thể
    không có internet.
  - Không giãn chữ (letter-spacing), để không làm hỏng nét nối của chữ Ả Rập.
- **Component dùng chung**: `StatusBreakdown` (thanh trạng thái ngang và ô chú thích) và `DocumentProgress`
  (thanh tiến trình theo kiểu Timeline của DS; không có trạng thái lỗi nên bước lỗi giữ dạng "đang chạy", kèm lý do).
- **Giấy phép**: theo `LICENSE.md` của gói, Design System chỉ được dùng cho dự án của Chính phủ Liên bang UAE;
  trường hợp khác phải xin phép TDRA. Cần xác nhận trước khi đưa vào production (xem ADR 0007). App không dùng
  logo hay quốc huy của Chính phủ UAE.

## Docker

`OppOcr.Web.csproj` đặt `RequiresAspNetWebAssets=true`. Không có cờ này, bản publish trong Docker thiếu
`_framework/blazor.web.js` (SDK chỉ tải package chứa file đó khi thấy file `.razor` lúc restore, mà Dockerfile
restore từ `.csproj` trước khi copy code), và không trang nào tương tác được.

## Xem nhanh

```bash
dotnet run --project src/Hosts/Web --launch-profile http
```

Mở `/login`, đăng nhập `admin` / `admin`, nhập mã `123456`. Đổi ngôn ngữ bằng English / العربية ở góc header.
