# Cassandra vs ScyllaDB — 5.000 request in-flight, 25/10/2026

## Khác bài test trước ở đâu?

Bài trước: 5.000 CCU nhưng application cap chỉ 256 request đang gọi driver, phần còn lại chờ semaphore. Bài này: **CCU=5.000 và max-inflight=5.000**, không còn cap 256. Cả hai DB dùng cùng cấu hình pool CQL 4 connection query, mỗi connection 2.048 slot, tổng 8.192 slot để không bị trần client 4.096 slot của pool cũ. Không tạo 5.000 connection hay 5.000 thread.

Đây là số request chưa hoàn tất ở application/driver; **không phải 5.000 query cùng thực thi CPU ở server**. Mỗi DB vẫn chỉ có giới hạn 2 CPU.

## Thiết lập

- Cassandra 5.0.8 và ScyllaDB 6.2.3, mỗi DB single node, giới hạn 2 CPU / 3 GB RAM. Cassandra JVM heap 1 GB; Scylla internal memory 1 GB. Hai mức bộ nhớ nội bộ này không tương đương hoàn toàn.
- Client .NET 10 giới hạn 2 CPU / 3 GB RAM; cùng CassandraCSharpDriver 3.23.0, protocol V4, prepared SELECT theo ID, RF=1, consistency ONE, tắt retry policy.
- Dataset đã có: 100.000 ID, payload ASCII 256 byte ký tự x; lượt này SkipSeed nhưng kiểm tra tất cả ID trước khi đo. Không clear cache/ép flush/compaction.
- 30 giây đo + 10 giây warmup + 2 giây cooldown, 2 lượt mỗi DB; đảo thứ tự DB ở lượt thứ hai. Hai DB nhận tải lần lượt. Tổng 4 scenario; môi trường local Docker không hoàn toàn cô lập.

## Kết quả

**Biến động lớn giữa hai lượt:** throughput Cassandra tăng khoảng 2,97 lần, Scylla khoảng 1,90 lần. Không xem trung bình hoặc tỷ lệ giữa hai DB là năng lực ổn định hay kết luận production; cần thêm lượt đo dài trong môi trường cô lập.

RPS là trung bình số học hai lượt, từng lượt Success/WallSeconds kể cả drain. p95/p99 là khoảng min–max percentile của hai lượt, **không phải percentile gộp**.

| DB | Read/s TB | p95 ms | p99 ms | App peak | Driver peak quan sát | Query connections | Lỗi |
|---|---:|---:|---:|---:|---:|---:|---:|
| cassandra | 6883.7 | 1226.92–2692.80 | 1738.06–3385.29 | 5000 | 5000 | 4 | 0 |
| scylla | 15043.8 | 542.58–1424.41 | 702.78–2038.01 | 5000 | 5000 | 4 | 0 |

Tổng 1,327,072 request thành công, 0 lỗi. Tỷ lệ throughput Scylla/Cassandra: 2.19x trong đúng lượt đo này.

## Bằng chứng concurrency và hàng đợi

- PeakInflight là counter các lời gọi ReadAsync active phía application, có thể bao gồm pool/driver/network/materialization.
- DriverPeakInflight/DriverMeanInflight lấy từ session.GetState().GetInFlightQueries; DriverPeakConnections lấy từ GetOpenConnections. Mục tiêu lấy mẫu mỗi 20 ms, gồm drain; scheduler có thể làm trễ. Peak là giá trị quan sát được, không đảm bảo bắt mọi peak; mean là trung bình theo mẫu, không có trọng số thời gian. Có thể gồm request nội bộ driver; không phải counter số query đang chạy CPU trong DB.
- Số connection ở bảng là pool query, không tính control connection. JSON/CSV giữ số liệu từng lượt và số sample.

| DB | Driver mean theo mẫu | Số mẫu hai lượt | Queue p95 ms | Service p95 ms | Client cores TB / cap 2 |
|---|---:|---:|---:|---:|---:|
| cassandra | 4612.2 | 2169 | 0.00–0.00 | 1226.92–2692.80 | 0.93 |
| scylla | 2663.7 | 1004 | 0.00–0.00 | 542.58–1424.41 | 1.49 |

## Từng lượt

| DB | Lượt | Read/s | p95 ms | p99 ms | App peak | Driver peak | Driver mean | Pool conn | Drain s | Lỗi |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| cassandra | 1 | 3470.8 | 2692.80 | 3385.29 | 5000 | 5000 | 4826.5 | 4 | 0.681 | 0 |
| scylla | 1 | 10372.1 | 1424.41 | 2038.01 | 5000 | 5000 | 2519.0 | 4 | 0.511 | 0 |
| scylla | 2 | 19715.5 | 542.58 | 702.78 | 5000 | 5000 | 2808.3 | 4 | 0.092 | 0 |
| cassandra | 2 | 10296.6 | 1226.92 | 1738.06 | 5000 | 5000 | 4397.8 | 4 | 0.189 | 0 |

## Giới hạn

Đây là warm-cache point-read nhỏ, payload dễ nén, closed-loop zero think time; không đánh giá dữ liệu vượt RAM, write workload, HA, multi-node hoặc open-loop arrival rate. Hai lượt chưa đủ lập khoảng tin cậy; JIT/GC, trạng thái memtable/SSTable và workload khác trên máy có thể ảnh hưởng.

Không kết luận nâng cap gây thay đổi throughput chỉ bằng cách so với báo cáo cũ: thời điểm, warmup/duration, số connection và instrumentation đã khác. Nếu cần so tác động riêng của cap, phải chạy một thí nghiệm A/B cùng điều kiện. Xem driver peak/mean để biết concurrency quan sát được, không chỉ đọc giá trị cấu hình 5.000.

## Tái chạy

Release build không có warning/error; self-test và kiểm tra cú pháp PowerShell đều PASS. Sau benchmark, chỉ hai container Cassandra/Scylla đã được dừng; volume dữ liệu được giữ nguyên. Không ghi nhận OOMKilled ở hai container.

```powershell
./Run-Benchmark.ps1 -Database "cassandra,scylla" -Ccu "5000" -MaxInflight 5000 -Rows 100000 -Duration 30 -Warmup 10 -Cooldown 2 -Repetitions 2 -SkipSeed
```

Bỏ SkipSeed nếu chưa có dataset. Raw JSON/CSV, metadata, image digest, docker stats và log ở validation-cassandra-scylla-5k-inflight/.
