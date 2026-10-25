# Kiểm tra thực tế — 24/10/2026

- Build Release .NET SDK 10.0.401: 0 warning, 0 error.
- Self-test: PASS (histogram, tham số CLI, giới hạn inflight, đếm lỗi/success, drain, RPS, cancellation).
- Docker Compose config: hợp lệ; build container .NET 10 thành công.
- PostgreSQL 17.6 và ScyllaDB 6.2.3: healthcheck PASS.
- Seed 10.000 dòng x 256 byte: xác thực thành công trên hai database.
- Smoke test cả 4 mức CCU, 1 lượt, warmup 1 giây, đo 3 giây, cooldown 1 giây: **8/8 scenario không có lỗi**, exit code 0.
- Script Run-Benchmark.ps1 chạy end-to-end với SkipSeed, 100 CCU, 1 giây: PASS trên cả hai DB.
- File kết quả nguyên bản và metadata được giữ trong validation/.

| DB | CCU | Success/s | p95 ms | p99 ms | Lỗi |
|---|---:|---:|---:|---:|---:|
| postgres | 100 | 8471.7 | 46.00 | 57.83 | 0 |
| scylla | 100 | 40390.4 | 6.35 | 19.55 | 0 |
| postgres | 1000 | 7690.3 | 200.60 | 265.05 | 0 |
| scylla | 1000 | 36251.1 | 40.42 | 48.35 | 0 |
| postgres | 5000 | 9977.3 | 570.26 | 581.72 | 0 |
| scylla | 5000 | 33209.8 | 249.69 | 360.82 | 0 |
| postgres | 10000 | 7388.9 | 1589.17 | 1605.07 | 0 |
| scylla | 10000 | 31637.9 | 410.64 | 418.90 | 0 |

**Chỉ là kiểm tra chức năng, không phải kết luận hiệu năng.** Dataset nhỏ, chạy rất ngắn, máy có workload khác, client giới hạn 2 CPU, Scylla chỉ dùng 1 GB bộ nhớ nội bộ. Không dùng các con số này để chọn database production. Chạy cấu hình 60 giây x 3 lượt (hoặc lâu hơn), ghi lại tài nguyên và xem cả độ biến thiên/tỷ lệ lỗi khi so sánh nghiêm túc.
