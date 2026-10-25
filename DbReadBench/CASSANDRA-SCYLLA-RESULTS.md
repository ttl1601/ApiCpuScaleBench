# Apache Cassandra vs ScyllaDB — 25/10/2026

## Cấu hình và cách đo

- Cassandra **5.0.8** và ScyllaDB **6.2.3**, mỗi DB một node. Image digest và phiên bản thực tế giữ trong environment.json. Đây không phải tuyên bố các phiên bản mới nhất.
- Mỗi DB: giới hạn 2 CPU / 3 GB RAM. Cassandra JVM heap 1 GB, JVM ActiveProcessorCount=2, num_tokens=16. Scylla 2 shard, bộ nhớ nội bộ 1 GB. **Heap JVM không phải tổng RAM Cassandra, cũng không tương đương bộ nhớ nội bộ Scylla**. Client .NET 10: giới hạn 2 CPU / 3 GB RAM.
- Docker engine: 8 CPU, RAM 12.379.713.536 byte. Có thêm hai container Baget và Redis ngoài bài test; môi trường không hoàn toàn cô lập.
- Hai DB dùng **cùng CqlStore, CassandraCSharpDriver 3.23.0, native protocol V4**, 2 connection local, tối đa 2048 request/connection; cùng prepared SELECT payload FROM bench.read_items WHERE id = ?, consistency ONE, RF=1, tắt retry policy.
- Dataset: 100.000 ID nguyên, payload ASCII 256 byte gồm ký tự x lặp lại. Seed upsert và xác thực tất cả ID trước khi chạy.
- 100 / 1.000 / 5.000 / 10.000 CCU; 2 lượt mỗi DB/mức tải. Mỗi scenario: warmup 5 giây, đo 15 giây, cooldown 2 giây. Thứ tự DB đảo ở lượt thứ hai; hai DB nhận tải lần lượt.
- CCU là user async closed-loop, zero think time. Application max-inflight=256; 10.000 CCU không phải 10.000 query/connection đồng thời trên server.

## Tổng hợp

RPS là **trung bình số học của hai lượt**, mỗi lượt Success/WallSeconds, kể cả drain. p95 là **khoảng min–max p95 của hai lượt**, không phải p95 gộp. Latency bao gồm queue + driver + network + database.

| CCU | Cassandra RPS TB | ScyllaDB RPS TB | Scylla/Cassandra | Cassandra p95 ms | ScyllaDB p95 ms |
|---:|---:|---:|---:|---:|---:|
| 100 | 26125.4 | 43813.2 | 1.68x | 6.61–7.75 | 4.85–5.92 |
| 1000 | 24943.4 | 41793.5 | 1.68x | 69.87–76.41 | 32.15–33.13 |
| 5000 | 20556.9 | 36430.8 | 1.77x | 298.66–310.79 | 169.38–174.51 |
| 10000 | 19967.8 | 33957.7 | 1.70x | 605.34–611.39 | 339.91–390.71 |

Tổng **7,490,422 request thành công, 0 lỗi**, 16 scenario. Tất cả count latency khớp success + errors; peak-inflight không vượt 256. ErrorTypes và success-only latency có trong results.json.

## Từng lượt

| DB | CCU | Lượt | RPS | p95 ms | p99 ms | Drain s | Lỗi |
|---|---:|---:|---:|---:|---:|---:|---:|
| cassandra | 100 | 1 | 26283.3 | 6.61 | 58.41 | 0.001 | 0 |
| scylla | 100 | 1 | 49076.1 | 4.85 | 7.09 | 0.001 | 0 |
| scylla | 100 | 2 | 38550.3 | 5.92 | 11.54 | 0.001 | 0 |
| cassandra | 100 | 2 | 25967.4 | 7.75 | 51.83 | 0.001 | 0 |
| cassandra | 1000 | 1 | 27271.5 | 69.87 | 79.51 | 0.070 | 0 |
| scylla | 1000 | 1 | 41528.8 | 33.13 | 40.42 | 0.023 | 0 |
| scylla | 1000 | 2 | 42058.1 | 32.15 | 38.46 | 0.021 | 0 |
| cassandra | 1000 | 2 | 22615.3 | 76.41 | 85.25 | 0.024 | 0 |
| cassandra | 5000 | 1 | 20287.0 | 310.79 | 364.42 | 0.299 | 0 |
| scylla | 5000 | 1 | 36471.1 | 169.38 | 183.41 | 0.131 | 0 |
| scylla | 5000 | 2 | 36390.6 | 174.51 | 187.10 | 0.120 | 0 |
| cassandra | 5000 | 2 | 20826.8 | 298.66 | 307.71 | 0.268 | 0 |
| cassandra | 10000 | 1 | 20246.8 | 605.34 | 695.82 | 0.515 | 0 |
| scylla | 10000 | 1 | 35353.1 | 339.91 | 360.82 | 0.240 | 0 |
| scylla | 10000 | 2 | 32562.3 | 390.71 | 423.09 | 0.224 | 0 |
| cassandra | 10000 | 2 | 19688.8 | 611.39 | 784.07 | 0.576 | 0 |

## CPU client

Giá trị là trung bình CPU-seconds / wall-seconds qua hai lượt, đơn vị core. Client giới hạn 2 core; gần 2 core cho thấy bộ tạo tải/driver có thể thành bottleneck. Không phải CPU database.

| CCU | Client khi đo Cassandra | Client khi đo Scylla |
|---:|---:|---:|
| 100 | 1.11 | 1.71 |
| 1000 | 1.16 | 1.69 |
| 5000 | 1.06 | 1.59 |
| 10000 | 1.07 | 1.60 |

Snapshot docker stats được lưu trong resource-samples.jsonl, cách khoảng 40 giây, gồm cả warmup/cooldown. Không coi snapshot là CPU trung bình của từng scenario. Nodetool info trước/sau lưu heap, cache và trạng thái Cassandra.

## Giới hạn diễn giải

- Đây là kết quả **end-to-end của đúng cấu hình lab**, không phải benchmark công suất database thuần hoặc kết luận DB nào luôn nhanh hơn. Cùng giới hạn container không có nghĩa hai DB được tune tối ưu tương đương.
- Dataset nhỏ, payload dễ nén, warm-cache point-read theo primary key. Không đánh giá scan, range query lớn, write workload, multi-node, replication, failover hoặc dữ liệu vượt RAM.
- Cassandra dùng volume mới; Scylla tái sử dụng volume benchmark cũ. Cả hai upsert cùng dataset rồi xác thực/warmup, nhưng không ép flush/compaction hay xóa disk cache. Trạng thái memtable/SSTable có thể khác.
- Warmup 5 giây và hai lượt 15 giây chưa đủ để loại ảnh hưởng JVM JIT/GC, dao động hệ thống hoặc lập khoảng tin cậy. Xem từng lượt thay vì chỉ giá trị trung bình.
- Ở CCU cao, hàng đợi phía client có thể chi phối p95. Xem QueueLatency và ServiceLatency trong results.json; ServiceLatency vẫn gồm driver/network, không phải server-only latency.
- Không gộp bảng này với các lượt MongoDB/PostgreSQL trước: ngày chạy, trạng thái hệ thống, phiên bản source và thiết lập protocol có thể khác.

Muốn quyết định production: tăng thời gian đo 60–300 giây, warmup dài hơn, 3–5 lượt trở lên; kiểm tra bottleneck client, JVM GC, compaction, IO và dùng dataset/workload đại diện ứng dụng.

## Kiểm tra triển khai

- Build Release: 0 warning, 0 error. Self-test PASS, bao gồm chọn cặp Cassandra/Scylla và giữ tương thích các cặp cũ.
- Script PowerShell chạy end-to-end cho cặp Cassandra/Scylla ở 100 CCU: PASS, không làm đổi thư mục terminal. Lượt riêng 1 giây này không trộn vào bảng đo 15 giây.
- Cả hai schema CQL gốc, nodetool info trước/sau và gcstats sau chạy được lưu kèm. gcstats bao gồm cả startup/seed/warmup, không phải GC riêng của từng scenario.
- Trước khi dừng, cả hai container đang chạy và OOMKilled=false. Container lab được dừng sau kiểm tra, không xóa volume/dữ liệu.

## Tái chạy

```powershell
./Run-Benchmark.ps1 -Database "cassandra,scylla" -Rows 100000 -PayloadBytes 256 -Duration 15 -Warmup 5 -Cooldown 2 -Repetitions 2
```

Raw data và cấu hình: validation-cassandra-scylla/. Cassandra expose localhost:19042; Scylla localhost:9042. Trong Docker network, cả hai dùng native port 9042 trên hai hostname khác nhau.
