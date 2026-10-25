# .NET 10 vs Rust - JSON text API, 1 CPU / 1 GiB

## Thiet lap

Benchmark live HTTP local ngay 25/10/2026, khong phai ket qua gia lap.

- ASP.NET Core / .NET runtime 10.0.12, System.Text.Json, Release JIT, khong NativeAOT/source generation.
- Rust compiler 1.90, Axum 0.8.6, serde 1.0.229, serde_json 1.0.154, Tokio 1.54.1 voi 1 worker; release thin LTO. Cargo.lock dinh kem.
- Moi API container: CPU quota 1 core, RAM limit 1 GiB (1.073.741.824 byte). Da xac minh docker inspect va cgroup cpu.max=100000 100000, memory.max=1073741824. Khong pin physical core.
- Load generator rieng: 4 CPU / 2 GiB; khong nam trong quota API. Cung mot client cho hai API.
- POST /parse: UTF-8 text -> owned string -> typed object -> serialize JSON tra ve. 4096 byte/request, 24 nested items, strings ASCII, khong DB/cache/compression/TLS. Day la end-to-end HTTP, khong phai parser-only.
- HTTP/1.1 keep-alive, closed-loop zero think time; 100 / 1000 / 5000 CCU; 20 giay do + 5 giay warmup + 2 giay cooldown, 3 luot moi API/muc tai. Hai API nhan tai lan luot, dao thu tu o luot chan. So luot le khong can bang thu tu hoan toan.

## Tong hop

Median RPS cua 3 luot; p95 o bang nay la median cua 3 gia tri p95 tung luot, KHONG phai p95 gop. RPS tinh success / wall time bao gom drain.

| CCU | .NET request/s | .NET p95 ms | Rust request/s | Rust p95 ms | Rust/.NET RPS |
|---|---:|---:|---:|---:|---:|
| 100 | 4723.0 | 73.43 | 10378.3 | 13.94 | 2.20x |
| 1000 | 4666.2 | 467.35 | 10402.6 | 252.18 | 2.23x |
| 5000 | 6258.5 | 1110.71 | 12571.5 | 975.94 | 2.01x |

Tong 2,761,486 request thanh cong; 0 loi trong cac scenario do. Loi warmup (neu co) xem benchmark log; khong gop vao so lieu do.

Bien dong giua cac luot dang ke, dac biet o 100/1000 CCU; khong coi median la capacity on dinh. O 5000 CCU, Rust co throughput cao hon nhung p95/p99 khong thang trong moi luot.

## Tung luot

| API | CCU | Luot | Request/s | p50 ms | p95 ms | p99 ms | App peak | Errors | Client cores |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| dotnet | 100 | 1 | 4923.5 | 15.71 | 73.43 | 86.96 | 100 | 0 | 0.70 |
| rust | 100 | 1 | 10378.3 | 9.27 | 13.94 | 17.01 | 100 | 0 | 1.99 |
| rust | 100 | 2 | 11606.3 | 8.39 | 11.20 | 15.55 | 100 | 0 | 1.97 |
| dotnet | 100 | 2 | 4723.0 | 15.24 | 62.00 | 80.31 | 100 | 0 | 1.10 |
| dotnet | 100 | 3 | 1911.7 | 51.32 | 98.97 | 113.76 | 100 | 0 | 0.71 |
| rust | 100 | 3 | 4680.7 | 17.52 | 44.21 | 52.88 | 100 | 0 | 1.55 |
| dotnet | 1000 | 1 | 6978.6 | 128.19 | 249.69 | 295.71 | 1000 | 0 | 1.08 |
| rust | 1000 | 1 | 8274.0 | 113.76 | 252.18 | 336.54 | 1000 | 0 | 2.01 |
| rust | 1000 | 2 | 10402.6 | 51.32 | 262.42 | 402.55 | 1000 | 0 | 1.94 |
| dotnet | 1000 | 2 | 2189.3 | 467.35 | 791.91 | 866.10 | 1000 | 0 | 0.94 |
| dotnet | 1000 | 3 | 4666.2 | 188.97 | 467.35 | 593.41 | 1000 | 0 | 0.90 |
| rust | 1000 | 3 | 11088.8 | 78.73 | 183.41 | 226.04 | 1000 | 0 | 2.00 |
| dotnet | 5000 | 1 | 6480.5 | 768.62 | 1110.71 | 1214.77 | 5000 | 0 | 0.90 |
| rust | 5000 | 1 | 12571.5 | 190.86 | 975.94 | 1067.37 | 5000 | 0 | 2.14 |
| rust | 5000 | 2 | 10569.7 | 371.75 | 1239.19 | 1542.44 | 5000 | 0 | 2.08 |
| dotnet | 5000 | 2 | 5930.1 | 799.83 | 1190.83 | 1289.50 | 5000 | 0 | 0.78 |
| dotnet | 5000 | 3 | 6258.5 | 799.83 | 975.94 | 1190.83 | 5000 | 0 | 0.78 |
| rust | 5000 | 3 | 13055.5 | 178.02 | 866.10 | 985.70 | 5000 | 0 | 2.15 |

App peak la so client calls chua hoan tat, gom client/network queue; khong phai so query thuc thi CPU song song. Latency la all-attempt end-to-end, histogram upper bucket sai so khoang 1%. JSON co them success-only latency, bytes, sampled checks va error types.

## CPU / RAM quan sat

Docker stats samples trong cua so do tung scenario (khong warmup). Lay mau khoang 10 giay, timestamp truoc lenh stats nen co sai lech bien cua so. RAM la working-set Docker stats, khong phai RSS rieng hay peak tuyet doi; CPU median theo mau, 100% xap xi 1 core.

| API | CCU | So mau | RAM peak quan sat MiB | CPU median % |
|---|---:|---:|---:|---:|
| dotnet | 100 | 4 | 42.04 | 106.3 |
| rust | 100 | 5 | 4.60 | 101.2 |
| dotnet | 1000 | 6 | 71.22 | 101.6 |
| rust | 1000 | 6 | 31.54 | 100.7 |
| dotnet | 5000 | 6 | 189.10 | 99.6 |
| rust | 5000 | 6 | 159.70 | 98.9 |

## Validation va gioi han

- Kiem tra bo sung: 6 cases/API PASS (raw UTF-8 Unicode, content-type, int overflow, nested schema).
- Smoke live: 16 cases/API PASS; round-trip va schema rejection, null/type/UTF-8/body-size checks.
- Postman collection local: 3 requests, 4 assertions/API PASS. Khong publish len workspace. CLI --output yeu cau login nen chi luu stdout local.
- Moi response trong load duoc doc het body va check status; semantic equality kiem tra baseline va khoang 1/1024 successful responses moi worker, khong check full object moi request.
- Cac build .NET Release: 0 warning/error. Rust release build thanh cong. Compose config hop le.
- Xem tung luot: host Docker Desktop/WSL2 co workload khac; co bien dong throughput, JIT/PGO, GC/allocator va CPU throttling. Ba luot ngan khong du ket luan production capacity hay universal language ranking.
- Rust AOT release va .NET JIT + serializer mac dinh la hai implementation cu the. Khong suy ra serde_json nhanh hon System.Text.Json rieng le tu benchmark HTTP nay.
- HttpClient timeout 15s den headers; body read chi theo run cancellation, khong co timeout body rieng. Khong coi day la benchmark hard-deadline request.

Khong co OOMKilled/restart tren hai API. Da dung rieng hai container api-json-bench, khong dung DB/container khac. Rust demo khong dang ky SIGTERM handler: Docker stop da doi timeout roi terminate (exit 137 sau khi do xong), khong phai OOM trong benchmark. Load generator chay runtime .NET 10.0.13; API .NET chay ASP.NET Core 10.0.12 nhu diagnostics.

## Tai chay

Xem README.md va Run-Benchmark.ps1. Raw CSV/JSON, metadata, logs, source manifest va resource samples o validation/.
