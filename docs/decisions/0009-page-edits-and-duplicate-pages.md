# ADR 0009: Chèn, thay, xoá trang của tài liệu và cảnh báo trang trùng

- Trạng thái: Chấp nhận (bổ sung ADR 0003, 0006, 0007)
- Ngày: 2026-11-21

## Bối cảnh

Bên AI cập nhật dịch vụ OCR ("API dịch vụ OCR tiếng Ả Rập — tài liệu cho nhóm FE", cập nhật 20/11/2026 (4),
mục 3.8 và 4.4):

- `POST /v1/jobs/{job_id}/pages` chèn trang (một hoặc nhiều file, `position` từ 1 tới `pages_total + 1`, bỏ trống thì
  thêm vào cuối). `PUT /v1/jobs/{job_id}/pages/{n}` thay trang n. Hai thao tác này trả 202, job quay về
  `queued → running → done`. Chỉ trang mới được đọc; trang cũ lấy kết quả lưu sẵn.
- `DELETE /v1/jobs/{job_id}/pages/{n}` xoá ngay (200), các trang sau lùi số. Text cập nhật ngay; file Word dựng lại ở
  nền (`word_ready = false` vài giây). Không xoá được trang cuối cùng (`409 last_page`).
- Chỉ sửa được khi job `queued`, `failed`, hoặc `done` với `word_ready = true`. Đang đọc hoặc đang dựng file Word thì
  trả `409 job_busy`.
- Job có thêm `duplicates` (`identical`, `similar` có ngay khi gửi; `same_text` có khi job xong), `edits`, `updated_at`.
- Callback gửi thêm sự kiện `job.updated` khi job sửa trang xong. Body vẫn là Job, nên endpoint callback hiện có
  (ADR 0006) xử lý như cũ.
- Theo `openapi.json` của dịch vụ (kiểm tra 21/11/2026), chèn và thay trang **chỉ nhận `file`**, không có `file_url`.

## Quyết định

1. **Module Ocr**
   - Domain: `OcrJob` thêm `WordReady`, `Duplicates` (`OcrPageDuplicate`), `Edits`, `UpdatedAtUtc`, và các quy tắc
     `AllowsPageEdits` (mọi trạng thái trừ `cancelled`), `IsBusyWithPages` (`running`, hoặc `done` mà Word chưa xong),
     `CanEditPages`, `CanDeletePage`, `HasPage`, `CanInsertAt`.
   - Application: ba use case `InsertOcrPages`, `ReplaceOcrPage`, `DeleteOcrPage`. Mỗi use case đọc job trước để từ
     chối sớm (bận, số trang sai, trang cuối, quá 20 trang) mà không phải gửi file qua đường mạng chậm. Dịch vụ vẫn
     là nơi quyết định cuối cùng: `job_busy` và `last_page` của dịch vụ được đổi thành lỗi của module.
   - File chèn / thay được kiểm tra như upload: đuôi file, nội dung khớp đuôi, mỗi lần gửi tối đa 25 MB (cùng giới
     hạn với một lần upload, vì file nằm trong bộ nhớ khi gửi).
   - Sau khi sửa, job được thêm lại vào danh sách theo dõi để worker báo khi đọc xong. Chỉ thông báo khi trạng thái
     đổi (chèn / thay: `done → queued`). Xoá trang không đổi trạng thái, nên không có toast "OCR finished" sai nghĩa.
   - `word_ready` không có trong response (dịch vụ cũ) thì coi là `true`.
2. **Gửi file trực tiếp** cho chèn / thay trang, kể cả khi đã cấu hình `OCR_APP_URL` (ADR 0006), vì dịch vụ không
   nhận `file_url` ở hai endpoint này.
3. **Trang document** (`/documents/{id}`):
   - Khung cảnh báo trang trùng: mỗi cặp có nút xem hai trang và nút **Delete page n** (trang sau của cặp).
   - Các nút **Insert pages**, **Replace page n**, **Delete page n** cho trang đang xem. Chỉ hiện khi document đang
     *Recognizing*, *Reviewing* hoặc *Failed*. Document đã lưu trữ phải *Send back to review* trước (ADR 0007).
   - Khi job bận, các nút bị khoá và có dòng giải thích. Trang tự làm mới mỗi 3 giây, kể cả khi job đã `done` nhưng
     Word chưa xong.
   - Ảnh scan hiện cả khi job còn `queued` / `running`, để so hai trang trùng trước khi xoá.
   - Link ảnh trang có thêm `?v={edits}`, để trình duyệt không dùng ảnh cache của trang cũ cùng số.
4. **Text và file Word của document theo kết quả OCR mới**
   - Khi job xong lại (`edits` khác số đã ghi), `DocumentStore` tạo phiên bản mới *OCR result after the pages changed*
     từ text OCR. Text đã sửa trước đó **không được giữ lại** trong bản mới (dịch vụ chỉ có text OCR), nhưng vẫn còn
     trong lịch sử phiên bản. Giao diện ghi rõ điều này trước khi sửa trang.
   - Kiểm tra tự động chạy lại trên các trang mới, nhưng **không tự duyệt**, vì người dùng đang review. Document đã lưu
     trữ mà bị sửa trang từ ngoài app (gọi API trực tiếp) cũng quay về *Reviewing*.
   - File Word: khi `word_ready = true`, file DOCX mới của dịch vụ trở thành phiên bản Word mới. Phiên bản text được
     gắn với phiên bản Word đó (`DocumentVersion.WordVersion`). Trong lúc Word chưa xong, preview hiện "đang hoàn tất
     file Word" và nút sửa Word bị ẩn, để không sửa và lưu đè file cũ lên trang mới.
   - Cũng vì vậy, job vừa xong mà `word_ready = false` không còn bị lấy file DOCX thiếu ảnh logo / chữ ký / con dấu
     làm phiên bản Word đầu tiên (trước đây file thiếu ảnh này được giữ mãi).
5. **Trang trùng là một vấn đề của kiểm tra tự động**, dùng chung công tắc "trang có cảnh báo của dịch vụ OCR". Nếu
   không, document có trang trùng vẫn qua kiểm tra, tự lưu trữ, và không ai thấy cảnh báo.
6. Nhật ký hoạt động ghi thêm `PagesInserted`, `PageReplaced`, `PageDeleted` (nhóm *Document change*).

## Phương án đã cân nhắc

- **Giữ text đã sửa của các trang không đổi, chỉ lấy text OCR cho trang mới.** Cần theo dõi số trang qua từng lần
  chèn / xoá (kể cả lần sửa từ client khác), và không ghép được vào file Word đã sửa. Để lại cho khi có module
  Documents lưu text theo trang.
- **Tự động xoá trang trùng `identical`.** Đặc tả nói dịch vụ chỉ cảnh báo; người dùng quyết định.
- **Cho sửa trang cả khi document đã lưu trữ.** Trái với ADR 0007 (bản lưu trữ là bản cuối).
- **Thêm cài đặt riêng cho trang trùng ở Settings.** Thêm giao diện mà chưa có nhu cầu; dùng chung công tắc cảnh báo.

## Hệ quả

- Chèn / thay trang tải file lên trực tiếp từ server app. Trên đường mạng văn phòng (~5–7 KB/s, ADR 0006), ảnh 1 MB mất
  khoảng 3 phút; file 25 MB có thể vượt `Ocr:Api:RequestTimeoutSeconds` (300). Nên đề nghị bên AI hỗ trợ `file_url`
  cho hai endpoint này.
- Mỗi lần sửa trang tạo một phiên bản text và (khi Word xong) một phiên bản Word mới.
- Vẫn một instance, lưu trong bộ nhớ (ADR 0002, 0003): sau khi restart, document có `edits > 0` được coi như mới và tạo
  phiên bản 1.0 từ text hiện tại.
- Dịch vụ vẫn tự xoá file sau 7 ngày (ADR 0006): sau đó không sửa trang được nữa.
