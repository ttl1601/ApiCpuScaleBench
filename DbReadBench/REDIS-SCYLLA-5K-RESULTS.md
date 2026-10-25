# Redis vs ScyllaDB - 5.000 CCU / max-inflight 5.000

## Pham vi

Benchmark .NET 10 thuc te ngay 25/10/2026. Redis 8.2.2, StackExchange.Redis 3.2.0; ScyllaDB 6.2.3, CassandraCSharpDriver 3.23.0. Khong dung ket qua Cassandra cu de so sanh.

- Moi DB single-node va client: 2 CPU / 3 GiB. Redis maxmemory 1 GiB noeviction; Scylla internal memory 1 GiB. Day khong phai hai cach tinh bo nho tuong duong.
- 100.000 ID, payload 256 byte ky tu x; seed va kiem tra tat ca ID o ca hai DB. Redis GET va CQL prepared primary-key SELECT tra ve payload, khong client cache.
- Closed-loop, zero think time, uniform random ID; 5.000 users va cap 5.000 ReadAsync active. Khong phai 5.000 connection/thread hay 5.000 query thuc thi CPU cung luc. Redis dung shared multiplexer, Scylla 4 query connections.
- 30 giay do + 10 giay warmup moi scenario, cooldown 2 giay, 3 luot moi DB; thu tu redis/scylla, scylla/redis, redis/scylla. DB nhan tai lan luot; thu tu khong can bang hoan toan vi so luot le.
- Redis AOF everysec, snapshots disabled; Scylla RF1/ONE. Khong coi persistence/durability la tuong duong; day la read-only warm-cache benchmark.

## Ket qua tung luot

RPS = successful requests / actual wall time gom drain. Percentile theo moi luot, histogram xap xi upper bound 1%.

| DB | Luot | Read/s | p50 ms | p95 ms | p99 ms | App peak | Driver peak | Errors | Client CPU cores |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| redis | 1 | 119180.4 | 29.69 | 87.83 | 270.38 | 5000 | N/A | 0 | 1.87 |
| scylla | 1 | 24200.1 | 185.25 | 427.32 | 599.34 | 5000 | 4895 | 0 | 1.62 |
| scylla | 2 | 9879.5 | 418.90 | 1179.04 | 1637.33 | 5000 | 4450 | 0 | 1.65 |
| redis | 2 | 127382.0 | 27.69 | 77.17 | 91.40 | 5000 | N/A | 0 | 1.99 |
| redis | 3 | 165348.7 | 19.36 | 67.81 | 74.90 | 5000 | N/A | 0 | 1.99 |
| scylla | 3 | 24253.7 | 178.02 | 458.14 | 668.67 | 5000 | 4271 | 0 | 1.72 |

Median throughput Redis: 127382.0 read/s; Scylla: 24200.1 read/s. Ty le median Redis/Scylla: 5.26x, chi cho cau hinh va luot do nay; khong phai ket luan Redis luon nhanh hon.

Redis driver telemetry khong duoc instrument: N/A khong phai 0. App peak bao gom Redis client queue, khong chung minh 5.000 request tren wire/server. Scylla driver snapshot 20ms cung khong phai so query dang thuc thi CPU.

## Gioi han va validation

Bien dong throughput giua cac luot: Redis 119180-165349 read/s, Scylla 9880-24254 read/s. Scylla luot 2 cham hon ro ret; chua co bang chung de quy nguyen nhan. Redis client dat 1,87-1,99 CPU cores tren cap 2: co dau hieu bottleneck phia client, khong coi so do la tran server Redis.

Tong 14.121.920 request do thanh cong, 0 loi; khong ghi nhan OOMKilled o hai DB. Sau test chi dung hai container benchmark Redis/Scylla, giu volumes va khong dung Redis khac tren may.

Build Release 0 warning/error, self-test PASS, Docker Compose config hop le. Redis/Scylla cung duoc chay lai trong mot batch moi. Client 2 CPU va driver/serialization/network co the gioi han ket qua. Dataset nho vua RAM, payload de nen; khong danh gia disk-bound, writes, failover, multi-node, production capacity. Ba luot ngan khong du lap khoang tin cay; xem tung luot thay vi chi trung binh.

Raw JSON/CSV, metadata va diagnostics o validation-redis-scylla-5k/.

## Tai chay

```powershell
./Run-Benchmark.ps1 -Database 'redis,scylla' -Ccu '5000' -MaxInflight 5000 -Rows 100000 -Duration 30 -Warmup 10 -Cooldown 2 -Repetitions 3
```
