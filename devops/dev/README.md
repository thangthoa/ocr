# Deploy môi trường dev

Các lệnh này chạy **trên chính server dev**, từ thư mục code của repo (ví dụ sau khi `git clone`/`git pull`).
Không có SSH: script build image và chạy container ngay trên máy đang đứng.

Server chỉ cần **Docker + docker compose** (v2 `docker compose` hoặc `docker-compose`) và `curl`.
Nên dùng v2: `docker-compose` v1 (1.29) đã ngừng hỗ trợ và không recreate được container trên Docker Engine 25+
(`KeyError: 'ContainerConfig'`). Script vẫn chạy được với v1 vì luôn xoá container cũ rồi mới tạo container mới.
Không cần .NET SDK: Dockerfile ([devops/docker/Dockerfile](../docker/Dockerfile)) build bên trong container.

## Sửa Word trực tiếp (OnlyOffice, ADR 0004)

docker-compose chạy thêm container `<CONTAINER_NAME>-docs` (OnlyOffice Docs, cổng `DOCS_PORT`, mặc định 8085).
Server cần thêm khoảng 4 GB RAM; editor khởi động 1–2 phút sau mỗi lần deploy (`status.sh` báo `word: ok` khi sẵn sàng).

- `DOCS_EDITOR_PUBLIC_URL`: địa chỉ **trình duyệt** mở được tới editor. Dùng `http://localhost:8085` khi chỉ
  dùng trên máy này; khi người khác truy cập: `http://<địa chỉ server>:8085`.
- `DOCS_EDITOR_JWT_SECRET`: secret dùng chung giữa app và OnlyOffice (≥ 32 ký tự). `setup.sh` tự sinh.
- File Word đã sửa nằm trên volume `<CONTAINER_NAME>_documents`; deploy và `stop.sh` không xoá volume.
- Bản Community giới hạn 20 người sửa cùng lúc; production cần license Enterprise.
- Editor đứng mãi ở khung xám, Console báo `Analytics.js net::ERR_FAILED` / `ERR_BLOCKED_BY_CLIENT`: extension chặn
  quảng cáo trên trình duyệt đang chặn editor. Tắt extension cho trang app (đang mở trang nào thì cho phép trang đó;
  với `localhost` là cả hai cổng), hoặc mở bằng cửa sổ ẩn danh.

Server đã cài từ trước: chạy lại `./devops/dev/setup.sh` một lần để thêm các biến trên vào `.env`, rồi deploy.

## Lần đầu trên server

```bash
git clone git@github.com:thangthoa/ocr.git && cd ocr
./devops/dev/setup.sh          # kiểm tra Docker, tạo devops/dev/.env (quyền 600)
nano devops/dev/.env           # điền OCR_API_KEY (và APP_PORT nếu không dùng 5286)
./devops/dev/deploy.sh
```

`devops/dev/.env` bị git bỏ qua; đừng commit file này.

## Lệnh

| Lệnh | Việc |
|---|---|
| `./devops/dev/deploy.sh` | Build image từ code hiện tại → chạy lại container → kiểm tra health |
| `./devops/dev/deploy.sh --pull` | `git pull --ff-only` trước rồi deploy |
| `./devops/dev/status.sh` | Container đang chạy, `/health/live`, `/health/ready` |
| `./devops/dev/logs.sh [số dòng]` | Theo dõi log (mặc định 200 dòng cuối, Ctrl+C để thoát) |
| `./devops/dev/rollback.sh` | Chạy lại image của lần deploy trước |
| `./devops/dev/stop.sh` | Dừng app (giữ image và `.env`) |

Mỗi lần deploy gắn tag theo commit (`<sha>`, hoặc `<sha>-dirty-<thời gian>` nếu còn thay đổi chưa commit).
Giữ 3 image gần nhất để rollback, image cũ hơn bị xoá. Trạng thái nằm trong `devops/dev/.state/` (git bỏ qua).

`/health/ready` kiểm tra cả dịch vụ OCR: báo NOT READY khi server không gọi được dịch vụ OCR, dù app vẫn chạy.

## Tuỳ chỉnh

Đặt biến môi trường khi chạy lệnh:

| Biến | Mặc định | Ý nghĩa |
|---|---|---|
| `APP_PORT` | trong `.env`, không có thì `5286` | Cổng app trên server |
| `ENV_FILE` | `devops/dev/.env` | File chứa `OCR_API_KEY`, `OCR_API_BASE_URL` |
| `CONTAINER_NAME` | `oppocr-dev` | Tên container |
| `IMAGE_NAME` | `oppocr-web` | Tên image |
| `KEEP_IMAGES` | `3` | Số image giữ lại để rollback |

## Lưu ý

- App **chưa có đăng nhập**: ai vào được `http://<server>:<APP_PORT>` đều upload được (dùng khoá API của
  server) và xem được mọi job. Chỉ mở trong mạng nội bộ.
- Container chạy `ASPNETCORE_ENVIRONMENT=Development` (trang lỗi chi tiết). Không dùng cho production.
