# DbReadBench — .NET 10 / PostgreSQL / MongoDB / Cassandra / ScyllaDB

Tool console benchmark đọc theo primary key, chạy các database được chọn và bộ tạo tải trong Docker Compose. Không có HTTP/API ở giữa.

## Chạy nhanh (PowerShell)

Cần Docker Desktop ở chế độ Linux containers. Khuyến nghị cấp Docker ít nhất 6 CPU, 10–12 GB RAM và đủ dung lượng đĩa để tải image. Cần còn ít nhất khoảng 4 GB RAM khả dụng cho các process lab, không chỉ tổng RAM được cấp. Đây là cấu hình lab, không phải cấu hình production.

```powershell
./Run-Benchmark.ps1
```

Script khởi động DB, build .NET 10 container, seed và benchmark. Mặc định: **100 / 1.000 / 5.000 / 10.000 CCU**, 100.000 dòng, payload ASCII 256 byte, warmup 15 giây, đo 60 giây, nghỉ 5 giây, 3 lượt/mức/DB. Tổng thời gian đo + warmup + nghỉ khoảng 32 phút, chưa gồm build, seed, xác thực dataset và drain.

Chạy nhanh để kiểm tra thiết lập:

```powershell
./Run-Benchmark.ps1 -Rows 10000 -Duration 5 -Warmup 2 -Repetitions 1
```

## 5.000 request in-flight với Cassandra/Scylla

```powershell
./Run-Benchmark.ps1 -Database "cassandra,scylla" -Ccu "5000" -MaxInflight 5000 -Rows 100000 -Duration 30 -Warmup 10 -Cooldown 2 -Repetitions 2
```

Khác cấu hình cũ 5.000 CCU nhưng cap 256, lệnh này cho phép 5.000 user cùng có request chưa hoàn tất trong lời gọi driver. Đây không phải 5.000 connection, thread hoặc query đang chạy CPU cùng lúc ở server. Dùng SkipSeed nếu dataset đúng đã tồn tại.

Cap trên 256 chỉ hỗ trợ khi toàn bộ DB được chọn là Redis/Cassandra/Scylla, tối đa 10.000. PostgreSQL/MongoDB vẫn giới hạn 256; alias both có PostgreSQL nên không dùng với cap 5.000.

Pool CQL tự tăng theo max-inflight: mặc định 256 dùng 2 connection query; cap 5.000 dùng 4 connection query, mỗi connection tối đa 2.048 request, tổng 8.192 slot để có headroom. Cả hai DB dùng cùng cách tính, protocol V4, consistency ONE và tắt retry.

JSON/CSV bổ sung DriverPeakInflight, DriverMeanInflight, DriverPeakConnections và DriverSamples. Đây là snapshot driver GetState, mục tiêu mỗi 20 ms, gồm cả drain; scheduler có thể làm trễ việc lấy mẫu. Peak snapshot là peak quan sát được, không đảm bảo bắt mọi thời điểm; mean là trung bình theo mẫu, không phải trung bình có trọng số thời gian. Snapshot có thể gồm request nội bộ driver, và không đo số thread/query thực thi CPU bên trong DB. PeakInflight cũ vẫn là số lời gọi ReadAsync đang active ở application.

## Cassandra so với ScyllaDB

```powershell
./Run-Benchmark.ps1 -Database "cassandra,scylla" -Rows 100000 -PayloadBytes 256 -Duration 15 -Warmup 5 -Cooldown 2 -Repetitions 2
```

Cassandra dùng image cassandra:5.0.8, single node, giới hạn 2 CPU / 3 GB RAM, JVM heap 1 GB; Scylla giữ image 6.2.3 và bộ nhớ nội bộ 1 GB. Heap JVM không phải tổng RAM của Cassandra và không tương đương bộ nhớ nội bộ Scylla. Không coi cấu hình lab này là cấu hình production tối ưu.

Hai DB dùng chung CqlStore và CassandraCSharpDriver 3.23.0, cùng native protocol V4, prepared SELECT, consistency ONE, RF=1, mặc định hai connection local và tắt retry policy. Giới hạn request đang gọi driver mặc định vẫn là 256; có thể tăng cho CQL như phần 5.000 in-flight.

Container Cassandra publish cổng localhost:19042 để không xung đột Scylla localhost:9042. Benchmark trong Compose kết nối cassandra:9042 qua CASSANDRA_HOST và CASSANDRA_PORT=9042. Khi chạy .NET trên host, Cassandra mặc định localhost:19042; có thể ghi đè CASSANDRA_HOST/CASSANDRA_PORT hoặc SCYLLA_HOST/SCYLLA_PORT. Không bật auth/TLS trong lab; không expose ra mạng public. Dữ liệu Cassandra lưu riêng trong volume cassandra-data.

Chạy từng bước:

```powershell
docker compose up -d --wait --wait-timeout 300 cassandra scylla
docker compose build benchmark
docker compose run --rm benchmark seed --database cassandra,scylla --rows 100000 --payload-bytes 256
docker compose run --rm benchmark run --database cassandra,scylla --rows 100000 --payload-bytes 256 --ccu 100,1000,5000,10000 --duration 15 --warmup 5 --cooldown 2 --repetitions 2 --output /results
```

Các giới hạn warm-cache, closed-loop, payload dễ nén, hàng đợi phía client và ảnh hưởng JVM/JIT/GC đều phải được cân nhắc. Muốn so sánh dài hạn nên tăng duration/warmup/repetitions và theo dõi CPU, GC, compaction, disk IO. Phiên bản image được chọn để chạy lab, không phải tuyên bố mới nhất.

## MongoDB 8 so với ScyllaDB

Tool cũng hỗ trợ MongoDB, không thay đổi ý nghĩa cũ của --database both (PostgreSQL + ScyllaDB).

```powershell
./Run-Benchmark.ps1 -Database "mongodb,scylla" -Rows 100000 -PayloadBytes 256 -Duration 15 -Warmup 5 -Cooldown 2 -Repetitions 2
```

Script chỉ khởi động các DB được chọn. Container benchmark không tự kéo dependency lên; nếu gọi docker compose run trực tiếp, cần chạy up --wait cho các DB trước.

Chạy thủ công:

```powershell
docker compose up -d --wait --wait-timeout 300 mongodb scylla
docker compose build benchmark
docker compose run --rm benchmark seed --database mongodb,scylla --rows 100000 --payload-bytes 256
docker compose run --rm benchmark run --database mongodb,scylla --rows 100000 --payload-bytes 256 --ccu 100,1000,5000,10000 --duration 15 --warmup 5 --cooldown 2 --repetitions 2 --output /results
```

MongoDB dùng image mongo:8.0 (patch thay đổi theo lần pull), standalone, giới hạn 2 CPU / 3 GB, WiredTiger cache 1 GB; Scylla cùng giới hạn container, bộ nhớ nội bộ 1 GB. MONGO_CONNECTION có thể ghi đè địa chỉ kết nối. MongoDB lab không bật auth, chỉ publish cổng trên localhost; không dùng trên mạng public.

Dataset MongoDB là collection bench.read_items, _id kiểu int và payload ASCII giống Scylla. Seed upsert theo batch 1.000 dòng; run xác thực số dòng trong khoảng ID và độ dài payload. Read là find theo _id với projection chỉ payload, tận dụng index _id mặc định. Driver dùng primary/local, tắt retry read/write và pool tối đa bằng max-inflight. Không so sánh semantics replication/HA từ các cấu hình single-node này.

Có thể chọn một DB hoặc danh sách: --database mongodb; --database mongodb,scylla; --database postgres,mongodb,scylla. Để so sánh, các DB nhận tải lần lượt, không đồng thời. RPS gồm client/driver/network/materialization; không phải công suất server thuần. Các giới hạn closed-loop, warm-cache và payload dễ nén vẫn áp dụng.

## Chạy từng bước

Trong thư mục chứa compose.yaml:

```powershell
docker compose up -d --wait --wait-timeout 300 postgres scylla
docker compose build benchmark
docker compose run --rm benchmark self-test
docker compose run --rm benchmark seed --rows 100000 --payload-bytes 256
docker compose run --rm benchmark run --ccu 100,1000,5000,10000 --rows 100000 --payload-bytes 256 --duration 60 --warmup 15 --repetitions 3 --output /results
```

Chỉ đo một DB hoặc thay giới hạn concurrency:

```powershell
docker compose run --rm benchmark run --database scylla --max-inflight 128 --output /results
docker compose run --rm benchmark run --database postgres --max-inflight 128 --output /results
```

Phải dùng cùng rows/payload-bytes lúc seed và run. Seed upsert các ID 1..rows, **không xóa** các ID cũ ngoài khoảng này. Run chỉ kiểm tra/đọc, không tạo schema hay ghi dữ liệu. Scylla xác thực toàn bộ ID, PostgreSQL kiểm tra số dòng đúng độ dài trong khoảng. Không dùng database chứa dữ liệu thật: schema bench/read_items là dành riêng cho tool.

## Ý nghĩa CCU và tính công bằng

- Một CCU là một tác vụ người dùng async: đọc xong mới gửi lần tiếp theo, không think time. Không tạo 10.000 thread hay 10.000 kết nối.
- Đây là **closed-loop**, không phải fixed arrival rate hoặc phép đo loại bỏ coordinated omission. Không suy diễn kết quả thành SLA của tải open-loop.
- Cả hai DB chạy đồng thời, nhưng **chỉ một DB nhận tải benchmark tại một thời điểm** để hạn chế tranh chấp CPU. Thứ tự DB đảo ở lượt lặp chẵn.
- Giới hạn application-level mặc định 256 request đang gọi driver; CCU còn lại chờ semaphore. PostgreSQL/MongoDB chỉnh 1..256; Redis/Cassandra/Scylla chỉnh 1..10000. Mức 10.000 CCU KHÔNG tự động có nghĩa 10.000 request đang gọi driver hoặc 10.000 query thực thi CPU đồng thời trên server.
- PostgreSQL pool tối đa bằng max-inflight; dùng SELECT có parameter và auto-prepare sau 2 lần. Scylla dùng prepared statement, 2 kết nối multiplexed, consistency ONE, RF=1 và tắt retry policy. Không ép hai driver cùng số TCP connection vì mô hình giao thức khác nhau.
- Mỗi DB giới hạn 2 CPU / 3 GB RAM; Scylla dùng 2 shard / 1 GB bộ nhớ nội bộ; PostgreSQL shared_buffers 512 MB. Giới hạn bằng nhau không đồng nghĩa cấu hình tối ưu bằng nhau.
- Timeout hàng đợi và timeout driver là hai budget riêng (mặc định mỗi budget 30 giây). Scylla driver không hỗ trợ hủy request đã gửi bằng CancellationToken; Ctrl+C chờ các request đó hoàn tất/timeout rồi thoát.
- Warmup và kiểm tra dữ liệu không được tính vào số liệu. Đây là **warm-cache point-read**, không phải cold disk scan/join/range query. Payload gồm ký tự x lặp lại, dễ nén; không đại diện entropy dữ liệu thật. Không so sánh dung lượng lưu trữ từ dataset này.
- Request bắt đầu trước deadline đều được chờ hoàn tất, kể cả request còn trong hàng đợi. Không hủy tất cả ở ranh giới đo để làm mất tail latency. AdmissionSeconds là thời gian cho phép phát sinh request; WallSeconds gồm drain.

## Kết quả

Mỗi lần chạy tạo thư mục riêng trong results/:

- metadata.json: tham số, OS/runtime, số CPU client thấy, mô hình đo và giả định.
- results.json: toàn bộ từng scenario; latency tất cả attempts, success-only, queue, service; lỗi theo loại; CPU client, allocation, GC.
- results.csv: bảng gọn để mở Excel hoặc tổng hợp theo DB/CCU/lượt.

SuccessRps = Success / WallSeconds, **bao gồm drain**. ErrorPercent = Errors / (Success + Errors). p50/p95/p99 ở console/CSV tính cho tất cả attempts, gồm lỗi và thời gian chờ queue. Percentile dùng histogram logarit, độ phân giải khoảng 1%, không giữ mọi sample trong RAM. Mean và Max dùng đồng hồ monotonic thực tế. QueueLatency đo chờ semaphore; ServiceLatency gồm pool/driver/network/database/materialization, không phải server-only latency.

Xem cả tỷ lệ lỗi và success-only latency; không coi RPS cao khi lỗi nhiều là tốt. Kết quả có lỗi trả exit code 2, lỗi thiết lập trả 1, Ctrl+C trả 130. Scenario đã hoàn tất được lưu sau từng lượt; lượt bị hủy bị bỏ.

## Quan sát tài nguyên

Mở terminal khác khi chạy:

```powershell
docker stats
docker compose logs --tail 100 postgres scylla
```

Client được giới hạn 2 CPU / 3 GB trong Compose. Với 10k CCU, client/GC/network có thể thành bottleneck; hãy theo dõi client CPU và docker stats, tăng tài nguyên client khi cần và ghi lại thay đổi. CPU được xuất là CPU của process .NET, **không phải CPU database**. Metadata không tự thu phiên bản server/image digest hoặc CPU/IO server; hãy lưu docker inspect, docker info và cấu hình Compose kèm kết quả khi công bố so sánh. Kết quả local Docker không chứng minh DB nào nhanh hơn trong production.

## Chạy .NET trực tiếp

```powershell
dotnet build -c Release
dotnet run -c Release --no-build -- self-test
dotnet run -c Release --no-build -- help
```

PG_CONNECTION, MONGO_CONNECTION và SCYLLA_HOST có thể ghi đè địa chỉ mặc định localhost. Khuyến nghị chạy benchmark bằng Compose: discovery của Cassandra driver có thể nhận địa chỉ container mà Windows host không truy cập được. Credential mặc định chỉ dùng local lab; cổng DB chỉ bind 127.0.0.1; không dùng credential này trên môi trường public.

## Dừng và dọn dẹp

```powershell
docker compose down
```

Lệnh trên giữ dữ liệu trong named volumes. Chỉ khi muốn xóa dữ liệu benchmark: docker compose down -v (xóa vĩnh viễn các volume của project). Tool không tự chạy lệnh xóa dữ liệu.

## Tài liệu tham chiếu

- Npgsql: https://www.npgsql.org/doc/basic-usage.html
- Cassandra C# driver: https://github.com/datastax/csharp-driver
- .NET container: https://learn.microsoft.com/dotnet/core/docker/build-container
- Scylla Docker: https://hub.docker.com/r/scylladb/scylla

PostgreSQL/ScyllaDB và NuGet dùng phiên bản cụ thể; image SDK/runtime .NET dùng tag 10.0 (patch có thể thay đổi khi pull). Đây không phải tuyên bố phiên bản mới nhất. Chỉ sử dụng cho lab; kiểm tra hỗ trợ/bản vá trước khi tái sử dụng ngoài lab.
## Redis vs ScyllaDB

Redis uses a shared StackExchange.Redis multiplexer and GET, no client cache.
Docker Redis 8.2.2 has AOF everysec, snapshots disabled, maxmemory 1gb, noeviction.
Each service and benchmark client is capped at 2 CPU and 3 GiB.
Redis persistence is not equivalent to Scylla durability. This is a warm-cache
single-node point-read comparison, not a write durability or HA test. Redis
driver telemetry is unavailable; application PeakInflight includes client queues.

```powershell
./Run-Benchmark.ps1 -Database "redis,scylla" -Ccu "5000" -MaxInflight 5000 -Duration 30 -Warmup 10 -Repetitions 3
```
