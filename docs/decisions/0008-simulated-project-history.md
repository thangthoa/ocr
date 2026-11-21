# ADR 0008: Lịch sử dự án giả lập làm nguồn số liệu chung của mockup

- Trạng thái: Chấp nhận
- Ngày: 2026-11-21

## Bối cảnh

Trước đây mỗi màn hình mockup lấy số liệu từ một nguồn riêng, nên các con số không khớp nhau:

- Dashboard dùng số viết cứng theo Ngày / Tuần / Tháng (`SampleReports`). Reports tự tính lại (`SampleReportFacts`) và
  lệch nhẹ so với Dashboard.
- KPI *Review backlog* cố định là 312, thanh trạng thái ghi 196 đang *Reviewing*, còn Review Queue chỉ có vài job thật.
- All Documents, Review Queue và Archive chỉ có job OCR thật (và 14 tài liệu mẫu nếu bật). Upload file test không làm
  thay đổi số nào trên Dashboard hay Reports.
- Báo cáo năng suất liệt kê capture01–12 và review01–06, trong khi trang Users chỉ có 3 tài khoản scan và 2 người duyệt.
  Activity log mẫu tự sinh mã document không có trong danh sách nào.

Chủ sản phẩm muốn dữ liệu thống nhất: mockup giả lập một dự án **đã xử lý 1.093.911 document**, và các file test upload
thêm làm tăng con số đó. Ba điểm đã chốt cùng chủ sản phẩm:

- Khoảng **1 trang mỗi document**. Giữ kế hoạch của hồ sơ thầu: 2.000.000 trang trong 100 ngày, dự án đã chạy 45 ngày.
- Document lịch sử **hiện thành từng dòng** trong các danh sách, phân trang được.
- Lưu trữ bền (qua restart) **làm sau**, thành một việc riêng.

## Quyết định

- **Một nguồn: `ProjectHistory`** (`Features/Mockups/History/`). Nó sinh 1.093.911 document cho 45 ngày trước hôm nay.
  Ngày đầu của lịch sử trùng ngày bắt đầu mặc định của kế hoạch. Dữ liệu sinh ra giống hệt nhau ở mọi lần chạy; chỉ
  ngày tháng đi theo ngày hiện tại.
  - **Hôm nay không có gì giả lập**: chỉ có file upload trong app. Vì vậy tổng số chỉ tăng khi có upload.
  - Dây chuyền chạy 07:00–23:00, chia hai ca, 12 người scan. capture03 bị khoá từ 40 ngày trước nên chỉ scan trong 5 ngày
    đầu.
  - Mỗi document có phòng ban, loại, mức mật, số trang (trung bình khoảng 1,1), confidence theo từng phòng ban.
  - Kiểm tra tự động dùng ngưỡng mặc định. Khoảng 3,7 % document phải vào review; 0,2 % lỗi OCR.
  - 6 người duyệt làm ca 07:30–16:30. Mỗi người lấy document cũ nhất mà họ được xem (theo phòng ban và mức mật), mỗi người
    một tốc độ. Những gì còn chờ ở cuối ngày hôm qua là backlog (khoảng 370 document).
- **Lưu gọn**: khoảng 28 byte mỗi document, tổng khoảng 30 MB. Lịch sử được dựng ở nền khi app khởi động (khoảng 0,9 giây)
  và dùng chung trong cả process. Tên, metadata, text, phiên bản và kết quả kiểm tra của một document được dựng lại từ số
  thứ tự của nó, chỉ khi cần hiển thị.
- **`DocumentSnapshot`** (`DocumentCatalog.SnapshotAsync`) gom job thật, tài liệu mẫu, bản sao của document lịch sử đã sửa,
  và `ProjectFacts` (số liệu cộng theo ngày, phòng ban, người và giờ). Dashboard, KPI, 5 báo cáo, Settings › Retention và
  khung quyền trong Users đều đọc `ProjectFacts`. Các danh sách phân trang trên snapshot (`DocumentCatalog.Query`): job thật
  đứng trước, rồi tài liệu mẫu và lịch sử trộn theo cùng thứ tự; chỉ các dòng của trang đang xem được dựng đầy đủ.
- **Document lịch sử được xử lý như tài liệu mẫu** (`IsSample`): mở, duyệt, sửa, gửi lại review đều được. Khi một document
  bị thay đổi, `DocumentStore` giữ một bản sao. Số liệu và danh sách bỏ bản giả lập của nó và dùng bản sao.
- Khi bật, **tài liệu mẫu thay chỗ 14 document lịch sử**, để tổng vẫn là 1.093.911.
- **Activity log mẫu** (30 ngày trước hôm nay) được sinh từ lịch sử, gồm:
  - mỗi lô upload là một dòng, ghi đúng số document của lô;
  - lỗi OCR;
  - các document bị kiểm tra tự động đánh dấu;
  - người duyệt sửa trong Word rồi duyệt đúng document, đúng giờ.

  Bỏ hai thao tác "xoá" và "trả về review" khỏi log mẫu, vì chúng mâu thuẫn với trạng thái của document.
- **Users**: thêm capture04–12 và review03–06 (review03 xem được tài liệu *Secret*). Lần đăng nhập cuối của họ là ca làm
  hôm qua.
- Tìm kiếm document lịch sử chỉ theo **metadata** (mã, tên file, tiêu đề, số hiệu, từ khoá), không theo nội dung. Ô tìm
  báo điều này khi chọn tìm theo nội dung. Số hiệu của document lịch sử không trùng nhau.
- File CSV xuất từ Archive giới hạn **50.000 dòng**. Nhiều hơn thì trả 400 và yêu cầu lọc hẹp lại.
- Cấu hình `Documents:History:Enabled` (mặc định bật). Test component tắt nó, trừ các test về chính lịch sử và về số liệu.

## Các phương án đã cân nhắc

- **Chỉ có số đếm, không có dòng**: làm nhanh hơn, nhưng danh sách và số đếm vẫn khác nhau. Chủ sản phẩm chọn hiện từng
  dòng.
- **Giả lập cả hôm nay tới giờ hiện tại**: chế độ Ngày trên Dashboard luôn có số. Đổi lại, tổng tự tăng trong ngày, không
  còn đúng "1.093.911 + file test".
- **Neo lịch sử vào lúc app khởi động**: ngày tháng không đổi khi qua nửa đêm. Nhưng server chạy nhiều ngày thì lịch sử lệch
  khỏi kế hoạch, và Dashboard sẽ trống sau ngày đầu.
- **Nhiều trang mỗi document (khoảng 12)**: thành khoảng 13 triệu trang, phải đổi kế hoạch thầu và kéo dài dự án.
- **Lưu đủ một triệu bản ghi, hoặc dùng PostgreSQL**: tốn bộ nhớ, và trái với ADR 0003. Phần này để lại cho việc lưu trữ
  bền.

## Hệ quả

- Mọi màn hình khớp nhau. Upload một file thì tổng thành 1.093.912 ngay; Dashboard tự làm mới khi có job thay đổi.
- Chế độ **Ngày** trên Dashboard chỉ có file upload hôm nay. Phần lớn KPI theo ngày hiện *No data yet*, *Pages scanned per
  day* là *Off track*, và mức so với hôm qua là số âm. Chế độ Tuần và 30 ngày cũng tính cả hôm nay.
- Ngày tháng của document lịch sử trượt theo hôm nay: qua nửa đêm, cả lịch sử lùi một ngày. Bản sao của document đã sửa
  vẫn giữ giờ cũ.
- Tốn thêm khoảng 30 MB bộ nhớ và 0,9 giây CPU khi khởi động. Activity log mẫu có khoảng 80.000 dòng; mỗi lần mở trang mất
  khoảng 0,8 giây để băm lại chuỗi.
- Các thay đổi trên document lịch sử, và trạng thái duyệt của file test, vẫn chỉ nằm trong RAM và mất khi restart. Lưu trữ
  bền sẽ có ADR riêng.
- Khi có dữ liệu thật hoặc module Documents: bỏ `ProjectHistory`; `ProjectFacts` thành query của module báo cáo.
