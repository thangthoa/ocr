# ADR 0004: Sửa file Word trực tiếp bằng OnlyOffice Docs, lưu các phiên bản trên đĩa

- Trạng thái: Chấp nhận (thay một phần ADR 0003: cho phép lưu file cục bộ, chỉ cho file Word đã sửa)
- Ngày: 2026-11-19

## Bối cảnh

Người dùng cần sửa kết quả OCR **dưới dạng Word, ngay trong trình duyệt**, giữ đúng định dạng DOCX (bảng,
tiêu đề, chữ Ả Rập phải sang trái), và mỗi lần lưu là một phiên bản mới. Trước đó trang document chỉ sửa
được text thuần của từng trang. ADR 0003 quy định app không có database và không lưu file cục bộ.

## Quyết định

- **Nhúng OnlyOffice Docs** (Community Edition, image `onlyoffice/documentserver:9.4.0.1`, ghim phiên bản)
  chạy như một service riêng cạnh app. Trang `/documents/{id}/word` tải `api.js` của document server và mở
  editor với cấu hình do server tạo.
- **Bảo mật giữa hai service**:
  - Cấu hình editor được ký **JWT HS256** bằng secret dùng chung (`DOCS_EDITOR_JWT_SECRET`, ≥ 32 ký tự,
    chỉ nằm phía server); trình duyệt không sửa được URL file hay callback.
  - Document server không có phiên đăng nhập, nên link tải file nó dùng được ký **HMAC** và hết hạn
    (mặc định 240 phút); link callback ký HMAC, và thân callback phải mang JWT hợp lệ.
  - App chỉ tải file đã sửa từ đúng địa chỉ document server (đổi địa chỉ public sang địa chỉ nội bộ), để một
    callback giả không khiến app gọi tới nơi khác (SSRF).
- **Luồng lưu**: OnlyOffice gọi callback khi người dùng bấm Save (status 6) hoặc khi đóng editor (status 2).
  App tải file về, bỏ qua nếu trùng hệt phiên bản trước (SHA-256), lưu thành phiên bản mới, đọc lại text để
  cập nhật nội dung tìm kiếm, và ghi nhật ký hoạt động.
- **Phiên bản 1** là DOCX của dịch vụ OCR; nếu không có (tài liệu mẫu, dịch vụ OCR không kết nối được) thì
  app tự tạo DOCX từ text (không thêm thư viện Office; chỉ dùng `System.IO.Compression` và XML).
- **Lưu trữ**: `{Documents:Storage:Path}/{id}/{n}.docx` cùng `versions.json` (mặc định `App_Data/documents`;
  trong container là `/app/data/documents` trên Docker volume `documents`, không mất khi deploy lại).
- **Xem trước** (trang `/documents/{id}`): mỗi trang ảnh gốc hiện cạnh cùng trang đó của file Word mới nhất.
  App gọi Conversion API (`/converter`, request ký JWT, `outputtype: png`, `thumbnail.first: false`). Document
  server trả về zip ảnh PNG từng trang (1240×1754), và app lưu vào `{id}/preview/{n}/{trang}.png`. Mỗi phiên bản chỉ
  render một lần. Ảnh render bằng chính engine của editor nên giống hệt khi mở Word. Trình duyệt chỉ tải ảnh,
  không nạp editor. Document server tắt hoặc lỗi thì trang hiện text OCR kèm nút thử lại.
- Không cấu hình document server thì tính năng tự ẩn; phần còn lại của app chạy bình thường.
- **Chạy được khi trình duyệt chặn quảng cáo** (bổ sung 2026-11-20): editor nạp `web-apps/apps/common/Analytics.js`
  như một module bắt buộc (`require.config` trong `apps/*/main/app.js`). Extension chặn quảng cáo/tracker
  (uBlock Origin, AdGuard, Brave Shields) và web filter dùng chung danh sách chặn mọi URL có `analytics.js`, nên editor
  dừng ở khung xám. Image `oppocr-documentserver:9.4.0.1` (`devops/docker/documentserver.Dockerfile`) dựng từ image
  gốc và chỉ thay một điểm: module được nạp dưới tên `common/AppHooks.js`, nội dung file giữ nguyên. Build fail nếu
  phiên bản OnlyOffice mới nạp module theo cách khác, để không âm thầm phát hành editor vẫn bị chặn.
  - Đã thử: chặn `*nalytics.js*` (`ERR_BLOCKED_BY_CLIENT`). Với image gốc, editor không khởi động. Với image đã vá,
    editor khởi động bình thường.
  - Cache trình duyệt: mã trong URL (`/9.4.0-<hash>/`) sinh lại mỗi lần container khởi động, nên sau deploy
    trình duyệt tải bản mới.

## Phương án đã cân nhắc

- **Trình soạn thảo rich text trong trang** (không thêm server): nhẹ nhưng không giữ đúng định dạng DOCX.
- **Mở bằng Microsoft Word trên máy** (WebDAV / `ms-word:`): giữ định dạng nhưng cần Office trên mọi máy,
  WebDAV và xác thực phía server.
- **Tải về, sửa, upload lại**: đơn giản nhưng không phải sửa trực tiếp.
- **Collabora Online**: giấy phép MPL, nhưng phải cài đặt giao thức WOPI (nhiều endpoint hơn) và định dạng DOCX
  thường lệch hơn OnlyOffice.
- Với việc editor bị chặn:
  - **Chỉ hướng dẫn người dùng tắt chặn cho trang**: không phải sửa image, nhưng mỗi người dùng (kể cả khách hàng)
    phải tự làm, và web filter của công ty thì người dùng không tắt được. Cảnh báo sau 45 giây vẫn giữ, phòng khi
    có file khác bị chặn.
  - **Đổi URL ở reverse proxy** (`sub_filter` của nginx): nginx trước document server không nằm trong repo, phải
    tắt nén ở upstream, và vẫn là sửa phần mềm khi phục vụ.

## Hệ quả

- Thêm một service nặng: image khoảng 1,4 GB, nên có từ 4 GB RAM, khởi động 1–2 phút sau mỗi lần deploy.
- **Giấy phép**: bản Community dùng AGPL v3 và giới hạn **20 người sửa cùng lúc**. Proposal cần ≥ 100 người
  dùng đồng thời, nên production cần **license OnlyOffice Docs Enterprise** (hoặc đánh giá lại phương án).
- Image đã vá là **bản sửa đổi của phần mềm AGPL v3**. Theo điều 13, người dùng tương tác với nó qua mạng phải lấy
  được mã nguồn tương ứng. Mã nguồn đó là mã nguồn OnlyOffice cùng `devops/docker/documentserver.Dockerfile`;
  file này cần được bàn giao cùng mã nguồn (proposal §22). Thông báo bản quyền và logo của OnlyOffice giữ nguyên.
- Khi nâng phiên bản OnlyOffice, sửa dòng `FROM` trong `documentserver.Dockerfile` và tag `DOCS_IMAGE`. Nếu build
  fail ở bước vá, cần xem lại cách bản mới nạp module analytics.
- Document server phải tải file từ app qua mạng Docker nội bộ, nên bật `ALLOW_PRIVATE_IP_ADDRESS`. Chỉ mở cổng
  document server cho mạng nội bộ.
- `DOCS_EDITOR_PUBLIC_URL` phải là địa chỉ trình duyệt mở được (với người dùng khác máy: `http://<server>:8085`).
- File Word được giữ trên volume, nhưng trạng thái duyệt, metadata và lịch sử text vẫn trong bộ nhớ (ADR 0003):
  sau khi restart, danh sách phiên bản Word còn nhưng các phiên bản text bị mất. Dữ liệu trên volume cần được
  backup; tải file DOCX của dịch vụ OCR vẫn trả kết quả OCR gốc.
- Lần đầu xem một phiên bản mất vài giây để render (trang hiện trạng thái đang render). Ảnh xem trước nằm trên
  volume cùng file Word, khoảng 20–60 KB mỗi trang.
- Khi có module Documents với PostgreSQL, phần lưu trữ này chuyển vào Infrastructure của module đó (lưu file
  trên object storage theo proposal), còn hợp đồng với OnlyOffice giữ nguyên.
