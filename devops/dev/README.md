# Deploy môi trường dev

Các lệnh này chạy **trên chính server dev**, từ thư mục code của repo (ví dụ sau khi `git clone`/`git pull`).
Không có SSH: script build image và chạy container ngay trên máy đang đứng.

Server chỉ cần **Docker + docker compose** (v2 `docker compose` hoặc `docker-compose`) và `curl`.
Không cần .NET SDK: Dockerfile ([devops/docker/Dockerfile](../docker/Dockerfile)) build bên trong container.

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
