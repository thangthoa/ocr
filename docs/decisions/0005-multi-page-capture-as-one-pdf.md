# ADR 0005: Capture nhiều ảnh thành một PDF nhiều trang, ghép ngay trong app

- Trạng thái: Chấp nhận (bổ sung ADR 0003: Capture không còn gửi mỗi file một job)
- Ngày: 2026-11-19

## Bối cảnh

Màn Capture trước đây có phiếu batch (Batch ID, barcode thùng, phòng ban, số file dự kiến) và gửi **mỗi
file một job**. Yêu cầu mới: trước khi upload người dùng chọn **loại tài liệu** (dropdown, mặc định
*Printed / typed*), **One | Multi** và nhập **tên file** (bắt buộc); bỏ phiếu batch. Chế độ *Multi* chọn
nhiều ảnh và **gộp thành một file nhiều trang**, tức là một document (một job OCR).

Dịch vụ OCR nhận PDF nhiều trang (tối đa 20 trang, 25 MB — mã lỗi `too_many_pages`, `file_too_large`)
nhưng không có API ghép ảnh. TIFF nhiều trang chưa được xác nhận là dịch vụ đọc hết các trang.

## Quyết định

- **Ghép thành PDF ở server, trước khi gửi**: use case `UploadOcrPages` (Application) kiểm tra đầu vào
  như upload một file (đuôi, chữ ký file, tổng dung lượng ≤ 25 MB, ≤ 20 trang), gọi port `IPdfComposer`,
  rồi gửi PDF qua chính `UploadOcrFile` (một job, tên `<tên nhập>.pdf`).
- **Không thêm thư viện**: `ImagePdfComposer` (Infrastructure) tự ghi PDF 1.4 tối giản, chỉ nhận **JPG và
  PNG** (định dạng của máy scan và điện thoại):
  - JPEG nhúng nguyên dữ liệu (`DCTDecode`), không giải nén, không giảm chất lượng. Hướng EXIF (ảnh điện
    thoại) được áp dụng bằng ma trận vẽ, nên trang luôn đứng đúng chiều.
  - PNG xám / RGB / palette nhúng nguyên dữ liệu (`FlateDecode` + predictor PNG). PNG có kênh alpha được
    giải nén từng dòng và phủ lên nền trắng (PDF không có alpha trong ảnh). PNG interlaced bị từ chối với
    thông báo rõ (`ocr.image_not_supported`).
- **Kích thước trang theo DPI của ảnh** (JFIF / `pHYs`), để dịch vụ khi render PDF thành ảnh nhận lại xấp
  xỉ số pixel gốc. Ảnh không có DPI đáng tin (ngoài 100–1200, ví dụ ảnh điện thoại ghi 72 dpi) được coi là
  scan 300 dpi.
- **Tên file**: người dùng nhập tên, app tự thêm đuôi (`.pdf` khi ghép; đuôi của file gốc khi chỉ một file),
  trừ khi tên đã có đúng đuôi. Tên không được chứa `/ \ : * ? " < > |` hay ký tự điều khiển.
- **One** vẫn nhận mọi định dạng dịch vụ hỗ trợ (kể cả PDF) và gửi nguyên file. Ở **Multi**, nếu chỉ còn
  một ảnh thì cũng gửi nguyên ảnh, không bọc PDF.
- Chọn file chỉ **liệt kê** (sắp theo tên, số tính theo giá trị: `page-2` trước `page-10`); người dùng đổi
  thứ tự / bỏ trang rồi bấm **Upload**, lúc đó file mới được đọc. Chọn lại file thì danh sách được thay
  (trình duyệt không giữ file của lần chọn trước).

## Phương án đã cân nhắc

- **PDFsharp** (MIT): ghép được JPG/PNG/BMP, nhưng thêm phụ thuộc chỉ cho việc nhúng ảnh; vẫn phải tự xử
  lý hướng EXIF và DPI.
- **SkiaSharp** (MIT): đọc được nhiều định dạng hơn (WebP, BMP), nhưng kèm thư viện native cho Linux/Docker
  và mã hoá lại ảnh.
- **ImageSharp, QuestPDF**: giấy phép không phải MIT/Apache thuần cho mọi trường hợp dùng thương mại.
- **Ghép trong trình duyệt** (canvas → PDF bằng JS): server vẫn phải kiểm tra lại PDF, ảnh bị mã hoá lại
  thành JPEG (giảm chất lượng OCR), khó test.
- **TIFF nhiều trang**: phải giải nén/mã hoá lại PNG, và chưa chắc dịch vụ đọc hết các trang.

## Hệ quả

- Ở Multi chỉ ghép được JPG/PNG; ảnh TIFF/WebP/BMP phải gửi từng file ở chế độ One (hoặc chuyển đổi trước).
- Giả định dịch vụ render trang PDF ở khoảng 200–300 dpi; nếu dịch vụ dùng DPI khác thì có thể cần chỉnh
  `ImagePdfComposer.DefaultDpi`. Cần xác nhận với nhóm dịch vụ OCR.
- Bộ nhớ khi ghép: ảnh (≤ 25 MB) + PDF (≈ cùng cỡ) cho mỗi lần upload; không lưu gì xuống đĩa.
- Mỗi lần upload luôn tạo đúng một job, nên khái niệm batch bị bỏ khỏi Capture. Bản ghi upload trong bộ
  nhớ (`CaptureBatchStore`) chỉ còn giữ người upload và mã lần upload cho trang Documents và nhật ký.
