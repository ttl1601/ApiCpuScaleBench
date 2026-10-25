FROM debian:trixie-slim AS build
RUN apt-get update && apt-get install -y --no-install-recommends g++ cmake make libdrogon-dev libjsoncpp-dev libssl-dev zlib1g-dev uuid-dev nlohmann-json3-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY . .
RUN cmake -S . -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build -j2 && dpkg-query -W > /src/packages.txt && g++ --version > /src/compiler.txt
FROM debian:trixie-slim
RUN apt-get update && apt-get install -y --no-install-recommends libdrogon1t64 && rm -rf /var/lib/apt/lists/*
COPY --from=build /src/build/json-api /app/json-api
COPY --from=build /src/packages.txt /src/compiler.txt /app/
ENTRYPOINT ["/app/json-api"]
