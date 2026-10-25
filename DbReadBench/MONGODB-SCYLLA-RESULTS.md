# MongoDB 8 vs ScyllaDB — kết quả thực tế 24/10/2026

## Cấu hình

- MongoDB **8.0.34**, WiredTiger; ScyllaDB **6.2.3** (giữ phiên bản Scylla từ phép đo trước). Binary version và image digest lưu trong environment.json.
- Cả hai database: 2 CPU, giới hạn 3 GB RAM; MongoDB WiredTiger cache 1 GB, Scylla internal memory 1 GB. Client .NET 10: 2 CPU, giới hạn 3 GB RAM.
- Docker engine có 8 CPU và 12.379.713.536 byte RAM. Có hai container ngoài bài test (Baget và Redis); môi trường không hoàn toàn cô lập.
- 100.000 dòng, payload ASCII 256 byte, đọc ngẫu nhiên uniform theo primary key, closed-loop, zero think time. MongoDB dùng index _id; payload-only projection.
- 4 mức CCU × 2 DB × 2 lượt = **16 scenario**. Mỗi scenario warmup 5 giây, đo 15 giây, cooldown 2 giây; thứ tự DB đảo ở lượt thứ hai.
- Hai DB chạy đồng thời nhưng nhận tải lần lượt. Application max-inflight = 256 cho cả hai. CCU lớn hơn 256 chờ hàng đợi, không có 10.000 query đồng thời trên server.
- Tắt retry trong MongoDB/Scylla driver. Mongo standalone primary/local; Scylla RF=1/ONE. Không đánh đồng semantics replication hay HA.

## Tổng hợp

RPS là **trung bình số học của hai lượt**, mỗi lượt Success/WallSeconds (kể cả drain). p95 là **khoảng min–max p95 của hai lượt**, không phải p95 gộp. Latency bao gồm application queue, driver, network và database.

| CCU | MongoDB RPS TB | ScyllaDB RPS TB | Tỷ lệ Scylla/Mongo | MongoDB p95 ms | ScyllaDB p95 ms |
|---:|---:|---:|---:|---:|---:|
| 100 | 11443.9 | 51917.5 | 4.54x | 46.00–48.83 | 3.86–4.53 |
| 1000 | 11606.1 | 57966.4 | 4.99x | 111.52–126.92 | 23.62–25.07 |
| 5000 | 11566.7 | 47746.3 | 4.13x | 521.41–605.34 | 119.57–126.92 |
| 10000 | 11066.2 | 45834.4 | 4.14x | 1121.82–1179.04 | 249.69–249.69 |

**Lỗi: 0 trên toàn bộ 16 scenario.** Tất cả scenario có request thành công, count latency khớp success + errors, peak-inflight không vượt 256.

## CPU client

Giá trị dưới là trung bình CPU-seconds / wall-seconds qua hai lượt, đơn vị core; client bị giới hạn 2 core. Gần 2 core cho thấy bộ tạo tải/driver có thể là bottleneck. Đây không phải CPU server.

| CCU | MongoDB client cores | ScyllaDB client cores |
|---:|---:|---:|
| 100 | 1.60 | 1.91 |
| 1000 | 1.58 | 1.71 |
| 5000 | 1.57 | 1.58 |
| 10000 | 1.45 | 1.57 |

resource-samples.jsonl giữ các snapshot docker stats cách khoảng 40 giây; snapshot bao gồm cả warmup/cooldown và không phải phép đo liên tục hay trung bình theo scenario.

## Từng lượt

| DB | CCU | Lượt | RPS | p95 ms | p99 ms | Drain s | Lỗi |
|---|---:|---:|---:|---:|---:|---:|---:|
| mongodb | 100 | 1 | 10665.1 | 46.00 | 60.18 | 0.005 | 0 |
| scylla | 100 | 1 | 45745.1 | 4.53 | 23.38 | 0.001 | 0 |
| scylla | 100 | 2 | 58089.9 | 3.86 | 12.49 | 0.003 | 0 |
| mongodb | 100 | 2 | 12222.7 | 48.83 | 58.41 | 0.005 | 0 |
| mongodb | 1000 | 1 | 12000.5 | 111.52 | 156.42 | 0.081 | 0 |
| scylla | 1000 | 1 | 56702.7 | 25.07 | 31.21 | 0.019 | 0 |
| scylla | 1000 | 2 | 59230.1 | 23.62 | 28.82 | 0.014 | 0 |
| mongodb | 1000 | 2 | 11211.7 | 126.92 | 166.04 | 0.048 | 0 |
| mongodb | 5000 | 1 | 11951.3 | 521.41 | 570.26 | 0.397 | 0 |
| scylla | 5000 | 1 | 47237.6 | 126.92 | 138.81 | 0.094 | 0 |
| scylla | 5000 | 2 | 48254.9 | 119.57 | 126.92 | 0.084 | 0 |
| mongodb | 5000 | 2 | 11182.0 | 605.34 | 655.49 | 0.400 | 0 |
| mongodb | 10000 | 1 | 10913.7 | 1179.04 | 1214.77 | 0.811 | 0 |
| scylla | 10000 | 1 | 45729.1 | 249.69 | 267.70 | 0.166 | 0 |
| scylla | 10000 | 2 | 45939.7 | 249.69 | 265.05 | 0.164 | 0 |
| mongodb | 10000 | 2 | 11218.6 | 1121.82 | 1190.83 | 1.065 | 0 |

## Diễn giải và giới hạn

Trong đúng cấu hình lab này, Scylla có throughput end-to-end cao hơn MongoDB. Không suy diễn thành khẳng định Scylla luôn nhanh hơn MongoDB: workload chỉ là warm-cache point-read nhỏ; payload x lặp lại dễ nén; dataset vừa bộ nhớ; giới hạn client 2 CPU; thư viện driver và giao thức khác nhau; hai lượt 15 giây chưa đủ để lập khoảng tin cậy hoặc đánh giá production.

Ở tải cao, p95 chủ yếu có thể tăng do queue phía client. Xem QueueLatency và ServiceLatency trong results.json trước khi coi đó là latency riêng của database. Không so trực tiếp số liệu này với smoke test PostgreSQL trước đó vì dataset/thời gian/lượt đo khác.

Để quyết định production: tăng thời gian lên 60–300 giây, ít nhất 3–5 lượt, tăng tài nguyên client nếu bão hòa, dùng dữ liệu có entropy thực và dataset lớn hơn RAM, bổ sung workload/replication/consistency phù hợp ứng dụng.

## Kiểm tra triển khai

- Build Release: 0 warning, 0 error; self-test PASS, bao gồm chọn cặp MongoDB/Scylla và giữ tương thích --database both.
- Script PowerShell chạy end-to-end cho cặp MongoDB/Scylla ở 100 CCU: PASS, không làm đổi thư mục terminal của caller. Đây là lượt riêng 1 giây, không trộn vào bảng 15 giây.
- Explain một query mẫu theo _id với cùng projection: EXPRESS_IXSCAN trên index _id_, examined 1 key / 1 document, returned 1. Kết quả gốc được lưu cùng báo cáo.
- Trước khi dừng: cả hai container đang chạy, OOMKilled=false. Các container lab được dừng sau kiểm tra; không xóa volume hay dữ liệu.

## Tái chạy

```powershell
./Run-Benchmark.ps1 -Database "mongodb,scylla" -Rows 100000 -PayloadBytes 256 -Duration 15 -Warmup 5 -Cooldown 2 -Repetitions 2
```

Raw data: validation-mongodb-scylla/results.json, results.csv, metadata.json, environment.json; query plan: mongo-query-plan.json. Image mongo:8.0 có thể trỏ patch mới khi pull; dùng digest đã ghi nếu cần tái lập đúng image.
