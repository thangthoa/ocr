# Chạy dự án ở máy local

## Yêu cầu

- .NET SDK 10 (`~/.dotnet/dotnet` nếu chưa có trong PATH)
- Khoá API của dịch vụ OCR

App không dùng database (xem ADR 0003).

## Lần đầu

```bash
cp .env.example .env      # điền OCR_API_KEY (và OCR_API_BASE_URL nếu khác mặc định)
```

| Biến | Tương đương | Ý nghĩa |
|---|---|---|
| `OCR_API_BASE_URL` | `Ocr:Api:BaseUrl` | Địa chỉ dịch vụ OCR, mặc định `http://n2.ckey.vn:1436` |
| `OCR_API_KEY` | `Ocr:Api:ApiKey` | Khoá API, bắt buộc; chỉ ở server, không commit |

Ở môi trường Development, app tự đọc `.env` ở thư mục gốc repo (biến môi trường thật vẫn được ưu tiên).
Môi trường khác phải đặt biến môi trường như bình thường. Thiếu khoá thì app dừng ngay khi khởi động.

## Chạy app

```bash
dotnet run --project src/Hosts/Web --launch-profile http
```

- Upload OCR: `/ocr` · Danh sách job: `/ocr/jobs` · Chi tiết: `/ocr/jobs/{job_id}`
- Health: `/health/live` (process), `/health/ready` (kèm dịch vụ OCR)

Mỗi file upload được gửi ngay tới dịch vụ OCR qua HTTP không mã hoá, tới máy chủ của bên thứ ba:
chỉ dùng tài liệu đã được phép thử.

## Kiểm tra trước khi push

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet format --verify-no-changes
```

Test tự động không gọi dịch vụ OCR thật (client HTTP được test với response giả).

## Đa ngôn ngữ (i18n)

- Ngôn ngữ hỗ trợ: `en` (English, mặc định), `ar` (العربية). Danh sách nằm ở `LocalizationSetup.SupportedCultures`.
- Ngôn ngữ chỉ đổi khi người dùng bấm English / العربية (lưu cookie `.AspNetCore.Culture`); `Accept-Language` của trình duyệt bị bỏ qua.
- Tiếng Ả Rập hiển thị phải sang trái: `<html dir="rtl">` và Bootstrap RTL được chọn theo ngôn ngữ; CSS dùng
  thuộc tính logic (`margin-inline-start`, `inset-inline-end`…) thay vì `left`/`right`. Nội dung không phải
  tiếng Ả Rập (mã job, bước xử lý của dịch vụ) đặt `dir="ltr"`, tên file đặt `dir="auto"`.
- Ngày tháng tiếng Ả Rập dùng lịch Gregorian (mặc định của .NET cho `ar` là Hijri Umm al-Qura).
- Chuỗi giao diện nằm trong file `.resx` cạnh class đánh dấu:
  - `src/Hosts/Web/Localization/SharedText{,.ar}.resx`: layout, menu, trang chủ
  - `src/Hosts/Web/Features/Ocr/OcrText{,.ar}.resx`: chức năng OCR
- Application/Domain không chứa chuỗi hiển thị. Use case trả mã lỗi (ví dụ `ocr.too_large`, hoặc mã của
  dịch vụ như `too_many_pages`), Web dịch mã đó bằng `.resx`.
- Thêm chuỗi mới: thêm key vào **cả** file mặc định và `.ar.resx`. Test `LocalizationTests` sẽ fail
  nếu hai file lệch key hoặc một mã lỗi chưa có bản dịch.

## Chạy bằng Docker

```bash
docker-compose -f devops/local/docker-compose.yml up -d --build    # http://localhost:8080, đọc OCR_API_* từ .env
LOCAL_PORT=8096 docker-compose -f devops/local/docker-compose.yml up -d --build   # nếu cổng 8080 đã bị chiếm
```

## Deploy môi trường dev

Chạy trên chính server dev, xem [devops/dev/README.md](../../devops/dev/README.md): `./devops/dev/setup.sh` (lần đầu) rồi `./devops/dev/deploy.sh`.
